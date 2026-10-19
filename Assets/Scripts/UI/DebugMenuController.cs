using AbsorbRoguelike.Abilities;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 게임 내 디버그 메뉴(2026-09-28 유저 요청). F1로 오른쪽 패널을 열고 닫는다. 에디터·개발 빌드 전용이고
    /// 릴리스 빌드에서는 Awake에서 스스로 꺼진다. 버튼은 전부 호스트(NetworkServer.active)의 서버 메서드를 직접
    /// 호출한다 — 원격 클라이언트용 Command 경로는 없어서 원격 클라이언트에서는 모든 버튼이 비활성이다.
    /// 게임 규칙은 바꾸지 않고 기존 흐름의 진입점만 부른다(자세한 목록: docs/04_구현위키/디버그메뉴.md).
    /// 버튼은 코드로 만든다(기본 uGUI Image/Button/Text, 새 아트 없음).
    /// </summary>
    public class DebugMenuController : MonoBehaviour
    {
        [Tooltip("열고 닫는 패널(화면 오른쪽). 버튼은 실행 시 이 아래에 코드로 만든다.")]
        [SerializeField] private RectTransform panel;
        [Tooltip("HUD와 같은 폰트. 비어 있으면 기본 LegacyRuntime 폰트.")]
        [SerializeField] private Font font;

        /// <summary>
        /// '몬스터 수치 표시' 토글(2026-10-13, 레벨 디자인 시트 1차, 기본 꺼짐). 켜면 WorldHealthBar가 잡몹·정예·보스 체력바 위에
        /// '현재/최대' 숫자를, HudController가 '층·인원·체력 ×·공격 ×' 줄을 띄운다. 이 화면에서만 보이는 표시 전용(호스트·원격 모두 켤 수 있음).
        /// 릴리스 빌드에서는 항상 false.
        /// </summary>
        public static bool ShowMonsterStats { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float RefreshInterval = 0.25f;
        private const int FontSize = 16;
        private const float CellHeight = 30f;

        private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.3f, 0.95f);
        private static readonly Color HeaderColor = new Color(1f, 0.85f, 0.4f);

        private readonly System.Collections.Generic.List<(UnityEngine.UI.Button button, System.Func<bool> canUse)> _buttons =
            new System.Collections.Generic.List<(UnityEngine.UI.Button, System.Func<bool>)>();
        private readonly System.Collections.Generic.List<(UnityEngine.UI.Text text, System.Func<string> label)> _dynamicLabels =
            new System.Collections.Generic.List<(UnityEngine.UI.Text, System.Func<string>)>();

        private UnityEngine.UI.Text _infoText;
        private bool _built;
        private float _refreshTimer;

        private GameObject _player;
        private Player.PlayerHealth _health;
        private Player.AbsorptionController _absorption;
        private Abilities.MainAbilityController _ability;

        /// <summary>패널이 열려 있는지.</summary>
        public static bool IsOpen { get; private set; }

        private static DebugMenuController _instance;

        /// <summary>ESC(일시정지 메뉴 우선순위 ③)로 디버그 메뉴를 닫는다 — 2026-09-30, 기능 변경 없음.</summary>
        public static void CloseIfOpen()
        {
            if (IsOpen && _instance != null)
            {
                _instance.SetOpen(false);
            }
        }

        private void Awake()
        {
            _instance = this;
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            SetOpen(false);
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                SetOpen(!IsOpen);
            }

            if (!IsOpen)
            {
                return;
            }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = RefreshInterval;
                Refresh();
            }
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            if (panel == null)
            {
                return;
            }

            if (open && !_built)
            {
                Build();
            }

            panel.gameObject.SetActive(open);
            if (open)
            {
                _refreshTimer = 0f;
                Refresh();
            }
        }

        // ── 상태 갱신 ─────────────────────────────────────────────

        private bool CacheLocalPlayer()
        {
            var identity = Mirror.NetworkClient.localPlayer;
            if (identity == null)
            {
                _player = null;
                return false;
            }

            if (_player != identity.gameObject)
            {
                _player = identity.gameObject;
                _health = _player.GetComponent<Player.PlayerHealth>();
                _absorption = _player.GetComponent<Player.AbsorptionController>();
                _ability = _player.GetComponent<Abilities.MainAbilityController>();
            }

            return true;
        }

        private bool IsHostReady => Mirror.NetworkServer.active && CacheLocalPlayer();

        private void Refresh()
        {
            bool ready = IsHostReady;
            foreach (var (button, canUse) in _buttons)
            {
                button.interactable = ready && (canUse == null || canUse());
            }

            foreach (var (text, label) in _dynamicLabels)
            {
                text.text = label();
            }

            if (_infoText != null)
            {
                _infoText.text = ready ? InfoLine() : "호스트 전용 — 비활성";
            }
        }

        private string InfoLine()
        {
            var dungeon = Dungeon.FloorProgression.Instance;
            string floor = dungeon != null
                ? $"층 {dungeon.Floor}/{dungeon.MaxFloors} · 층 속성 {dungeon.CurrentFloorElement.DisplayName()} · 동물: {dungeon.AnimalPoolLabel}"
                : "던전 매니저 없음";
            string element = _ability != null ? _ability.CurrentElement.DisplayName() : "-";
            string trait = "없음";
            if (_absorption != null && _absorption.CurrentTrait != null)
            {
                trait = _absorption.CurrentTrait.DisplayName;
            }

            return $"{floor}\n내 속성 {element} · 특성 {trait}";
        }

        // ── UI 만들기 ─────────────────────────────────────────────

        private void Build()
        {
            _built = true;
            if (font == null)
            {
                font = DamagePopup.BuiltinFont;
            }

            var layout = panel.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = panel.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            }

            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panel.gameObject.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = panel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            }

            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            AddText("디버그 메뉴 (F1 닫기 · 호스트 전용)", FontSize + 2, HeaderColor, TextAnchor.MiddleCenter);
            _infoText = AddText("", FontSize - 2, Color.white, TextAnchor.UpperLeft);

            BuildDisplaySection();
            BuildElementSection();
            BuildAnimalSection();
            BuildMoveSection();
            BuildCombatSection();
            BuildSpawnSection();
            BuildSituationSection();
        }

        // 보스 상황 특성(GDD ⑮A·⑯A) — 현재 층 특성을 즉시 교체한다. 스폰된 보스에도 바로 반영(FloorProgression.DebugSetBossSituation).
        private void BuildSituationSection()
        {
            AddText("상황 특성 (현재 층 즉시 교체 — 스폰된 보스에도 즉시 반영, 갑옷은 가득 채워 재시작)", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            UnityEngine.UI.Text current = AddText("", FontSize - 2, Color.white, TextAnchor.MiddleLeft);
            _dynamicLabels.Add((current, () =>
            {
                var dungeon = Dungeon.FloorProgression.Instance;
                return dungeon != null ? $"현재: {Dungeon.BossSituationExtensions.DisplayName(dungeon.CurrentBossSituation)}" : "현재: -";
            }));

            RectTransform grid = AddGrid(4);
            var options = new[]
            {
                (Dungeon.BossSituation.None, "없음"),
                (Dungeon.BossSituation.Armored, "갑옷"),
                (Dungeon.BossSituation.FlurrySwing, "연타"),
                (Dungeon.BossSituation.Regenerating, "재생"),
            };
            foreach (var (situation, label) in options)
            {
                Dungeon.BossSituation captured = situation;
                AddButton(grid, label, () => Dungeon.FloorProgression.Instance.DebugSetBossSituation(captured),
                    () => Dungeon.FloorProgression.Instance != null);
            }
        }

        // 표시 전용 토글 — 게임 상태를 바꾸지 않으므로 원격 클라이언트에서도 누를 수 있다(호스트 전용 버튼 목록 _buttons에 넣지 않음).
        private void BuildDisplaySection()
        {
            AddText("표시", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            RectTransform grid = AddGrid(2);
            UnityEngine.UI.Button toggle = CreateButton(grid, "");
            UnityEngine.UI.Text label = toggle.GetComponentInChildren<UnityEngine.UI.Text>();
            toggle.onClick.AddListener(() => ShowMonsterStats = !ShowMonsterStats);
            _dynamicLabels.Add((label, () => ShowMonsterStats ? "몬스터 수치 표시: 켜짐" : "몬스터 수치 표시: 꺼짐"));

            // 비주얼 효과 전/후 비교(2026-10-15): 끄면 PostFx Volume 비활성 + 이펙트 발광 재질 원래대로(VisualFx).
            UnityEngine.UI.Button fx = CreateButton(grid, "");
            UnityEngine.UI.Text fxLabel = fx.GetComponentInChildren<UnityEngine.UI.Text>();
            fx.onClick.AddListener(() => VisualFx.Enabled = !VisualFx.Enabled);
            _dynamicLabels.Add((fxLabel, () => VisualFx.Enabled ? "비주얼 효과: 켜짐" : "비주얼 효과: 꺼짐"));
        }

        private void BuildElementSection()
        {
            AddText("속성", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            RectTransform grid = AddGrid(3);
            foreach (Abilities.ElementType element in Abilities.ElementTypeExtensions.AllElements)
            {
                Abilities.ElementType captured = element;
                AddButton(grid, element.DisplayName(), () => _ability.ApplyElement(captured), () => _ability != null);
            }

            AddButton(grid, "해제", () => _ability.ApplyElement(Abilities.ElementType.None), () => _ability != null);
        }

        private void BuildAnimalSection()
        {
            AddText("동물 (특성 바로 얻기)", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            RectTransform grid = AddGrid(3);
            var dungeon = Dungeon.FloorProgression.Instance;
            if (dungeon != null)
            {
                foreach (var entry in dungeon.AllAnimals)
                {
                    var stub = entry != null && entry.normalPrefab != null ? entry.normalPrefab.GetComponent<Monster.MonsterStub>() : null;
                    if (stub == null || stub.TraitData == null)
                    {
                        continue;
                    }

                    Absorption.PassiveTraitData trait = stub.TraitData;
                    string label = !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : trait.DisplayName;
                    AddButton(grid, label, () => _absorption.DebugGrantTrait(trait), () => _absorption != null);
                }
            }

            AddButton(grid, "특성 해제", () => _absorption.DebugClearTrait(), () => _absorption != null && _absorption.CurrentTrait != null);
        }

        private void BuildMoveSection()
        {
            AddText("이동", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            RectTransform grid = AddGrid(2);
            AddButton(grid, "보스 방으로", () => TeleportToBossRoom(_player), () => FindBossRoom() != null);
            AddButton(grid, "시작 방으로", () => TeleportToSpawnRoom(_player), () => SpawnPoint() != null);
            AddButton(grid, "다음 층으로", () => Dungeon.FloorProgression.Instance.AdvanceFloor(),
                () => Dungeon.FloorProgression.Instance != null && !Dungeon.FloorProgression.Instance.IsLastFloor);
        }

        private void BuildCombatSection()
        {
            AddText("전투", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            RectTransform grid = AddGrid(2);
            AddButton(grid, "현재 방 몬스터 전멸", KillCurrentRoomEnemies, null);
            AddButton(grid, "보스 체력 1로", SetBossHealthToOne, () => FindAliveBoss() != null);
            // 보스 페이즈 확인(BossBrain + 패턴 세트 에셋, 70/40/15% 기준): 50% → 페이즈 2, 20% → 3, 10% → 4(폭주)
            AddButton(grid, "보스 체력 50%", () => SetBossHealthPercent(0.5f), () => FindAliveBoss() != null);
            AddButton(grid, "보스 체력 20%", () => SetBossHealthPercent(0.2f), () => FindAliveBoss() != null);
            AddButton(grid, "보스 체력 10%", () => SetBossHealthPercent(0.1f), () => FindAliveBoss() != null);
            AddButton(grid, "패턴 C 즉시", () => UseBossPattern("C"), () => FindAliveBossAttack() != null);
            AddButton(grid, "패턴 D 즉시", () => UseBossPattern("D"), () => FindAliveBossAttack() != null);
            AddButton(grid, "패턴 E 즉시", () => UseBossPattern("E"), () => FindAliveBossAttack() != null);
            AddButton(grid, "체력 전체 회복", () => _health.DebugHealFull(), () => _health != null && !_health.IsDead);
            UnityEngine.UI.Button invincible = AddButton(grid, "", () => _health.SetDebugInvincible(!_health.DebugInvincible), () => _health != null);
            _dynamicLabels.Add((invincible.GetComponentInChildren<UnityEngine.UI.Text>(),
                () => _health != null && _health.DebugInvincible ? "무적: 켜짐" : "무적: 꺼짐"));
            AddButton(grid, "파편 머금기", () => _absorption.DebugHoldDebris(), () => _absorption != null && !_absorption.HasDebris);
        }

        // 잡몹 소환(2026-10-02): 원하는 동물 패턴을 바로 체험하기 위함. 방 잠금·클리어 판정과 연결하지 않는다(흡수는 그대로 됨).
        private bool _spawnElite;
        private readonly System.Collections.Generic.List<GameObject> _debugSpawned = new System.Collections.Generic.List<GameObject>();

        private void BuildSpawnSection()
        {
            AddText("잡몹 소환 (플레이어 오른쪽 2.5칸 · 방 잠금과 무관)", FontSize, HeaderColor, TextAnchor.MiddleLeft);
            RectTransform grid = AddGrid(4);
            var dungeon = Dungeon.FloorProgression.Instance;
            if (dungeon != null)
            {
                foreach (var entry in dungeon.AllAnimals)
                {
                    if (entry == null || entry.normalPrefab == null)
                    {
                        continue;
                    }

                    var captured = entry;
                    string label = !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : entry.normalPrefab.name;
                    AddButton(grid, label, () => SpawnMonster(captured), () => _player != null);
                }
            }

            UnityEngine.UI.Button elite = AddButton(grid, "", () => _spawnElite = !_spawnElite, null);
            _dynamicLabels.Add((elite.GetComponentInChildren<UnityEngine.UI.Text>(), () => _spawnElite ? "정예: 켜짐" : "정예: 꺼짐"));
            AddButton(grid, "소환 몹 제거", ClearDebugSpawned, () => _debugSpawned.Exists(m => m != null));
        }

        private void SpawnMonster(Dungeon.FloorProgression.AnimalEntry entry)
        {
            GameObject prefab = _spawnElite && entry.elitePrefab != null ? entry.elitePrefab : entry.normalPrefab;
            GameObject monster = SpawnMonsterAt(prefab, _player.transform.position + new Vector3(2.5f, 0f, 0f));
            _debugSpawned.RemoveAll(m => m == null);
            _debugSpawned.Add(monster);
        }

        private void ClearDebugSpawned()
        {
            DestroySpawned(_debugSpawned);
        }

        // ── 잡몹 소환 정적 헬퍼 ─────────────────────────────────
        // 에디터 트레일러 촬영 도구(Assets/Editor/Trailer — 몽타주 자동 조작 샷)도 호출한다(2026-10-19, 동작 변화 없이 추출).
        // 호스트(서버) 전제 — 호출하는 쪽이 NetworkServer.active를 확인한다.

        /// <summary>동물 표시 이름(AnimalEntry.displayName, 예: "소") 또는 일반 프리팹 이름으로 동물 항목을 찾는다. 없으면 null.</summary>
        public static Dungeon.FloorProgression.AnimalEntry FindAnimal(string animalName)
        {
            var dungeon = Dungeon.FloorProgression.Instance;
            if (dungeon == null || string.IsNullOrEmpty(animalName))
            {
                return null;
            }

            foreach (var entry in dungeon.AllAnimals)
            {
                if (entry == null || entry.normalPrefab == null)
                {
                    continue;
                }

                if (entry.displayName == animalName || entry.normalPrefab.name == animalName)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>프리팹을 월드 루트 위치에 만들고 NetworkServer.Spawn한다(방 잠금·클리어 판정과 무관). 만든 오브젝트를 돌려준다.</summary>
        public static GameObject SpawnMonsterAt(GameObject prefab, Vector3 position)
        {
            GameObject monster = Instantiate(prefab, position, Quaternion.identity); // 월드 루트에 바로 생성(중첩 스폰 위치 문제 없음)
            Mirror.NetworkServer.Spawn(monster);
            Debug.Log($"[DebugMenu] spawned {prefab.name} at {position}");
            return monster;
        }

        /// <summary>목록의 소환 몹(이미 사라진 것은 건너뜀)을 NetworkServer.Destroy하고 목록을 비운다.</summary>
        public static void DestroySpawned(System.Collections.Generic.List<GameObject> monsters)
        {
            foreach (GameObject monster in monsters)
            {
                if (monster != null)
                {
                    Mirror.NetworkServer.Destroy(monster);
                }
            }

            monsters.Clear();
        }

        private UnityEngine.UI.Text AddText(string content, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var text = go.AddComponent<UnityEngine.UI.Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;
            return text;
        }

        private RectTransform AddGrid(int columns)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var grid = go.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            float width = panel.rect.width > 0f ? panel.rect.width - 24f : 396f;
            float spacing = 6f;
            grid.cellSize = new Vector2((width - spacing * (columns - 1)) / columns, CellHeight);
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            return (RectTransform)go.transform;
        }

        private UnityEngine.UI.Button AddButton(RectTransform parent, string label, System.Action onClick, System.Func<bool> canUse)
        {
            UnityEngine.UI.Button button = CreateButton(parent, label);
            button.onClick.AddListener(() =>
            {
                if (!IsHostReady)
                {
                    return;
                }

                onClick();
                Refresh();
            });
            _buttons.Add((button, canUse));
            return button;
        }

        private UnityEngine.UI.Button CreateButton(RectTransform parent, string label)
        {
            var go = new GameObject(label.Length > 0 ? label : "Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = ButtonColor;
            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<UnityEngine.UI.Text>();
            text.font = font;
            text.fontSize = FontSize - 1;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.text = label;
            return button;
        }

        // ── 이동 ─────────────────────────────────────────────
        // 아래 정적 헬퍼는 에디터 트레일러 촬영 도구(Assets/Editor/Trailer)도 호출한다(2026-10-19, 동작 변화 없이 추출).
        // 호스트(서버) 전제 — 호출하는 쪽이 NetworkServer.active를 확인한다.

        /// <summary>플레이어를 위치로 옮긴다(리지드바디 위치·속도도 맞춤). player가 null이면 무시.</summary>
        public static void Teleport(GameObject player, Vector2 position)
        {
            if (player == null)
            {
                return;
            }

            player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
            }

            Debug.Log($"[DebugMenu] teleported {player.name} to {position}");
        }

        /// <summary>보스 방(RoomArea가 있는 첫 번째). 없으면 null.</summary>
        public static Dungeon.RoomClearController FindBossRoom()
        {
            foreach (var room in FindObjectsByType<Dungeon.RoomClearController>(FindObjectsSortMode.None))
            {
                if (room.IsBossRoom && room.RoomArea != null)
                {
                    return room;
                }
            }

            return null;
        }

        // 보스 방 입장 트리거 안, 보스 배치 위치에서 가장 먼 쪽 경계 안쪽 2유닛 지점으로 옮긴다. 트리거 진입으로
        // 기존 방 잠금·보스 스폰 흐름이 그대로 일어난다.
        public static void TeleportToBossRoom(GameObject player)
        {
            var room = FindBossRoom();
            if (room == null)
            {
                return;
            }

            Physics2D.SyncTransforms();
            Bounds b = room.RoomArea.bounds;
            Vector2 center = b.center;
            Vector2 boss = room.BossPosition ?? center;
            const float inset = 2f;
            var candidates = new[]
            {
                new Vector2(b.min.x + inset, center.y),
                new Vector2(b.max.x - inset, center.y),
                new Vector2(center.x, b.min.y + inset),
                new Vector2(center.x, b.max.y - inset),
            };
            System.Array.Sort(candidates, (p, q) => Vector2.Distance(q, boss).CompareTo(Vector2.Distance(p, boss)));

            Vector2 target = candidates[0];
            foreach (Vector2 candidate in candidates)
            {
                if (room.RoomArea.OverlapPoint(candidate) && !OverlapsSolid(candidate))
                {
                    target = candidate;
                    break;
                }
            }

            if (Vector2.Distance(target, boss) < 3f)
            {
                Debug.LogWarning($"[DebugMenu] boss room is small — teleport point is only {Vector2.Distance(target, boss):0.0} units from the boss");
            }

            Teleport(player, target);
        }

        /// <summary>시작 지점(NetworkManager.startPositions의 마지막 유효 항목 = 현재 층 시작 방). 없으면 null.</summary>
        public static Transform SpawnPoint()
        {
            var starts = Mirror.NetworkManager.startPositions;
            for (int i = starts.Count - 1; i >= 0; i--)
            {
                if (starts[i] != null)
                {
                    return starts[i];
                }
            }

            return null;
        }

        public static void TeleportToSpawnRoom(GameObject player)
        {
            Transform point = SpawnPoint();
            if (point != null)
            {
                Teleport(player, point.position);
            }
        }

        /// <summary>점이 벽 등 정적 콜라이더와 겹치는지(리지드바디가 있는 몬스터·플레이어는 제외).</summary>
        public static bool OverlapsSolid(Vector2 point)
        {
            foreach (Collider2D col in Physics2D.OverlapPointAll(point))
            {
                if (!col.isTrigger && col.attachedRigidbody == null)
                {
                    return true; // 벽 등 정적 콜라이더(몬스터·플레이어처럼 리지드바디가 있는 것은 제외)
                }
            }

            return false;
        }

        // ── 전투 ─────────────────────────────────────────────

        private void KillCurrentRoomEnemies()
        {
            Vector2 position = _player.transform.position;
            foreach (var room in FindObjectsByType<Dungeon.RoomClearController>(FindObjectsSortMode.None))
            {
                if (room.ContainsPoint(position))
                {
                    room.DebugKillAllEnemies();
                    return;
                }
            }

            Debug.Log("[DebugMenu] 현재 위치를 포함하는 전투 방이 없어 몬스터 전멸을 건너뛴다.");
        }

        public static Monster.BossStub FindAliveBoss()
        {
            foreach (var boss in FindObjectsByType<Monster.BossStub>(FindObjectsSortMode.None))
            {
                if (!boss.IsDefeated && boss.isServer)
                {
                    return boss;
                }
            }

            return null;
        }

        public static Monster.BossBrain FindAliveBossAttack()
        {
            var boss = FindAliveBoss();
            return boss != null ? boss.GetComponent<Monster.BossBrain>() : null;
        }

        // 쿨·페이즈 제한 무시. 보스가 다른 패턴 중이면 다음 Idle에 시작한다(BossBrain.DebugUsePattern — 세트에서 PatternId가 같은 항목, 없으면 무시).
        public static void UseBossPattern(string patternId)
        {
            var attack = FindAliveBossAttack();
            if (attack != null)
            {
                attack.DebugUsePattern(patternId);
            }
        }

        public static void SetBossHealthPercent(float ratio)
        {
            var boss = FindAliveBoss();
            if (boss != null)
            {
                boss.DebugSetHealthPercent(ratio);
            }
        }

        private void SetBossHealthToOne()
        {
            var boss = FindAliveBoss();
            if (boss != null)
            {
                boss.DebugSetHealthToOne();
            }
        }
#else
        private void Awake()
        {
            gameObject.SetActive(false); // 릴리스 빌드에는 디버그 메뉴가 없다
        }
#endif
    }
}
