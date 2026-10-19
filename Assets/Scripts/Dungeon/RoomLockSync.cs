using AbsorbRoguelike.Localization;
using Edgar.Unity;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 방 잠금 동기화(리팩터 C2, 예전 DungeonGameManager에서 분리): 잠긴 방 인덱스(lockedRoomIndices, level.RoomInstances 순서 — 같은 시드면
    /// 서버·클라 같은 순서) SyncList와 보스전 여부, 문이 잠길 때 방 밖 플레이어 끌어오기(결정 ㉚A), 층 이동 뒤 도착 대기(QA #28·#39).
    /// 컴포넌트 순서 3번째: 로컬 레벨이 생긴 뒤(LevelGenerator 재생성) 잠금을 적용하므로 LevelGenerator보다 뒤. SyncList 콜백
    /// (ApplyAllLockVisuals)은 _roomClears가 비어 있어도 안전하다(늦은 접속 시 로컬 생성 직후 다시 적용).
    /// </summary>
    public class RoomLockSync : NetworkBehaviour
    {
        private const float DefaultPlayerRadius = 0.4f;

        // 잠긴 방 인덱스(level.RoomInstances 순서 — 같은 시드면 서버·클라 같은 순서). 서버가 RoomClearController.LockedChanged로 갱신.
        private readonly SyncList<int> lockedRoomIndices = new SyncList<int>();
        // 보스 방 문이 잠겨 보스전 중인가(2026-10-08, 배경음악 전환용 — MusicDirector). 서버가 잠김 변화 때 다시 계산한다.
        [SyncVar] private bool bossFightActive;

        /// <summary>보스 방이 잠겨 보스전이 진행 중(모든 화면 동기화). 보스를 쓰러뜨려 문이 열리면 false.</summary>
        public bool BossFightActive => bossFightActive;
        // 방 인덱스(level.RoomInstances 순서) → RoomClearController(없으면 null). 서버는 서버 레벨, 원격 클라이언트는 로컬 레벨 기준. 층마다 다시 만든다.
        private readonly System.Collections.Generic.List<RoomClearController> _roomClears = new System.Collections.Generic.List<RoomClearController>();
        private readonly System.Collections.Generic.List<System.Action<bool>> _roomLockHandlers = new System.Collections.Generic.List<System.Action<bool>>();

        public static RoomLockSync Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            lockedRoomIndices.Callback += (op, index, oldItem, newItem) => ApplyAllLockVisuals();
        }

        private void OnDestroy()
        {
            UnsubscribeRoomLocks();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>층 생성 시작(서버, 2인 검증 1차 후속): 이전 층 방 인덱스·잠금 목록을 비운다(원격 클라이언트는 새 층 로컬 생성 후 다시 적용).</summary>
        public void ServerResetForNewFloor()
        {
            UnsubscribeRoomLocks();
            lockedRoomIndices.Clear();
            bossFightActive = false;
        }

        /// <summary>방 인덱스 목록(서버·클라 공통): level.RoomInstances 순서로 RoomClearController를 모은다. 서버는 잠김 이벤트도 구독한다.</summary>
        public void BuildRoomIndex(DungeonGeneratorLevelGrid2D level)
        {
            UnsubscribeRoomLocks();
            if (level == null)
            {
                return;
            }

            foreach (var roomInstance in level.RoomInstances)
            {
                GameObject root = roomInstance.RoomTemplateInstance;
                RoomClearController clear = root != null ? root.GetComponent<RoomClearController>() : null;
                int index = _roomClears.Count;
                _roomClears.Add(clear);
                System.Action<bool> handler = null;
                if (clear != null && NetworkServer.active)
                {
                    handler = locked => OnRoomLockChangedServer(index, locked);
                    clear.LockedChanged += handler;
                }

                _roomLockHandlers.Add(handler);
            }
        }

        private void UnsubscribeRoomLocks()
        {
            for (int i = 0; i < _roomClears.Count; i++)
            {
                // 이전 층 방은 이미 파괴됐을 수 있다(Unity null) — C# 이벤트 해제는 파괴와 무관하게 안전하다.
                if (!ReferenceEquals(_roomClears[i], null) && _roomLockHandlers[i] != null)
                {
                    _roomClears[i].LockedChanged -= _roomLockHandlers[i];
                }
            }

            _roomClears.Clear();
            _roomLockHandlers.Clear();
        }

        // 서버: 방 잠김/해제 → SyncList 갱신(원격 클라이언트가 콜백에서 로컬 문에 반영).
        private void OnRoomLockChangedServer(int index, bool locked)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (locked)
            {
                if (!lockedRoomIndices.Contains(index))
                {
                    lockedRoomIndices.Add(index);
                }

                PullOutsidePlayersInto(index < _roomClears.Count ? _roomClears[index] : null);
            }
            else
            {
                lockedRoomIndices.Remove(index);
            }

            bool boss = false;
            foreach (int lockedIndex in lockedRoomIndices)
            {
                if (lockedIndex < _roomClears.Count && _roomClears[lockedIndex] != null && _roomClears[lockedIndex].IsBossRoom)
                {
                    boss = true;
                    break;
                }
            }

            bossFightActive = boss;
        }

        private const float PullSkipDistance = 2f; // [PLAYTEST] 방 경계에서 이 거리(칸) 안이면 합류 순간이동 제외

        // QA #28(2026-10-07): 층 이동 직후 원격 플레이어의 서버 사본은 아직 이전 층 좌표다(위치 권한이 클라이언트라, 클라이언트가
        // 새 층을 로컬 생성하고 시작 위치로 옮긴 값을 보내기 전까지). 그 옛 좌표가 새 층의 전투 방 안에 걸리면 서버가 "입장"으로 보고
        // 방을 잠그고 시작 방의 호스트를 그 방으로 끌어당겼다. 새 시작 위치 근처에서 한 번 확인될 때까지 방 입장·합류·지도 방문 판정에서 뺀다.
        private const float ArrivalRadius = 3f;      // 시작 위치에서 이 거리(칸) 안이면 도착으로 본다
        private const double ArrivalTimeout = 10.0;  // 그래도 안 오면 이 시간(초) 뒤 대기 해제
        // 값: (대기 시작 시각, 클라이언트 도착 신호를 받았는가). QA #39 — 서버가 사본을 시작 위치로 직접 옮기므로(ServerTeleport)
        // '시작 위치 근처'만으로는 도착을 알 수 없다. 클라이언트는 새 층을 만들기 전까지 이전 층 좌표를 계속 보내 사본이 되돌아간다.
        private readonly System.Collections.Generic.Dictionary<NetworkIdentity, (double since, bool confirmed)> _awaitingArrival =
            new System.Collections.Generic.Dictionary<NetworkIdentity, (double since, bool confirmed)>();

        /// <summary>서버: 새 시작 위치가 정해지면 이전 층 도착 대기를 모두 지운다(LevelGenerator.PositionPlayersAtSpawnRoom).</summary>
        public void ClearAwaitingArrival()
        {
            _awaitingArrival.Clear();
        }

        /// <summary>서버: 원격 플레이어를 새 시작 위치로 보낸 직후 도착 대기에 넣는다(LevelGenerator.PositionPlayersAtSpawnRoom).</summary>
        public void BeginAwaitingArrival(NetworkIdentity identity)
        {
            _awaitingArrival[identity] = (NetworkTime.time, false);
        }

        /// <summary>층 이동 뒤 아직 새 시작 위치에 도착이 확인되지 않은 원격 플레이어인가(서버). 이 동안은 방 입장 판정을 하지 않는다.
        /// 도착 = 클라이언트가 새 층을 만들고 시작 위치로 옮긴 뒤 보낸 CmdConfirmFloorArrival을 받았고, 서버 사본도 시작 위치 근처로 온 것.
        /// 신호가 끝내 안 오면 ArrivalTimeout 뒤 대기를 푼다.</summary>
        public bool IsAwaitingFloorArrival(GameObject player)
        {
            if (_awaitingArrival.Count == 0 || player == null)
            {
                return false;
            }

            var identity = player.GetComponent<NetworkIdentity>();
            if (identity == null || !_awaitingArrival.TryGetValue(identity, out var state))
            {
                return false;
            }

            if (NetworkTime.time - state.since > ArrivalTimeout
                || (state.confirmed && ((Vector2)identity.transform.position - LevelGenerator.Instance.SpawnPoint).sqrMagnitude <= ArrivalRadius * ArrivalRadius))
            {
                _awaitingArrival.Remove(identity);
                return false;
            }

            return true;
        }

        /// <summary>원격 클라이언트 → 서버: 이 층(generationId)의 레벨을 만들고 시작 위치로 옮겼다(QA #39). 다른 층 신호는 무시.
        /// LevelGenerator.ApplyClientSpawnPosition이 보낸다.</summary>
        [Command(requiresAuthority = false)]
        public void CmdConfirmFloorArrival(int id, NetworkConnectionToClient sender = null)
        {
            if (id != LevelGenerator.Instance.GenerationId || sender == null || sender.identity == null)
            {
                return;
            }

            if (_awaitingArrival.TryGetValue(sender.identity, out var state))
            {
                _awaitingArrival[sender.identity] = (state.since, true);
            }
        }

        /// <summary>
        /// 결정 ㉚A(2026-09-30): 전투 방 문이 잠기는 순간, 방 밖에 있는 살아 있는 플레이어를 그 방 안으로 순간이동시킨다 — 한 명이
        /// 먼저 들어가면 나머지가 문 밖에 갇히던 문제. 착지점·위치 적용은 지도 순간이동과 같은 경로(FloorMapService.TryFindLandingPoint →
        /// TargetApplyMapTeleport + NetworkTransform.ServerTeleport). 쓰러진 플레이어는 옮기지 않는다.
        /// </summary>
        [Server]
        private void PullOutsidePlayersInto(RoomClearController room)
        {
            if (room == null)
            {
                return;
            }

            FloorMapService map = FloorMapService.Instance;
            int mapIndex = map.IndexOfMapRoom(room);
            if (mapIndex < 0)
            {
                return;
            }

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                NetworkIdentity identity = connection?.identity;
                if (identity == null)
                {
                    continue;
                }

                var health = identity.GetComponent<Player.PlayerHealth>();
                if ((health != null && health.IsDead) || room.ContainsPoint(identity.transform.position)
                    || IsAwaitingFloorArrival(identity.gameObject))
                {
                    continue;
                }

                // 결정 ㉛A: 방 경계에서 PullSkipDistance 안이면 방금 들어오던 사람으로 본다 — 참가자 위치는 클라이언트 권한이라
                // 서버 사본이 한 박자 늦어, 문을 연 본인까지 방 가운데로 튀던 문제(2026-09-30 2인 검증 로그).
                Collider2D area = room.RoomArea;
                if (area != null && area.bounds.SqrDistance(identity.transform.position) <= PullSkipDistance * PullSkipDistance)
                {
                    continue;
                }

                var circle = identity.GetComponent<CircleCollider2D>();
                float radius = circle != null
                    ? circle.radius * Mathf.Max(Mathf.Abs(identity.transform.lossyScale.x), Mathf.Abs(identity.transform.lossyScale.y))
                    : DefaultPlayerRadius;
                if (!map.TryFindLandingPoint(mapIndex, radius, out Vector2 point))
                {
                    continue;
                }

                map.TargetApplyMapTeleport(connection, point);
                var networkTransform = identity.GetComponent<NetworkTransformBase>();
                if (networkTransform != null)
                {
                    Transform t = identity.transform;
                    networkTransform.ServerTeleport(new Vector3(point.x, point.y, t.position.z), t.rotation);
                }

                TargetShowNotice(connection, Loc.Pack("notice.pulled_into_combat"));
                Debug.Log($"[DungeonGameManager] pulled conn {connection.connectionId} into locked room {room.name} at {point}");
            }
        }

        // 짧은 안내 문구(지도 오버레이의 안내 줄을 재사용 — 순간이동 거부 안내와 같은 표시). message는 Loc.Pack 문자열 — 받는 쪽 언어로 푼다.
        [TargetRpc]
        private void TargetShowNotice(NetworkConnectionToClient target, string message)
        {
            FloorMapService.RaiseMapNotice(Loc.Unpack(message));
        }

        /// <summary>원격 클라이언트: 현재 잠긴 방 목록을 로컬 레벨 문에 일괄 적용한다(SyncList 콜백·로컬 생성 직후·늦은 접속 공통). 호스트는 no-op
        /// (호스트 문은 서버 RoomClearController가 직접 여닫는다).</summary>
        public void ApplyAllLockVisuals()
        {
            if (NetworkServer.active)
            {
                return;
            }

            for (int i = 0; i < _roomClears.Count; i++)
            {
                if (_roomClears[i] != null)
                {
                    _roomClears[i].ApplyLockedVisual(lockedRoomIndices.Contains(i));
                }
            }
        }
    }
}
