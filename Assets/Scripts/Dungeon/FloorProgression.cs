using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Localization;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 층 진행(리팩터 C2, 예전 DungeonGameManager에서 분리): 층 번호·마지막 층·런 승리, 층 속성(런마다 섞은 5속성 순서),
    /// 보스 상황 특성, 층별 동물 풀, 보스 프리팹 원본. 다음 층 이동(<see cref="AdvanceFloor"/>)은 정리 뒤
    /// <see cref="DungeonGameManager.StartFloor"/>로 층 시작 순서를 맡긴다.
    /// 컴포넌트 순서: "Dungeon Game Manager" 오브젝트의 NetworkBehaviour 중 첫 번째 — LevelGenerator 재생성 코루틴·배너가
    /// floor·층 속성·상황 특성을 읽으므로 먼저 역직렬화돼야 한다. 같은 이유로 floor는 currentBossSituation보다 위에 선언한다.
    /// </summary>
    public class FloorProgression : NetworkBehaviour
    {
        /// <summary>동물 1종 = 잡몹 프리팹 + 정예몹 프리팹. 방 템플릿에 배치된 몬스터 자리를 이걸로 바꿔 넣는다.</summary>
        [System.Serializable]
        public class AnimalEntry
        {
            public string displayName;
            public GameObject normalPrefab;
            public GameObject elitePrefab;
        }

        [Tooltip("마지막 층의 보스를 잡으면 런 승리. GDD 5챕터(속성 5종)에 맞춤.")]
        [SerializeField] private int maxFloors = 5;

        [Header("스테이지마다 무작위 동물 풀 (GDD v2.14)")]
        [SerializeField] private AnimalEntry[] animals;
        [Tooltip("층마다 전체 동물 중 이만큼만 등장한다. [PLAYTEST] 2026-09-28 동물 7종 기준 임시 3(유저 결정 대기, 기획_누락_추적 #17).")]
        [SerializeField] private int animalsPerFloor = 3;
        [Tooltip("상황 특성의 유리한 동물(갑옷=소, 연타=돼지)이 그 층 풀에 들어갈 확률. 성공하면 반드시 넣고, 실패하면 반드시 뺀다 — " +
                 "정확히 이 확률로 '이번 층엔 있다/없다'가 갈린다. 없어도 공략은 가능(결정 ⑳B, 2026-09-28). [PLAYTEST] 임시 0.5.")]
        [SerializeField, Range(0f, 1f)] private float favoredAnimalChance = 0.5f;

        [Header("보스 프리팹")]
        [Tooltip("보스 프리팹 원본(Boss_Ogre). 방 템플릿에 박힌 보스 인스턴스를 이 원본으로 바꿔 스폰한다 — 템플릿 안 중첩 인스턴스는 에디터가 _assetId를 " +
                 "바꿔 저장하는 일이 잦아, 빌드끼리/빌드-에디터 사이에 assetId가 어긋나면 참가자 쪽에서 스폰이 실패한다(2026-09-30 2인 검증).")]
        [SerializeField] private GameObject bossPrefab;

        [Header("보스 상황 특성 (GDD v2.20~2.21 ⑮A·⑯A 상황 요구 1차)")]
        [Tooltip("켜져 있으면 층마다 갑옷/연타 휘두르기/재생 중 1개를 무작위로(직전 층과 다르게) 보스에 붙인다. 끄면 항상 없음. [PLAYTEST]")]
        [SerializeField] private bool bossSituationsEnabled = true;

        [SyncVar] private int floor = 1;
        // 이 층 동물 id 목록(쉼표 구분, 언어 표 키 trait.<id>.name의 id — 2026-10-06, 예전엔 완성된 한국어 이름 문자열).
        // 각 클라이언트가 AnimalPoolLabel에서 자기 언어 이름으로 만든다.
        [SyncVar] private string animalPoolIds;
        [SyncVar] private ElementType currentFloorElement = ElementType.None;
        // 이 층 보스 상황 특성. floor보다 아래에 선언해 원격 클라이언트 훅 시점에 floor가 이미 새 값이 되도록 한다(선언 순서대로 역직렬화).
        [SyncVar(hook = nameof(OnBossSituationChanged))] private BossSituation currentBossSituation = BossSituation.None;

        private readonly System.Collections.Generic.List<AnimalEntry> _animalPool = new System.Collections.Generic.List<AnimalEntry>();
        // 이번 런의 층별 속성 순서(서버 전용). GDD '스테이지-속성 1:1 매칭' — 한 런 안에서 5속성이 한 번씩만 나온다.
        private readonly System.Collections.Generic.List<ElementType> _floorElements = new System.Collections.Generic.List<ElementType>();
        private bool _runVictoryShown;

        public static FloorProgression Instance { get; private set; }
        public int Floor => floor;
        public int MaxFloors => maxFloors;
        public bool IsLastFloor => floor >= maxFloors;
        public System.Collections.Generic.IReadOnlyList<AnimalEntry> CurrentAnimalPool => _animalPool;
        /// <summary>등록된 동물 전체(층 풀과 무관). 디버그 메뉴 동물 섹션용. 비어 있으면 빈 배열.</summary>
        public System.Collections.Generic.IReadOnlyList<AnimalEntry> AllAnimals => animals ?? System.Array.Empty<AnimalEntry>();
        /// <summary>이 층 동물 이름 목록(현재 언어, 예: "소, 뱀"). 없으면 빈 문자열.</summary>
        public string AnimalPoolLabel => AnimalLabelFromIds(animalPoolIds);
        /// <summary>이 층의 속성(보스 선택지 ①). 층 생성 직전에 정해진다.</summary>
        public ElementType CurrentFloorElement => currentFloorElement;
        /// <summary>이 층 보스 상황 특성. 층 생성 직전(Generate 전)에 정해진다 — 보스 스폰(BossStub.OnStartServer)이 읽는다.</summary>
        public BossSituation CurrentBossSituation => currentBossSituation;
        public GameObject BossPrefab => bossPrefab;

        /// <summary>상황 특성이 정해지거나 바뀌었을 때(호스트·클라이언트 각자, SyncVar 훅). 인자: 새 특성, 층 번호. 배너(FloorSituationBanner)가 구독한다.</summary>
        public static event System.Action<BossSituation, int> BossSituationAnnounced;

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

        /// <summary>마지막 층 포탈에 파티 전원이 서면 서버가 부른다(FloorExit) — 모든 화면에 승리 화면. 한 판에 한 번.</summary>
        [Server]
        public void ServerRunVictory()
        {
            if (_runVictoryShown)
            {
                return;
            }

            _runVictoryShown = true;
            RpcRunVictory();
        }

        [ClientRpc]
        private void RpcRunVictory()
        {
            // 각자 자기 최종 속성으로 문구를 만든다.
            var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<MainAbilityController>() : null;
            ElementType element = local != null ? local.CurrentElement : ElementType.None;
            AbsorbRoguelike.UI.RunEndScreenController.ShowResult(null, Loc.Pack("result.victory_message", Loc.KeyArg(element.LocKey())));
        }

        /// <summary>동물 1종의 언어 표 id = 잡몹 패시브 특성의 LocId(예: OxKnockback). 특성이 없으면 displayName(그대로 표시).</summary>
        public static string AnimalId(AnimalEntry entry)
        {
            var stub = entry != null && entry.normalPrefab != null ? entry.normalPrefab.GetComponent<Monster.MonsterStub>() : null;
            if (stub != null && stub.TraitData != null)
            {
                return stub.TraitData.LocId;
            }

            return entry != null ? entry.displayName : "";
        }

        private static string AnimalLabelFromIds(string ids)
        {
            if (string.IsNullOrEmpty(ids))
            {
                return "";
            }

            string[] parts = ids.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Loc.TryGet(Absorption.PassiveTraitData.NameKey(parts[i]), out string name) ? name : parts[i];
            }

            return string.Join(Loc.Get("common.list_separator"), parts);
        }

        /// <summary>5속성(None 제외)을 Fisher-Yates로 섞어 층 순서로 쓴다(서버, DungeonGameManager.OnStartServer가 부름).</summary>
        public void ShuffleFloorElements()
        {
            _floorElements.Clear();
            _floorElements.AddRange(ElementTypeExtensions.AllElements);
            for (int i = _floorElements.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_floorElements[i], _floorElements[j]) = (_floorElements[j], _floorElements[i]);
            }

            if (maxFloors > _floorElements.Count)
            {
                Debug.LogWarning($"[DungeonGameManager] maxFloors({maxFloors})가 속성 수({_floorElements.Count})보다 많아 {_floorElements.Count + 1}층부터 속성이 반복된다.");
            }
        }

        /// <summary>
        /// 보스 방 출구(FloorExit)에 들어가면 호출된다(서버). 층 번호를 올리고 새 던전을 생성해 플레이어를 새
        /// 시작 방으로 옮긴다. 씬을 다시 불러오는 "다시 시작"과 달리 플레이어 오브젝트는 그대로라 체력·패시브·속성이
        /// 유지된다. 마지막 층에서는 호출하지 않는다(승리 화면).
        /// </summary>
        [Server]
        public void AdvanceFloor()
        {
            if (DungeonGameManager.Instance.IsGenerating || IsLastFloor)
            {
                return;
            }

            floor++;
            Debug.Log($"[DungeonGameManager] advancing to floor {floor}/{maxFloors}");
            foreach (Player.AbsorptionController absorption in FindObjectsByType<Player.AbsorptionController>(FindObjectsSortMode.None))
            {
                absorption.SetFloorExitReady(false); // 새 층 보스를 흡수하기 전까지 출구 준비 해제
            }

            // Edgar는 재생성 때 이전 레벨을 Object.Destroy로 지운다. 거기 붙어 있던 네트워크 오브젝트(몬스터·보스·
            // 시체)와 레벨 밖에 떠 있는 것(파편·뱉은 투사체·떨어진 패시브)은 Mirror 쪽에서 먼저 정리한다.
            // 플레이어(연결 소유)와 씬 오브젝트(이 매니저 등)는 남긴다.
            // 비주얼 3단계 V9(2026-10-15): 호스트 화면은 아래 NetworkServer.Destroy가 같은 호출 안에서 OnStopClient(디졸브 잔상 훅)를 부르므로,
            // 파괴 전에 로컬 잔상 억제창을 켠다(표시 전용 static, 네트워크 없음). 원격 화면은 FloorExit 예측 + DissolveGhost 층 번호 비교가 막는다.
            AbsorbRoguelike.UI.DissolveGhost.SuppressFor(1f);
            var toDestroy = new System.Collections.Generic.List<NetworkIdentity>();
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity != null && identity.connectionToClient == null && identity.sceneId == 0)
                {
                    toDestroy.Add(identity);
                }
            }

            foreach (NetworkIdentity identity in toDestroy)
            {
                NetworkServer.Destroy(identity.gameObject);
            }

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                var absorption = connection.identity != null ? connection.identity.GetComponent<Player.AbsorptionController>() : null;
                if (absorption != null)
                {
                    absorption.ResetForNewFloor();
                }
            }

            DungeonGameManager.Instance.StartFloor();
        }

        /// <summary>
        /// 층 시작 준비(서버, DungeonGameManager 층 시작 코루틴이 Generate() 전에 부름): 층 속성 → 상황 특성 → 동물 풀 순서로 정한다.
        /// 층 속성은 반드시 Generate() 전에 정한다 — 보스(BossStub.OnStartServer)가 이 값을 읽어 선택지 ①로 쓰므로,
        /// 순서가 바뀌면 이전 층 속성을 읽게 된다. AdvanceFloor는 floor++ 뒤에 여기로 오므로 자동으로 다음 속성이 된다.
        /// 상황 특성도 Generate() 전에 정한다 — 보스 스폰(BossStub.OnStartServer)이 갑옷 초기값을 이 값으로 정한다.
        /// </summary>
        public void ServerPrepareFloor()
        {
            currentFloorElement = _floorElements.Count > 0 ? _floorElements[(floor - 1) % _floorElements.Count] : ElementType.None;
            Debug.Log($"[DungeonGameManager] floor {floor}/{maxFloors} element: {currentFloorElement.DisplayName()}");

            RollBossSituation();
            RollAnimalPool();
        }

        // 층마다(1층 포함) 상황 특성 3종 중 1개를 뽑는다. 직전 층과 같은 특성은 제외([PLAYTEST]). 동물 풀과는 독립 무작위다
        // (유리한 동물이 풀에 있다는 보장 없음 — 범위 밖, 마스터 세션 결정 대기).
        private void RollBossSituation()
        {
            if (!bossSituationsEnabled)
            {
                currentBossSituation = BossSituation.None;
                return;
            }

            var candidates = new System.Collections.Generic.List<BossSituation>(BossSituationExtensions.AllSituations);
            candidates.Remove(currentBossSituation); // 1층은 None이라 아무것도 빠지지 않는다
            currentBossSituation = candidates[Random.Range(0, candidates.Count)];
            Debug.Log($"[DungeonGameManager] floor {floor} boss situation: {currentBossSituation.DisplayName()}");
        }

        /// <summary>
        /// 디버그 메뉴(F1): 현재 층의 상황 특성을 즉시 바꾼다(서버). 이미 스폰된 보스에도 바로 반영한다 — 갑옷은 가득 채워
        /// 다시 시작하고, 재생은 다음 프레임부터, 연타 휘두르기는 다음 타격 시점에 읽는다(BossPatternSwing).
        /// </summary>
        [Server]
        public void DebugSetBossSituation(BossSituation situation)
        {
            currentBossSituation = situation;
            foreach (Monster.BossStub boss in FindObjectsByType<Monster.BossStub>(FindObjectsSortMode.None))
            {
                if (boss != null && boss.isServer && !boss.IsDefeated)
                {
                    boss.ApplySituation(situation);
                }
            }

            Debug.Log($"[DungeonGameManager] debug boss situation set: {situation.DisplayName()}");
        }

        private void OnBossSituationChanged(BossSituation oldValue, BossSituation newValue)
        {
            BossSituationAnnounced?.Invoke(newValue, floor);
        }

        // 상황 특성의 유리한 동물 보장(결정 ⑳B): favoredAnimalChance로 굴려 성공이면 풀에 넣고(없으면 무작위 1종과 교체),
        // 실패면 풀에서 뺀다(있으면 남은 후보 중 무작위 1종과 교체). RollBossSituation이 먼저 불린 뒤에 호출된다.
        private void ApplyFavoredAnimalRule(System.Collections.Generic.List<AnimalEntry> leftovers)
        {
            Absorption.AbsorbSkill favoredSkill = currentBossSituation.FavoredAnimalSkill();
            if (favoredSkill == Absorption.AbsorbSkill.None)
            {
                return;
            }

            string favoredName = favoredSkill.ToString(); // 로그용
            AnimalEntry favored = System.Array.Find(animals, a => a != null && AnimalSkill(a) == favoredSkill);
            if (favored == null)
            {
                Debug.LogWarning($"[DungeonGameManager] favored animal '{favoredName}' not found in animals");
                return;
            }

            bool include = Random.value < favoredAnimalChance;
            int index = _animalPool.IndexOf(favored);
            if (include && index < 0)
            {
                _animalPool[Random.Range(0, _animalPool.Count)] = favored;
            }
            else if (!include && index >= 0)
            {
                leftovers.Remove(favored);
                if (leftovers.Count > 0)
                {
                    _animalPool[index] = leftovers[Random.Range(0, leftovers.Count)];
                }
            }

            Debug.Log($"[DungeonGameManager] floor {floor} favored animal {favoredName}: {(include ? "included" : "excluded")}");
        }

        private static Absorption.AbsorbSkill AnimalSkill(AnimalEntry entry)
        {
            var stub = entry != null && entry.normalPrefab != null ? entry.normalPrefab.GetComponent<Monster.MonsterStub>() : null;
            return stub != null && stub.TraitData != null ? stub.TraitData.Skill : Absorption.AbsorbSkill.None;
        }

        // 층이 바뀔 때마다(1층 포함) 전체 동물 중 animalsPerFloor종을 무작위로 뽑는다. 연속 층 중복은 허용([PLAYTEST]).
        // 실제 교체는 던전 생성 중 RoomPostProcessing이 이 풀을 보고 한다.
        private void RollAnimalPool()
        {
            _animalPool.Clear();
            if (animals == null || animals.Length == 0)
            {
                animalPoolIds = "";
                return;
            }

            var candidates = new System.Collections.Generic.List<AnimalEntry>(animals);
            int count = Mathf.Clamp(animalsPerFloor, 1, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                int pick = Random.Range(0, candidates.Count);
                _animalPool.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }

            ApplyFavoredAnimalRule(candidates);

            var ids = new System.Collections.Generic.List<string>();
            foreach (AnimalEntry entry in _animalPool)
            {
                ids.Add(AnimalId(entry));
            }

            animalPoolIds = string.Join(",", ids);
            Debug.Log($"[DungeonGameManager] floor {floor} animal pool: {animalPoolIds}");
        }
    }
}
