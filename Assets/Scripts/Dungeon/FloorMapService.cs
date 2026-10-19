using AbsorbRoguelike.Localization;
using Edgar.Unity;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// Tab 지도 데이터와 지도 순간이동(리팩터 C2, 예전 DungeonGameManager에서 분리). 서버가 방·복도 사각형 목록(<see cref="MapRooms"/>,
    /// 방 종류·클리어·파티 공유 방문)을 만들어 SyncList로 보낸다(Tab 지도 2차, docs/04_구현위키/탭지도.md) — 타일맵 전체가 아니라 사각형 목록뿐이다.
    /// 순간이동 요청(<see cref="CmdRequestMapTeleport"/>)을 검증하고, 착지점 찾기·소유 클라이언트 위치 쓰기(<see cref="TryFindLandingPoint"/>·
    /// <see cref="TargetApplyMapTeleport"/>)는 방 합류(RoomLockSync)·시작 방 배치(LevelGenerator)도 같이 쓴다.
    /// 컴포넌트 순서 4번째(지도 SyncList는 다른 컴포넌트 값을 훅에서 읽지 않음).
    /// </summary>
    public class FloorMapService : NetworkBehaviour
    {
        /// <summary>
        /// Tab 지도 한 칸(방 또는 복도). bounds는 월드 좌표 사각형(방: 입장 트리거 콜라이더 bounds, 복도: 타일맵 범위).
        /// kind: <see cref="MapKindRoom"/> 0 / <see cref="MapKindCorridor"/> 1 / <see cref="MapKindSpawn"/> 2 / <see cref="MapKindBoss"/> 3.
        /// visited는 파티 공유(누구든 한 명이 들어가면 true), cleared는 서버 RoomClearController 판정.
        /// </summary>
        public struct MapRoom
        {
            public Rect bounds;
            public byte kind;
            public bool cleared;
            public bool visited;
        }

        public const byte MapKindRoom = 0;
        public const byte MapKindCorridor = 1;
        public const byte MapKindSpawn = 2;
        public const byte MapKindBoss = 3;

        // Tab 지도 3차: 순간이동 거부 사유 코드(TargetMapTeleportDenied로 보낸다).
        public const byte TeleportDenyCombat = 1;
        public const byte TeleportDenyNotCleared = 2;
        public const byte TeleportDenyDead = 3;
        public const byte TeleportDenyNoLanding = 4;
        public const byte TeleportDenyCooldown = 5;

        private const float MapVisitCheckInterval = 0.2f;
        private const float LandingStep = 0.5f;
        private const float DefaultPlayerRadius = 0.4f;

        [Header("Tab 지도 순간이동 (3차)")]
        [Tooltip("[PLAYTEST] 같은 플레이어가 지도 순간이동을 다시 쓰기까지 걸리는 시간(초). 0이면 제한 없음(2026-09-29 임시).")]
        [SerializeField] private float teleportCooldown = 0f;

        // Tab 지도 2차: 이 층의 방·복도 사각형 목록(서버가 BuildMapRooms로 채우고 클리어·방문 때 항목을 교체한다).
        private readonly SyncList<MapRoom> mapRooms = new SyncList<MapRoom>();
        // 서버 전용: mapRooms 인덱스 ↔ RoomClearController(없으면 null)와 그 Cleared 구독 핸들러(층 재구성 때 해제용).
        private readonly System.Collections.Generic.List<RoomClearController> _mapClears = new System.Collections.Generic.List<RoomClearController>();
        private readonly System.Collections.Generic.List<System.Action> _mapClearHandlers = new System.Collections.Generic.List<System.Action>();
        private float _mapVisitTimer;
        // Tab 지도 3차(서버 전용, 동기화 안 함): mapRooms 인덱스별 인접 칸(Edgar Doors 기준 — 방이면 붙은 복도, 복도면 붙은 방)과
        // 방 입장 트리거 콜라이더(복도·트리거 없는 방은 null, 순간이동 착지점 계산용). 연결별 마지막 순간이동 시각(쿨타임용).
        private readonly System.Collections.Generic.List<System.Collections.Generic.List<int>> _mapNeighbors = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
        private readonly System.Collections.Generic.List<Collider2D> _mapAreas = new System.Collections.Generic.List<Collider2D>();
        private readonly System.Collections.Generic.Dictionary<int, double> _lastTeleportTime = new System.Collections.Generic.Dictionary<int, double>();

        public static FloorMapService Instance { get; private set; }

        /// <summary>Tab 지도용 방·복도 목록(읽기 전용). 호스트·원격 클라이언트 모두 같은 값(SyncList).</summary>
        public System.Collections.Generic.IReadOnlyList<MapRoom> MapRooms => mapRooms;

        /// <summary>MapRooms가 바뀔 때마다(추가·교체·비움) 발생한다. 호스트는 서버 변경 즉시, 원격 클라이언트는 동기화 수신 때.</summary>
        public static event System.Action OnMapChanged;

        /// <summary>지도 순간이동이 서버에서 거부됐을 때(요청한 클라이언트에서만). 인자: 화면에 띄울 안내 문구.
        /// 방 합류 안내(RoomLockSync.TargetShowNotice)도 같은 줄에 띄운다.</summary>
        public static event System.Action<string> OnMapTeleportDenied;

        private void Awake()
        {
            Instance = this;
            mapRooms.Callback += (op, index, oldItem, newItem) => OnMapChanged?.Invoke();
        }

        private void OnDestroy()
        {
            UnsubscribeMapClears();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // 방문 갱신(서버, 0.2초 간격). 층 생성 중(DungeonGameManager.IsGenerating)이거나 지도가 비면 건너뛴다.
        private void Update()
        {
            if (!isServer || DungeonGameManager.Instance.IsGenerating || mapRooms.Count == 0)
            {
                return;
            }

            _mapVisitTimer -= Time.deltaTime;
            if (_mapVisitTimer > 0f)
            {
                return;
            }

            _mapVisitTimer = MapVisitCheckInterval;
            UpdateMapVisits();
        }

        /// <summary>방 안내 문구를 지도 오버레이 안내 줄에 띄운다(로컬, RoomLockSync.TargetShowNotice가 부름).</summary>
        internal static void RaiseMapNotice(string message)
        {
            OnMapTeleportDenied?.Invoke(message);
        }

        /// <summary>층 생성 시작(서버): 이전 층 지도는 생성 대기 동안 바로 비운다(클라이언트가 옛 층 지도를 보지 않게). BuildMapRooms도 다시 비운다.</summary>
        public void ServerClearMap()
        {
            UnsubscribeMapClears();
            mapRooms.Clear();
        }

        /// <summary>서버: 방(RoomClearController)의 mapRooms 인덱스. 없으면 -1(방 합류 순간이동 착지점용).</summary>
        public int IndexOfMapRoom(RoomClearController room)
        {
            return _mapClears.IndexOf(room);
        }

        /// <summary>
        /// Tab 지도 2차(서버): 방금 생성한 레벨의 방·복도 사각형을 mapRooms에 채운다. 1차 FloorMapOverlay.Rescan 기준 이식 —
        /// 방은 RoomEnterTrigger 콜라이더 bounds, 시작 방은 템플릿 이름 "Spawn room"(PositionPlayersAtSpawnRoom·RoomPostProcessing과 같은 기준),
        /// 보스 방은 RoomClearController.IsBossRoom(생성 직후라 보스가 목록에 있음 — 이후 보스가 파괴돼도 kind는 그대로), 복도는
        /// RoomInstance.IsCorridor 타일맵 범위. FindObjectsByType 대신 level.RoomInstances를 쓴다 — Edgar가 이전 층을 Object.Destroy로
        /// 지우면 프레임 끝까지 남아 있어 섞일 수 있기 때문. 입장 트리거가 없는 방은 타일맵 범위로 대신한다.
        /// </summary>
        [Server]
        public void BuildMapRooms(DungeonGeneratorLevelGrid2D level)
        {
            UnsubscribeMapClears();
            mapRooms.Clear();
            if (level == null)
            {
                return;
            }

            // autoSyncTransforms가 꺼져 있어, Edgar가 방을 옮긴 직후의 bounds는 템플릿 원본 좌표일 수 있다.
            Physics2D.SyncTransforms();
            // 3차: RoomInstance → mapRooms 인덱스(인접 복도 계산용, 이 함수 안에서만 쓴다).
            var indexOf = new System.Collections.Generic.Dictionary<RoomInstanceGrid2D, int>();
            foreach (var roomInstance in level.RoomInstances)
            {
                GameObject root = roomInstance.RoomTemplateInstance;
                if (root == null)
                {
                    continue;
                }

                Rect rect;
                if (roomInstance.IsCorridor)
                {
                    if (TryGetTilemapRect(root, out rect))
                    {
                        indexOf[roomInstance] = mapRooms.Count;
                        AddMapRoom(new MapRoom { bounds = rect, kind = MapKindCorridor }, null, null);
                    }

                    continue;
                }

                var trigger = root.GetComponentInChildren<RoomEnterTrigger>();
                var triggerCollider = trigger != null ? trigger.GetComponent<Collider2D>() : null;
                if (triggerCollider != null)
                {
                    Bounds b = triggerCollider.bounds;
                    rect = new Rect(b.min, b.size);
                }
                else if (!TryGetTilemapRect(root, out rect))
                {
                    continue;
                }

                bool isSpawn = roomInstance.RoomTemplatePrefab != null
                    ? roomInstance.RoomTemplatePrefab.name == "Spawn room"
                    : root.name.StartsWith("Spawn room");
                var clear = root.GetComponent<RoomClearController>();
                byte kind = isSpawn ? MapKindSpawn : clear != null && clear.IsBossRoom ? MapKindBoss : MapKindRoom;
                indexOf[roomInstance] = mapRooms.Count;
                AddMapRoom(new MapRoom
                {
                    bounds = rect,
                    kind = kind,
                    cleared = clear != null && clear.IsCleared,
                    visited = isSpawn, // 시작 방은 처음부터 방문 상태
                }, clear, triggerCollider);
            }

            // 3차: 인접 칸 목록(RoomPostProcessing과 같은 Doors → ConnectedRoomInstance). 목록에서 빠진 칸(타일맵 범위 없음)은 건너뛴다.
            foreach (var pair in indexOf)
            {
                foreach (var door in pair.Key.Doors)
                {
                    var other = door.ConnectedRoomInstance;
                    if (other != null && indexOf.TryGetValue(other, out int otherIndex) && !_mapNeighbors[pair.Value].Contains(otherIndex))
                    {
                        _mapNeighbors[pair.Value].Add(otherIndex);
                    }
                }
            }

            // 시작 방과 생성 시점에 이미 클리어된 방(몬스터 없는 방 등)은 나가는 복도를 바로 공개한다.
            for (int i = 0; i < mapRooms.Count; i++)
            {
                if (mapRooms[i].kind == MapKindSpawn || (mapRooms[i].kind != MapKindCorridor && mapRooms[i].cleared))
                {
                    RevealNeighborCorridors(i);
                }
            }

            Debug.Log($"[DungeonGameManager] floor {FloorProgression.Instance.Floor} map rooms: {mapRooms.Count}");
        }

        // 3차(서버): index 칸에 붙은 복도 중 아직 안 보이는 것만 방문 처리한다(안개는 복도까지만 걷힌다 — 복도 너머 방은 그대로).
        private void RevealNeighborCorridors(int index)
        {
            if (index < 0 || index >= _mapNeighbors.Count)
            {
                return;
            }

            foreach (int neighbor in _mapNeighbors[index])
            {
                MapRoom corridor = mapRooms[neighbor];
                if (corridor.kind != MapKindCorridor || corridor.visited)
                {
                    continue;
                }

                corridor.visited = true;
                mapRooms[neighbor] = corridor;
            }
        }

        private void AddMapRoom(MapRoom room, RoomClearController clear, Collider2D area)
        {
            int index = mapRooms.Count;
            mapRooms.Add(room);
            _mapNeighbors.Add(new System.Collections.Generic.List<int>());
            _mapAreas.Add(area);
            _mapClears.Add(clear);
            System.Action handler = null;
            if (clear != null)
            {
                handler = () => MarkMapRoomCleared(index);
                clear.Cleared += handler;
            }

            _mapClearHandlers.Add(handler);
        }

        private void UnsubscribeMapClears()
        {
            for (int i = 0; i < _mapClears.Count; i++)
            {
                // 이전 층 방은 이미 파괴됐을 수 있다(Unity null) — C# 이벤트 해제는 파괴와 무관하게 안전하다.
                if (!ReferenceEquals(_mapClears[i], null) && _mapClearHandlers[i] != null)
                {
                    _mapClears[i].Cleared -= _mapClearHandlers[i];
                }
            }

            _mapClears.Clear();
            _mapClearHandlers.Clear();
            // 3차 서버 전용 목록도 mapRooms 인덱스와 짝이라 같이 비운다(mapRooms.Clear 직전에 항상 이 함수가 불린다).
            _mapNeighbors.Clear();
            _mapAreas.Clear();
        }

        // struct라 항목을 꺼내 고친 뒤 같은 인덱스에 다시 넣어야 동기화된다.
        private void MarkMapRoomCleared(int index)
        {
            if (index < 0 || index >= mapRooms.Count || mapRooms[index].cleared)
            {
                return;
            }

            MapRoom room = mapRooms[index];
            room.cleared = true;
            mapRooms[index] = room;
            RevealNeighborCorridors(index); // 3차: 클리어한 방(보스 방 포함)의 나가는 복도 공개
        }

        // ── Tab 지도 3차: 클리어한 방 순간이동 ─────────────────────────────

        /// <summary>
        /// 지도에서 방을 클릭하면 보낸다(권한 없는 Command — 씬 오브젝트라서, 보낸 사람은 sender로 식별). 서버가 조건을 모두
        /// 확인한 뒤 요청자 소유 클라이언트에 위치를 쓰게 하고(클라이언트 권한 NetworkTransform), 거부면 사유 코드를 돌려준다.
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdRequestMapTeleport(int index, NetworkConnectionToClient sender = null)
        {
            if (sender == null)
            {
                return;
            }

            byte reason = CheckMapTeleport(index, sender, out Vector2 point);
            if (reason != 0)
            {
                TargetMapTeleportDenied(sender, reason);
                return;
            }

            _lastTeleportTime[sender.connectionId] = NetworkTime.time;
            // 소유 클라이언트가 위치·리지드바디를 직접 쓴다(서버에서만 바꾸면 클라이언트 권한 NetworkTransform이 곧바로 되돌린다).
            TargetApplyMapTeleport(sender, point);
            // 서버·다른 클라이언트의 보간 버퍼를 비워 새 위치로 곧장 보이게 한다(미끄러짐 방지). 같은 프레임에 보내 소유 클라이언트에도 같은 좌표로 도착한다.
            var networkTransform = sender.identity.GetComponent<NetworkTransformBase>();
            if (networkTransform != null)
            {
                Transform t = sender.identity.transform;
                networkTransform.ServerTeleport(new Vector3(point.x, point.y, t.position.z), t.rotation);
            }

            Debug.Log($"[DungeonGameManager] map teleport: conn {sender.connectionId} → room {index} at {point}");
        }

        // 0이면 허용(point에 착지점), 아니면 TeleportDeny* 사유 코드.
        [Server]
        private byte CheckMapTeleport(int index, NetworkConnectionToClient sender, out Vector2 point)
        {
            point = default;
            if (DungeonGameManager.Instance.IsGenerating || index < 0 || index >= mapRooms.Count)
            {
                return TeleportDenyNotCleared;
            }

            MapRoom room = mapRooms[index];
            if (room.kind == MapKindCorridor || !room.visited || !(room.cleared || room.kind == MapKindSpawn))
            {
                return TeleportDenyNotCleared; // 보스 방은 cleared(보스 처치)일 때만 통과
            }

            NetworkIdentity identity = sender.identity;
            if (identity == null)
            {
                return TeleportDenyDead;
            }

            var health = identity.GetComponent<Player.PlayerHealth>();
            if (health != null && health.IsDead)
            {
                return TeleportDenyDead;
            }

            Vector2 position = identity.transform.position;
            foreach (RoomClearController clear in _mapClears)
            {
                if (clear != null && clear.IsLocked && clear.ContainsPoint(position))
                {
                    return TeleportDenyCombat;
                }
            }

            if (teleportCooldown > 0f && _lastTeleportTime.TryGetValue(sender.connectionId, out double last)
                && NetworkTime.time - last < teleportCooldown)
            {
                return TeleportDenyCooldown;
            }

            var circle = identity.GetComponent<CircleCollider2D>();
            float radius = circle != null
                ? circle.radius * Mathf.Max(Mathf.Abs(identity.transform.lossyScale.x), Mathf.Abs(identity.transform.lossyScale.y))
                : DefaultPlayerRadius;
            return TryFindLandingPoint(index, radius, out point) ? (byte)0 : TeleportDenyNoLanding;
        }

        /// <summary>
        /// 착지점(서버): 방 사각형 중심부터 0.5유닛 격자를 가까운 고리 순으로 넓혀 가며, 입장 트리거 안이고 플레이어 몸(원)이
        /// 벽과 겹치지 않는 첫 지점을 고른다. 벽 판정은 디버그 메뉴 OverlapsSolid와 같은 규칙(트리거 아님, 리지드바디 없음).
        /// 방 합류 순간이동(RoomLockSync.PullOutsidePlayersInto)도 쓴다.
        /// </summary>
        [Server]
        public bool TryFindLandingPoint(int index, float radius, out Vector2 point)
        {
            Physics2D.SyncTransforms();
            Rect bounds = mapRooms[index].bounds;
            Collider2D area = index < _mapAreas.Count ? _mapAreas[index] : null;
            Vector2 center = bounds.center;
            int maxRings = Mathf.CeilToInt(Mathf.Max(bounds.width, bounds.height) * 0.5f / LandingStep);
            for (int ring = 0; ring <= maxRings; ring++)
            {
                // 같은 고리(체비셰프 거리 ring) 안에서는 중심에 가까운 순으로 본다.
                var candidates = new System.Collections.Generic.List<Vector2>();
                for (int x = -ring; x <= ring; x++)
                {
                    for (int y = -ring; y <= ring; y++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != ring)
                        {
                            continue;
                        }

                        Vector2 candidate = center + new Vector2(x, y) * LandingStep;
                        if (bounds.Contains(candidate))
                        {
                            candidates.Add(candidate);
                        }
                    }
                }

                if (candidates.Count == 0 && ring > 0)
                {
                    break; // 방 사각형을 모두 벗어났다
                }

                candidates.Sort((p, q) => (p - center).sqrMagnitude.CompareTo((q - center).sqrMagnitude));
                foreach (Vector2 candidate in candidates)
                {
                    if ((area == null || area.OverlapPoint(candidate)) && !OverlapsSolid(candidate, radius))
                    {
                        point = candidate;
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        private static bool OverlapsSolid(Vector2 point, float radius)
        {
            foreach (Collider2D col in Physics2D.OverlapCircleAll(point, radius))
            {
                if (!col.isTrigger && col.attachedRigidbody == null)
                {
                    return true; // 벽 등 정적 콜라이더(몬스터·플레이어처럼 리지드바디가 있는 것은 제외)
                }
            }

            return false;
        }

        /// <summary>요청한 플레이어의 소유 클라이언트(호스트 본인 포함)에서 실행된다 — 디버그 메뉴 Teleport와 같은 방식.
        /// 서버에서만 부른다: 지도 순간이동, 방 합류(RoomLockSync), 층 시작 방 배치(LevelGenerator).</summary>
        [TargetRpc]
        public void TargetApplyMapTeleport(NetworkConnectionToClient target, Vector2 point)
        {
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null)
            {
                return;
            }

            player.transform.position = new Vector3(point.x, point.y, player.transform.position.z);
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = point;
                body.linearVelocity = Vector2.zero;
            }
        }

        [TargetRpc]
        private void TargetMapTeleportDenied(NetworkConnectionToClient target, byte reason)
        {
            OnMapTeleportDenied?.Invoke(TeleportDenyMessage(reason));
        }

        // 사유 코드(byte)는 그대로 받고, 문구만 이 클라이언트 언어로(map.deny_*).
        private static string TeleportDenyMessage(byte reason)
        {
            switch (reason)
            {
                case TeleportDenyCombat: return Loc.Get("map.deny_combat");
                case TeleportDenyDead: return Loc.Get("map.deny_dead");
                case TeleportDenyNoLanding: return Loc.Get("map.deny_no_landing");
                case TeleportDenyCooldown: return Loc.Get("map.deny_cooldown");
                default: return Loc.Get("map.deny_not_cleared");
            }
        }

        // 0.2초마다(서버): 살아 있는 플레이어가 처음 들어간 방·복도를 방문 처리한다. 이미 true면 쓰지 않아 동기화가 생기지 않는다.
        private void UpdateMapVisits()
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection == null || connection.identity == null)
                {
                    continue;
                }

                var health = connection.identity.GetComponent<Player.PlayerHealth>();
                if (connection.identity.GetComponent<Player.PlayerController>() == null || (health != null && health.IsDead)
                    || RoomLockSync.Instance.IsAwaitingFloorArrival(connection.identity.gameObject))
                {
                    continue;
                }

                Vector2 position = connection.identity.transform.position;
                for (int i = 0; i < mapRooms.Count; i++)
                {
                    MapRoom room = mapRooms[i];
                    if (room.visited || !room.bounds.Contains(position))
                    {
                        continue;
                    }

                    room.visited = true;
                    mapRooms[i] = room;
                }
            }
        }

        // 타일맵은 Edgar가 공용 타일맵으로 복사한 뒤 꺼질 수 있어 Renderer.bounds 대신 Tilemap.localBounds를 월드로 옮긴다(1차 FloorMapOverlay와 같은 방식).
        private static bool TryGetTilemapRect(GameObject root, out Rect rect)
        {
            rect = default;
            bool found = false;
            foreach (var tilemap in root.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true))
            {
                tilemap.CompressBounds();
                if (tilemap.cellBounds.size.x <= 0 || tilemap.cellBounds.size.y <= 0)
                {
                    continue;
                }

                var local = tilemap.localBounds;
                Vector2 min = tilemap.transform.TransformPoint(local.min);
                Vector2 max = tilemap.transform.TransformPoint(local.max);
                var r = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
                rect = found
                    ? Rect.MinMaxRect(Mathf.Min(rect.xMin, r.xMin), Mathf.Min(rect.yMin, r.yMin), Mathf.Max(rect.xMax, r.xMax), Mathf.Max(rect.yMax, r.yMax))
                    : r;
                found = true;
            }

            return found;
        }
    }
}
