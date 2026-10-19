using System.Collections;
using AbsorbRoguelike.Abilities;
using Edgar.Unity;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 레벨 생성(리팩터 C2, 예전 DungeonGameManager에서 분리): Edgar for Unity(ADR-004)로 층 맵을 만든다. 씬의 "Dungeon Generator"
    /// (DungeonGeneratorGrid2D)를 호출하고, 층 속성 타일 테마를 입히고, 플레이어를 시작 방으로 옮긴다.
    /// 맵 레이아웃은 시드로 맞춘다(2026-09-30) — 서버가 층마다 시드를 정해 dungeonSeed·generationId SyncVar로 보내고, 원격 클라이언트는
    /// 같은 시드로 Generate()를 로컬에서 한 번 더 돌린다(<see cref="ClientGenerateCoroutine"/>, 호스트는 안 함). 클라이언트 후처리는
    /// RoomPostProcessing.RunClient(시각·충돌만). 서버 쪽 생성 순서는 <see cref="DungeonGameManager"/> 층 시작 코루틴이 조율한다.
    /// 컴포넌트 순서: FloorProgression 다음 — 재생성이 층 번호·속성·상황 특성을 읽는다. dungeonSeed·spawnPoint·hasSpawnPoint는
    /// generationId 위에 선언한다(선언 순서대로 역직렬화 — 훅 시점에 새 값).
    /// WebGL: Edgar가 자체적으로 동기 생성(Task.Run 없이 메인 스레드, 시간 초과는 EarlyStopIfTimeExceeded) — 층 생성 동안 프레임이 멈출 수 있음.
    /// </summary>
    public class LevelGenerator : NetworkBehaviour
    {
        [SerializeField] private DungeonGeneratorGrid2D generator;
        [Tooltip("층 속성별 타일 테마(2026-10-06). 생성 직후 그 층 속성의 테마로 타일을 바꿔 끼운다. 없으면 예제 타일 그대로.")]
        [SerializeField] private ElementTileTheme[] tileThemes;

        // 2인 검증 1차 후속(2026-09-30): 층 레이아웃 시드. generationId는 층 생성마다 1씩 올라(같은 시드가 우연히 반복돼도 재생성되게)
        // 원격 클라이언트 재생성 트리거로 쓴다. 선언 순서대로 역직렬화되므로 시드·시작 위치를 generationId보다 위에 둔다(훅 시점에 새 값).
        [SyncVar] private int dungeonSeed;
        // 이 층 시작 위치(서버 PositionPlayersAtSpawnRoom이 정함). 원격 클라이언트가 층 이동 뒤 로컬 생성이 끝나면 로컬 플레이어에 적용한다.
        [SyncVar] private Vector2 spawnPoint;
        [SyncVar] private bool hasSpawnPoint;
        [SyncVar(hook = nameof(OnGenerationIdChanged))] private int generationId;
        // 원격 클라이언트 전용: 마지막으로 로컬 생성을 시작한 generationId(중복 생성 방지)와 생성 중 여부.
        private int _clientGeneratedId;
        private bool _clientGenerating;

        public static LevelGenerator Instance { get; private set; }
        /// <summary>층 생성 횟수(층마다 1씩 오름). 도착 신호(RoomLockSync.CmdConfirmFloorArrival)가 같은 층인지 비교한다.</summary>
        public int GenerationId => generationId;
        /// <summary>이 층 시작 위치(서버가 정함). 도착 판정(RoomLockSync.IsAwaitingFloorArrival)이 읽는다.</summary>
        public Vector2 SpawnPoint => spawnPoint;

        /// <summary>층 생성 직후(호스트·원격 클라이언트 각자, 타일 테마 적용 뒤). 인자: 층 번호, 층 속성. 층 전환 연출(FloorTransitionOverlay)이 구독한다.</summary>
        public static event System.Action<int, ElementType> LevelGenerated;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void OnStartClient()
        {
            // 원격 클라이언트(늦은 접속 포함): 이미 정해진 층 시드가 있으면 바로 로컬 생성. 호스트는 RequestClientGenerate 안에서 걸러진다.
            RequestClientGenerate();
        }

        public override void OnStopClient()
        {
            // 재접속(새 호스트) 때 generationId가 우연히 같아도 다시 생성하도록 초기화한다.
            _clientGeneratedId = 0;
        }

        private void OnGenerationIdChanged(int oldValue, int newValue)
        {
            RequestClientGenerate();
        }

        // 원격 클라이언트 전용 진입점(OnStartClient·generationId 훅 공통). 호스트(서버)는 절대 다시 생성하지 않는다 — 서버 레벨이 곧 호스트 화면.
        private void RequestClientGenerate()
        {
            if (NetworkServer.active || generationId == 0 || generationId == _clientGeneratedId || !isActiveAndEnabled)
            {
                return;
            }

            bool floorChange = _clientGeneratedId != 0;
            _clientGeneratedId = generationId;
            StartCoroutine(ClientGenerateCoroutine(generationId, floorChange));
        }

        /// <summary>
        /// 원격 클라이언트 전용 레벨 생성(2인 검증 1차 후속): 서버와 같은 시드로 Generate()를 한 번 돌린다. Edgar(DungeonGeneratorTaskGrid2D)는
        /// "Generated Level" 루트의 이전 자식(이전 층)을 떼어 Destroy한 뒤 새로 만든다 — 서버와 같은 정리 경로. 서버 전용 처리
        /// (PositionPlayersAtSpawnRoom·BuildMapRooms·Roll*)는 부르지 않고, 후처리도 RoomPostProcessing.RunClient(시각·충돌만)로 갈라진다.
        /// 끝나면 방 인덱스 목록을 만들고 현재 잠금 상태를 한 번 적용한다(RoomLockSync).
        /// </summary>
        private IEnumerator ClientGenerateCoroutine(int id, bool floorChange)
        {
            while (_clientGenerating)
            {
                yield return null;
            }

            // 한 프레임 양보(서버 층 시작 코루틴과 같은 흐름). 그 사이 더 새로운 generationId가 오면 이 요청은 버린다(새 요청이 따로 돈다).
            yield return null;
            if (id != generationId)
            {
                yield break;
            }

            _clientGenerating = true;
            try
            {
                int seed = dungeonSeed;
                generator.UseRandomSeed = false;
                generator.RandomGeneratorSeed = seed;
                var payload = (DungeonGeneratorPayloadGrid2D)generator.Generate();
                DungeonGeneratorLevelGrid2D level = payload != null ? payload.GeneratedLevel : null;
                LogLevelLayout("client", seed, id, level);
                ApplyTileTheme(level);
                RaiseLevelGenerated();
                RoomLockSync.Instance.BuildRoomIndex(level);
                RoomLockSync.Instance.ApplyAllLockVisuals();
                if (floorChange)
                {
                    ApplyClientSpawnPosition();
                }
            }
            finally
            {
                _clientGenerating = false;
            }
        }

        // 원격 클라이언트: 층 이동 뒤 로컬 레벨이 준비되면 서버가 정한 시작 위치(spawnPoint SyncVar)로 로컬 플레이어를 다시 놓는다.
        // 서버의 TargetApplyMapTeleport가 로컬 생성보다 먼저 도착해도, 새 벽이 생긴 뒤 한 번 더 맞춰 벽 끼임·옛 위치 복귀를 막는다.
        // (위치 권한은 소유 클라이언트 — ClientToServer NetworkTransform — 이므로 여기서 쓴 값이 서버로 올라간다.)
        private void ApplyClientSpawnPosition()
        {
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null || !hasSpawnPoint)
            {
                return;
            }

            player.transform.position = new Vector3(spawnPoint.x, spawnPoint.y, player.transform.position.z);
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = spawnPoint;
                body.linearVelocity = Vector2.zero;
            }

            RoomLockSync.Instance.CmdConfirmFloorArrival(generationId); // 이제부터 이 클라이언트가 보내는 위치는 새 층 좌표다(QA #39)
        }

        // ── 서버 생성 단계(DungeonGameManager 층 시작 코루틴이 순서대로 부른다) ─────────────────────────────

        /// <summary>층 생성 시작(서버): 이전 층 시작 위치를 무효로 한다.</summary>
        public void ServerBeginGeneration()
        {
            hasSpawnPoint = false;
        }

        /// <summary>
        /// 2인 검증 1차 후속(서버): 층 시드를 정해 서버도 그 시드로 한 번만 생성하고(호스트 중복 생성 없음), 원격 클라이언트에는 SyncVar로 보낸다.
        /// 레이아웃 로그까지 찍고 생성된 레벨을 돌려준다.
        /// </summary>
        public DungeonGeneratorLevelGrid2D ServerGenerate()
        {
            int seed = Random.Range(int.MinValue, int.MaxValue);
            generator.UseRandomSeed = false;
            generator.RandomGeneratorSeed = seed;
            dungeonSeed = seed;
            var payload = (DungeonGeneratorPayloadGrid2D)generator.Generate();
            LogLevelLayout("server", seed, generationId + 1, payload.GeneratedLevel);
            return payload.GeneratedLevel;
        }

        /// <summary>층 생성 마지막(서버): 시드·시작 위치가 모두 정해진 뒤에 올린다 — 클라이언트 훅(OnGenerationIdChanged)이 새 시드·새 시작 위치를 읽는다.</summary>
        public void ServerFinishGeneration()
        {
            generationId++;
        }

        /// <summary>층 생성 직후 알림(서버·클라 공통). 층 번호·속성은 FloorProgression에서 읽는다.</summary>
        public void RaiseLevelGenerated()
        {
            FloorProgression progression = FloorProgression.Instance;
            LevelGenerated?.Invoke(progression.Floor, progression.CurrentFloorElement);
        }

        // 결정성 비교용 로그(서버·클라이언트 공통): 템플릿 이름 + 배치 위치를 RoomInstances 순서대로 한 줄에 찍는다.
        private static void LogLevelLayout(string who, int seed, int id, DungeonGeneratorLevelGrid2D level)
        {
            if (level == null)
            {
                Debug.LogWarning($"[DungeonGameManager] {who} generation {id} (seed {seed}): no level");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var room in level.RoomInstances)
            {
                string templateName = room.RoomTemplatePrefab != null ? room.RoomTemplatePrefab.name : "?";
                sb.Append(templateName).Append('@').Append(room.Position).Append("; ");
            }

            Debug.Log($"[DungeonGameManager] {who} layout gen {id} seed {seed} rooms {level.RoomInstances.Count}: {sb}");
        }

        /// <summary>층 속성 타일 테마(서버·클라 공통): 같은 시드·같은 속성이라 양쪽 화면 타일이 같다.</summary>
        public void ApplyTileTheme(DungeonGeneratorLevelGrid2D level)
        {
            if (level == null || tileThemes == null)
            {
                return;
            }

            ElementType element = FloorProgression.Instance.CurrentFloorElement;
            foreach (ElementTileTheme theme in tileThemes)
            {
                if (theme != null && theme.element == element)
                {
                    theme.Apply(level.RootGameObject);
                    return;
                }
            }
        }

        /// <summary>
        /// 2026-09-22 플레이테스트에서 발견 — Mirror는 씬에 NetworkStartPosition이 하나도 없으면 플레이어를
        /// 월드 원점(0,0)에 스폰한다. 던전은 Edgar가 매번 다른 위치에 절차적으로 배치하므로 원점이 "Spawn
        /// room" 안이라는 보장이 전혀 없다(대부분 방 밖 벽 속이나 허공에서 시작하게 됨). 던전 생성이 끝난
        /// 뒤, 이미 접속된 플레이어들을 "Spawn room"의 실제 생성 위치로 옮겨준다. RoomEnterTrigger의
        /// Collider2D(방 전체를 덮도록 이미 배치됨)의 bounds 중심을 안전한 실내 좌표로 사용한다.
        /// 2026-09-23: 플레이어가 던전 생성보다 늦게 스폰되면 옮겨지지 않고 원점(벽 속)에 남던 문제 —
        /// 같은 위치에 NetworkStartPosition을 만들어 Mirror가 이후 스폰하는 플레이어도 여기서 시작하게 한다.
        /// </summary>
        public void PositionPlayersAtSpawnRoom(DungeonGeneratorLevelGrid2D level)
        {
            RoomInstanceGrid2D spawnRoom = null;
            foreach (var roomInstance in level.RoomInstances)
            {
                if (roomInstance.RoomTemplatePrefab != null && roomInstance.RoomTemplatePrefab.name == "Spawn room")
                {
                    spawnRoom = roomInstance;
                    break;
                }
            }

            if (spawnRoom == null)
            {
                Debug.LogWarning("[DungeonGameManager] \"Spawn room\" 템플릿을 찾지 못해 플레이어 위치를 옮기지 못했다 — 기본 스폰 위치(보통 월드 원점)에 그대로 남는다.");
                return;
            }

            // autoSyncTransforms가 꺼져 있어, Edgar가 방을 옮긴 직후의 Collider2D.bounds는 템플릿 원본 좌표를 돌려준다.
            Physics2D.SyncTransforms();
            var trigger = spawnRoom.RoomTemplateInstance.GetComponentInChildren<RoomEnterTrigger>();
            var triggerCollider = trigger != null ? trigger.GetComponent<Collider2D>() : null;
            Vector3 spawnPosition = triggerCollider != null
                ? (Vector3)triggerCollider.bounds.center
                : spawnRoom.RoomTemplateInstance.transform.position;

            spawnPoint = spawnPosition;
            hasSpawnPoint = true;
            RoomLockSync.Instance.ClearAwaitingArrival();

            var startPoint = new GameObject("Spawn Point");
            startPoint.transform.SetParent(spawnRoom.RoomTemplateInstance.transform, false);
            startPoint.transform.position = spawnPosition;
            startPoint.AddComponent<NetworkStartPosition>();

            foreach (var connection in NetworkServer.connections.Values)
            {
                if (connection.identity != null)
                {
                    connection.identity.transform.position = spawnPosition;
                    var body = connection.identity.GetComponent<Rigidbody2D>();
                    if (body != null) body.position = spawnPosition;

                    // 원격 플레이어는 클라이언트 권한 NetworkTransform이라 서버에서만 옮기면 곧바로 되돌아간다 — 지도 순간이동과 같이
                    // 소유 클라이언트가 직접 위치를 쓰게 한다(2인 검증 준비, 층 이동 시 둘 다 새 시작 방으로). 호스트 본인은 위에서 이미 옮김.
                    if (!(connection is LocalConnectionToClient))
                    {
                        FloorMapService.Instance.TargetApplyMapTeleport(connection, spawnPosition);
                        // 서버 사본도 바로 시작 위치로(합류 순간이동과 같은 경로) + 도착 확인 전까지 방 입장 판정 제외(QA #28)
                        var networkTransform = connection.identity.GetComponent<NetworkTransformBase>();
                        if (networkTransform != null)
                        {
                            networkTransform.ServerTeleport(spawnPosition, connection.identity.transform.rotation);
                        }

                        RoomLockSync.Instance.BeginAwaitingArrival(connection.identity);
                    }
                }
            }
        }
    }
}
