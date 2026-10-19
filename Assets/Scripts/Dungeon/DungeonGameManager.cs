using System.Collections;
using Edgar.Unity;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 던전 진입점(리팩터 C2, 2026-10-12 분해 뒤): 층 시작 순서만 조율하고 몬스터 난이도 배율 정적 접근을 둔다.
    /// 같은 씬 오브젝트("Dungeon Game Manager")에 붙은 컴포넌트가 나머지를 맡는다(SyncVar는 각자 소유):
    /// - <see cref="FloorProgression"/>: 층 번호·AdvanceFloor·런 승리, 층 속성·보스 상황 특성·동물 풀, 보스 프리팹
    /// - <see cref="LevelGenerator"/>: Edgar 생성, 시드 동기화(dungeonSeed·generationId), 원격 클라이언트 재생성, 타일 테마, 시작 방 배치
    /// - <see cref="RoomLockSync"/>: 방 잠금 동기화, 보스전 여부, 방 밖 플레이어 끌어오기, 층 이동 도착 대기(QA #39)
    /// - <see cref="FloorMapService"/>: Tab 지도 데이터, 지도 순간이동 요청 검증
    /// - <see cref="DifficultyConfig"/>(ScriptableObject 에셋): 몬스터 이동속도·공격 쿨·예고 시간 배율
    /// 컴포넌트 순서(NetworkIdentity 뒤): FloorProgression → LevelGenerator → RoomLockSync → FloorMapService → DungeonGameManager.
    /// Mirror는 컴포넌트 순서대로 역직렬화하므로, 훅에서 다른 컴포넌트 값을 읽는 쪽(LevelGenerator 재생성이 층 번호·속성을 읽음)을
    /// 뒤에 둔다. 이 클래스는 SyncVar가 없어 마지막이다.
    /// </summary>
    public class DungeonGameManager : NetworkBehaviour
    {
        [Tooltip("몬스터 전체 난이도 배율 에셋(Assets/Data/Dungeon/DifficultyConfig.asset). 없으면 배율 1.")]
        [SerializeField] private DifficultyConfig difficultyConfig;

        /// <summary>몬스터 난이도 배율(서버가 읽음). 매니저나 에셋이 없으면 1.</summary>
        public static float MonsterMoveSpeedScale => Instance != null && Instance.difficultyConfig != null ? Instance.difficultyConfig.MonsterMoveSpeedScale : 1f;
        public static float MonsterAttackCooldownScale => Instance != null && Instance.difficultyConfig != null ? Instance.difficultyConfig.MonsterAttackCooldownScale : 1f;
        public static float MonsterWindupScale => Instance != null && Instance.difficultyConfig != null ? Instance.difficultyConfig.MonsterWindupScale : 1f;

        /// <summary>난이도 에셋(층·인원 배율은 DifficultyScaling이 이 참조로 읽는다). 없으면 null.</summary>
        public DifficultyConfig DifficultyConfig => difficultyConfig;

        public static DungeonGameManager Instance { get; private set; }

        private bool _generating;

        /// <summary>층 생성 코루틴이 도는 중(서버). 이 동안 층 이동·지도 순간이동·방문 갱신을 막는다.</summary>
        public bool IsGenerating => _generating;

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

        public override void OnStartServer()
        {
            // "다시 시작"(ServerChangeScene)은 씬을 새로 불러오므로 새 매니저 인스턴스가 여기서 다시 섞는다 — 런마다 새 순서.
            FloorProgression.Instance.ShuffleFloorElements();
            StartCoroutine(GenerateCoroutine());
        }

        /// <summary>다음 층 시작(서버). FloorProgression.AdvanceFloor가 floor++·정리 뒤에 부른다. 코루틴 주체는 이 매니저.</summary>
        public void StartFloor()
        {
            StartCoroutine(GenerateCoroutine());
        }

        private IEnumerator GenerateCoroutine()
        {
            LevelGenerator generator = LevelGenerator.Instance;
            RoomLockSync locks = RoomLockSync.Instance;
            FloorMapService map = FloorMapService.Instance;

            // ① 생성 중 표시
            _generating = true;

            // ② 이전 층 지도는 생성 대기 동안 바로 비운다(클라이언트가 옛 층 지도를 보지 않게). BuildMapRooms도 다시 비운다.
            map.ServerClearMap();
            // ③ 2인 검증 1차 후속: 이전 층 방 인덱스·잠금 목록도 비운다(보스전 false 포함).
            locks.ServerResetForNewFloor();
            // ④ 이전 층 시작 위치 무효
            generator.ServerBeginGeneration();

            // ⑤ Edgar 예제(Example1GameManager)와 동일하게, 생성 전 한 프레임 양보한다. 다음 층일 때는 이 사이에
            // 이전 층 네트워크 오브젝트 파괴도 끝난다.
            yield return null;

            // ⑥ 층 속성 → 상황 특성 → 동물 풀(반드시 Generate() 전 — 보스 스폰이 읽는다)
            FloorProgression.Instance.ServerPrepareFloor();

            // ⑦ 시드를 정하고 생성 + 레이아웃 로그
            DungeonGeneratorLevelGrid2D level = generator.ServerGenerate();
            // ⑧ 층 속성 타일 테마
            generator.ApplyTileTheme(level);
            // ⑨ 층 생성 알림(층 전환 연출)
            generator.RaiseLevelGenerated();
            // ⑩ 방 인덱스(잠김 이벤트 구독)
            locks.BuildRoomIndex(level);
            // ⑪ 플레이어를 시작 방으로(도착 대기 등록 포함)
            generator.PositionPlayersAtSpawnRoom(level);
            // ⑫ Tab 지도 만들기
            map.BuildMapRooms(level);
            // ⑬ 시드·시작 위치가 모두 정해진 뒤에 올린다 — 클라이언트 훅(OnGenerationIdChanged)이 새 시드·새 시작 위치를 읽는다.
            generator.ServerFinishGeneration();
            // ⑭ 생성 끝
            _generating = false;
        }
    }
}
