using System;
using System.Collections.Generic;
using System.Text;
using AbsorbRoguelike.Localization;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 옵션 창(2026-09-30, 타이틀·일시정지·옵션) — 코드로 만드는 임시 uGUI. 타이틀 '옵션'과 일시정지 메뉴 '옵션'이 같은 창을 연다.
    ///   (a) 볼륨: 전체/배경음/효과음 0~100 → <see cref="AudioSettingsStore"/>에 즉시 저장.
    ///   (b) 키 설정: Gameplay 맵의 Keyboard&amp;Mouse 바인딩만 바꾼다(게임패드 바인딩은 그대로). 리바인딩은
    ///       <see cref="InputBindingStore.Shared"/>에서 하고, 끝나면 저장 → 게임 중 입력 인스턴스 전부에 재적용.
    ///       같은 키가 두 행 이상이면 빨간색 + '중복 키' 경고(자동 교체 없음). Tab/ESC/F1은 고정 키(변경 불가, 안내만).
    /// NetworkManager 오브젝트(DontDestroyOnLoad)에 붙는다. ESC: 리바인딩 중이면 리바인딩 취소만, 아니면 창 닫기.
    /// </summary>
    public class OptionsMenuController : MonoBehaviour
    {
        private const int CanvasSortOrder = 50; // 시작 메뉴(40)·일시정지 메뉴(45) 위
        private const string KeyboardMouseGroup = "Keyboard&Mouse";
        private static readonly string[] FixedKeyPaths = { "<Keyboard>/tab", "<Keyboard>/escape", "<Keyboard>/f1" };
        private static readonly string[] FixedKeyNames = { "Tab", "ESC", "F1" };

        private static OptionsMenuController _instance;

        /// <summary>옵션 창이 열려 있는지.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>마지막으로 창이 닫힌 프레임 — 같은 프레임의 ESC를 일시정지 메뉴가 다시 처리하지 않게.</summary>
        public static int LastClosedFrame { get; private set; } = -1;

        private sealed class KeyRow
        {
            public string LabelKey;
            public InputAction Action;
            public int BindingIndex;
            public Text LabelText;
            public Text KeyText;
            public Button ChangeButton;
        }

        private readonly List<KeyRow> _rows = new List<KeyRow>();
        private GameObject _root;
        private Slider _masterSlider;
        private Slider _bgmSlider;
        private Slider _sfxSlider;
        private Text _masterValue;
        private Text _bgmValue;
        private Text _sfxValue;
        private Text _warningText;
        private Button _resetButton;
        private Button _closeButton;
        private Button _replayIntroButton;  // 첫 플레이 안내(2026-10-16): 누르면 다음 시작 때 인트로를 다시 보여 준다
        private Button _resetHintsButton;
        private Button _koreanButton;
        private Button _englishButton;
        private Action _onClosed;
        private InputActionRebindingExtensions.RebindingOperation _rebind;
        private int _rebindEndFrame = -1;

        /// <summary>옵션 창을 연다. 닫히면 onClosed를 부른다(타이틀·일시정지 메뉴로 복귀).</summary>
        public static void Open(Action onClosed)
        {
            if (_instance == null)
            {
                Debug.LogWarning("[OptionsMenu] no OptionsMenuController in scene");
                onClosed?.Invoke();
                return;
            }

            _instance.OpenInternal(onClosed);
        }

        public static void Close()
        {
            if (_instance != null)
            {
                _instance.CloseInternal();
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                enabled = false; // 씬 재로드 때 생기는 중복 NetworkManager 쪽 — 곧 파괴된다
                return;
            }

            _instance = this;
            Build();
            _root.SetActive(false);
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                CloseInternal();
            }
        }

        private void OnDestroy()
        {
            Loc.Changed -= OnLanguageChanged;
            DisplaySettingsStore.Changed -= RefreshDisplayRows;
            if (_instance == this)
            {
                _rebind?.Dispose(); // 파괴 중에는 UI 갱신 없이 정리만
                _rebind = null;
                IsOpen = false;
                _instance = null;
            }
        }

        private void Update()
        {
            if (!IsOpen || _rebind != null || Time.frameCount == _rebindEndFrame || StoryIntroController.IsPlaying)
            {
                return; // 리바인딩 중 ESC는 리바인딩 작업이 취소로 처리한다
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseInternal();
            }
        }

        private void OpenInternal(Action onClosed)
        {
            _onClosed = onClosed;
            IsOpen = true;
            _root.SetActive(true);
            _masterSlider.SetValueWithoutNotify(AudioSettingsStore.Master);
            _bgmSlider.SetValueWithoutNotify(AudioSettingsStore.Bgm);
            _sfxSlider.SetValueWithoutNotify(AudioSettingsStore.Sfx);
            RefreshVolumeLabels();
            RefreshKeys();
            RefreshLanguageButtons();
        }

        private void CloseInternal()
        {
            CancelRebind();
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            LastClosedFrame = Time.frameCount;
            if (_root != null)
            {
                _root.SetActive(false);
            }

            Action callback = _onClosed;
            _onClosed = null;
            callback?.Invoke();
        }

        // ── 볼륨 ──────────────────────────────────────────────────

        private void OnMasterChanged(float value)
        {
            AudioSettingsStore.Master = Mathf.RoundToInt(value);
            RefreshVolumeLabels();
        }

        private void OnBgmChanged(float value)
        {
            AudioSettingsStore.Bgm = Mathf.RoundToInt(value);
            RefreshVolumeLabels();
        }

        private void OnSfxChanged(float value)
        {
            AudioSettingsStore.Sfx = Mathf.RoundToInt(value);
            RefreshVolumeLabels();
        }

        private void RefreshVolumeLabels()
        {
            _masterValue.text = AudioSettingsStore.Master.ToString();
            _bgmValue.text = AudioSettingsStore.Bgm.ToString();
            _sfxValue.text = AudioSettingsStore.Sfx.ToString();
        }

        // ── 키 설정 ───────────────────────────────────────────────

        private void StartRebind(KeyRow row)
        {
            if (_rebind != null || row.Action == null || row.BindingIndex < 0)
            {
                return;
            }

            row.Action.Disable(); // 공유 인스턴스는 원래 꺼져 있지만, 리바인딩은 꺼진 액션에서만 가능
            row.KeyText.text = Loc.Get("options.press_key");
            row.KeyText.color = UIBuilder.SectionColor;
            _warningText.text = "";
            SetChangeButtonsInteractable(false);

            _rebind = row.Action.PerformInteractiveRebinding(row.BindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Mouse>/press")
                .WithControlsExcluding("<Mouse>/clickCount")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Gamepad>")
                .WithMatchingEventsBeingSuppressed()
                .OnComplete(_ => FinishRebind(true))
                .OnCancel(_ => FinishRebind(false))
                .Start();
        }

        private void FinishRebind(bool completed)
        {
            if (_rebind == null)
            {
                return;
            }

            _rebind.Dispose();
            _rebind = null;
            _rebindEndFrame = Time.frameCount;
            if (completed)
            {
                InputBindingStore.SaveFromShared(); // 저장 + 게임 중 입력 인스턴스 전부에 재적용
            }

            SetChangeButtonsInteractable(true);
            RefreshKeys();
        }

        private void CancelRebind()
        {
            if (_rebind != null)
            {
                _rebind.Cancel(); // OnCancel → FinishRebind(false)
            }

            if (_rebind != null)
            {
                _rebind.Dispose();
                _rebind = null;
            }
        }

        private void OnResetClicked()
        {
            CancelRebind();
            InputBindingStore.ResetAll();
            RefreshKeys();
        }

        private void SetChangeButtonsInteractable(bool interactable)
        {
            // 리바인딩 대기 중 아래 버튼을 좌클릭하면 그 클릭이 새 키로 잡히므로 함께 잠근다
            if (_resetButton != null) { _resetButton.interactable = interactable; }
            if (_closeButton != null) { _closeButton.interactable = interactable; }
            if (_replayIntroButton != null) { _replayIntroButton.interactable = interactable; }
            if (_resetHintsButton != null) { _resetHintsButton.interactable = interactable; }
            foreach (KeyRow row in _rows)
            {
                if (row.ChangeButton != null)
                {
                    row.ChangeButton.interactable = interactable;
                }
            }
        }

        /// <summary>현재 키 표시 + 중복(같은 effectivePath 두 행 이상)·고정 키 겹침 경고.</summary>
        private void RefreshKeys()
        {
            var byPath = new Dictionary<string, List<KeyRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyRow row in _rows)
            {
                if (row.Action == null || row.BindingIndex < 0)
                {
                    row.KeyText.text = Loc.Get("options.key_none");
                    continue;
                }

                string display = row.Action.GetBindingDisplayString(row.BindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
                row.KeyText.text = string.IsNullOrEmpty(display) ? Loc.Get("options.key_none") : display;
                row.KeyText.color = Color.white;
                row.LabelText.color = Color.white;

                string path = row.Action.bindings[row.BindingIndex].effectivePath;
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!byPath.TryGetValue(path, out List<KeyRow> list))
                {
                    list = new List<KeyRow>();
                    byPath[path] = list;
                }

                list.Add(row);
            }

            var warning = new StringBuilder();
            foreach (KeyValuePair<string, List<KeyRow>> pair in byPath)
            {
                int fixedIndex = Array.FindIndex(FixedKeyPaths, p => string.Equals(p, pair.Key, StringComparison.OrdinalIgnoreCase));
                bool duplicate = pair.Value.Count > 1;
                if (!duplicate && fixedIndex < 0)
                {
                    continue;
                }

                var labels = new List<string>();
                foreach (KeyRow row in pair.Value)
                {
                    row.KeyText.color = UIBuilder.WarningColor;
                    row.LabelText.color = UIBuilder.WarningColor;
                    labels.Add(Loc.Get(row.LabelKey));
                }

                if (warning.Length > 0)
                {
                    warning.Append('\n');
                }

                warning.Append(duplicate
                    ? Loc.Get("options.warn_duplicate", string.Join("·", labels))
                    : Loc.Get("options.warn_fixed", string.Join("·", labels), FixedKeyNames[fixedIndex]));
                if (duplicate && fixedIndex >= 0)
                {
                    warning.Append(Loc.Get("options.warn_fixed_also", FixedKeyNames[fixedIndex]));
                }
            }

            _warningText.text = warning.ToString();
            _warningText.color = UIBuilder.WarningColor; // 힌트 초기화 안내(하늘색) 뒤 원래 색으로
        }

        // ── 첫 플레이 안내(2026-10-16) ───────────────────────────

        private void OnReplayIntroClicked()
        {
            if (_rebind != null)
            {
                return;
            }

            // 바로 재생하지 않고 본 기록만 지운다 — 다음 '혼자 하기'·'멀티플레이' 때 인트로가 나온다(게임 중에도 누를 수 있음)
            StoryIntroController.ResetSeen();
            _warningText.text = Loc.Get("options.intro_reset_done");
            _warningText.color = UIBuilder.SectionColor;
        }

        private void OnResetHintsClicked()
        {
            FirstPlayHints.ResetAll();
            _warningText.text = Loc.Get("options.hints_reset_done");
            _warningText.color = UIBuilder.SectionColor;
        }

        // Move 컴포지트의 part(up/down/left/right) 또는 일반 액션의 Keyboard&Mouse 바인딩 인덱스.
        private static int FindBindingIndex(InputAction action, string compositePart)
        {
            if (action == null)
            {
                return -1;
            }

            var mask = InputBinding.MaskByGroup(KeyboardMouseGroup);
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite)
                {
                    continue;
                }

                if (compositePart != null)
                {
                    if (binding.isPartOfComposite && string.Equals(binding.name, compositePart, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                else if (!binding.isPartOfComposite && mask.Matches(binding))
                {
                    return i;
                }
            }

            return -1;
        }

        // ── 언어 ──────────────────────────────────────────────────

        private void OnLanguageClicked(GameLanguage language)
        {
            Loc.SetLanguage(language); // 저장 + Loc.Changed → 고정 글자(LocalizedText)·아래 갱신
            RefreshLanguageButtons();
        }

        // 언어가 바뀌면(이 창 또는 다른 경로) 코드가 쓰는 글자(키 표시·경고)를 다시 쓴다. 창이 닫혀 있으면 열 때 갱신된다.
        private void OnLanguageChanged()
        {
            if (!IsOpen)
            {
                return;
            }

            if (_rebind == null)
            {
                RefreshKeys();
            }

            RefreshLanguageButtons();
        }

        /// <summary>지금 언어 버튼은 '선택' 그림(호버 그림)·하늘색 글자, 나머지는 기본 그림.</summary>
        private void RefreshLanguageButtons()
        {
            MarkLanguageButton(_koreanButton, Loc.Current == GameLanguage.Korean);
            MarkLanguageButton(_englishButton, Loc.Current == GameLanguage.English);
        }

        private static void MarkLanguageButton(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            var image = button.targetGraphic as Image;
            if (image != null)
            {
                if (UIBuilder.Skin != null && UIBuilder.Skin.button != null)
                {
                    image.sprite = selected && UIBuilder.Skin.buttonHover != null ? UIBuilder.Skin.buttonHover : UIBuilder.Skin.button;
                }
                else
                {
                    image.color = selected ? new Color(0.32f, 0.42f, 0.6f, 0.95f) : UIBuilder.ButtonColor;
                }
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? UIBuilder.SectionColor : Color.white;
            }
        }

        // ── UI 만들기 ─────────────────────────────────────────────

        private void Build()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "OptionsMenuCanvas", CanvasSortOrder);
            _root = UIBuilder.AddFullPanel(canvas.transform, "OptionsPanel", UIBuilder.PanelColor);
            // 2026-10-07 화면 설정 추가: 세로 한 줄(620×700)이 꽉 차서 두 칸으로 — 왼쪽 언어·화면·볼륨, 오른쪽 조작 키. 1180×640(기준 1280×720 안).
            Transform panel = UIBuilder.AddColumn(_root.transform, "OptionsColumn", new Vector2(1180f, 640f), UIBuilder.BoxColor);
            panel.GetComponent<VerticalLayoutGroup>().spacing = 6f;

            UIBuilder.AddTextKey(panel, "options.title", 26, Color.white, 32f);

            Transform columns = UIBuilder.AddRow(panel, "Columns", 480f, 28f);
            columns.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Transform column = AddSubColumn(columns, "Left", 540f);
            Transform keyColumn = AddSubColumn(columns, "Right", 540f);

            AddLanguageRow(column);

            UIBuilder.AddTextKey(column, "options.display", 17, UIBuilder.SectionColor, 26f);
            AddDisplayRows(column);

            UIBuilder.AddTextKey(column, "options.volume", 17, UIBuilder.SectionColor, 26f);
            _masterSlider = AddVolumeRow(column, "options.volume_master", OnMasterChanged, out _masterValue);
            _bgmSlider = AddVolumeRow(column, "options.volume_bgm", OnBgmChanged, out _bgmValue);
            _sfxSlider = AddVolumeRow(column, "options.volume_sfx", OnSfxChanged, out _sfxValue);

            column = keyColumn;
            UIBuilder.AddTextKey(column, "options.keys", 17, UIBuilder.SectionColor, 26f);
            InputActionMap gameplay = InputBindingStore.Shared.Gameplay.Get();
            AddKeyRow(column, "options.key_move_up", gameplay.FindAction("Move"), "up");
            AddKeyRow(column, "options.key_move_down", gameplay.FindAction("Move"), "down");
            AddKeyRow(column, "options.key_move_left", gameplay.FindAction("Move"), "left");
            AddKeyRow(column, "options.key_move_right", gameplay.FindAction("Move"), "right");
            AddKeyRow(column, "options.key_attack", gameplay.FindAction("Attack"), null);
            AddKeyRow(column, "options.key_guard", gameplay.FindAction("Guard"), null);
            AddKeyRow(column, "options.key_dash", gameplay.FindAction("Dash"), null);
            AddKeyRow(column, "options.key_absorb", gameplay.FindAction("Absorb"), null);
            AddKeyRow(column, "options.key_revive", gameplay.FindAction("Revive"), null); // 쓰러진 동료 부활(2026-10-07)

            AddFixedRow(column, "options.key_map", "Tab");
            AddFixedRow(column, "options.key_pause", "ESC");
            AddFixedRow(column, "options.key_debug", "F1");

            _warningText = UIBuilder.AddText(panel, "", 14, UIBuilder.WarningColor, 22f);

            Transform bottom = UIBuilder.AddRow(panel, "Bottom", 42f);
            bottom.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            _resetButton = UIBuilder.AddButtonKey(bottom, "options.reset", OnResetClicked, 42f);
            _replayIntroButton = UIBuilder.AddButtonKey(bottom, "options.replay_intro", OnReplayIntroClicked, 42f);
            _resetHintsButton = UIBuilder.AddButtonKey(bottom, "options.reset_hints", OnResetHintsClicked, 42f);
            _closeButton = UIBuilder.AddButtonKey(bottom, "options.close", CloseInternal, 42f);
        }

        private static Transform AddSubColumn(Transform parent, string name, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredWidth = width;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go.transform;
        }

        // ── 화면 설정(2026-10-07) ────────────────────────────────
        private Button _windowedButton, _fullscreenButton; // 2026-10-13: 독점 전체 화면 제거로 버튼 3 → 2(QA #47)
        private Button _resPrevButton, _resNextButton;
        private Text _resolutionText;

        private void AddDisplayRows(Transform parent)
        {
#if UNITY_WEBGL
            // 브라우저판: 해상도·창 모드 없음(캔버스 크기는 WebGL 템플릿이 정함). '전체 화면' 토글 버튼 하나 —
            // 버튼 클릭이 사용자 제스처라 브라우저가 전체 화면 요청을 허용한다. _resolutionText가 null이라 RefreshDisplayRows는 바로 돌아간다.
            Transform fullRow = UIBuilder.AddRow(parent, "DisplayMode", KeyRowHeight, 6f);
            Text fullLabel = UIBuilder.AddTextKey(fullRow, "options.display_mode", 16, Color.white, KeyRowHeight, 130f);
            fullLabel.alignment = TextAnchor.MiddleLeft;
            _fullscreenButton = UIBuilder.AddButtonKey(fullRow, "options.mode_fullscreen", DisplaySettingsStore.ToggleFullscreen, KeyRowHeight, 390f);
            Text fullText = _fullscreenButton.GetComponentInChildren<Text>();
            fullText.fontSize = 14;
            UIBuilder.FitButtonLabel(fullText);
#else
            Transform modeRow = UIBuilder.AddRow(parent, "DisplayMode", KeyRowHeight, 6f);
            Text modeLabel = UIBuilder.AddTextKey(modeRow, "options.display_mode", 16, Color.white, KeyRowHeight, 130f);
            modeLabel.alignment = TextAnchor.MiddleLeft;
            // 버튼 2개가 예전 3개(128 × 3 + 간격 6 × 2)와 같은 줄 폭을 채우도록 195씩
            _windowedButton = UIBuilder.AddButtonKey(modeRow, "options.mode_windowed", () => DisplaySettingsStore.SetMode(DisplayMode.Windowed), KeyRowHeight, 195f);
            _fullscreenButton = UIBuilder.AddButtonKey(modeRow, "options.mode_fullscreen", () => DisplaySettingsStore.SetMode(DisplayMode.Fullscreen), KeyRowHeight, 195f);
            foreach (Button b in new[] { _windowedButton, _fullscreenButton })
            {
                Text t = b.GetComponentInChildren<Text>();
                t.fontSize = 14;
                UIBuilder.FitButtonLabel(t);
            }

            Transform resRow = UIBuilder.AddRow(parent, "Resolution", KeyRowHeight, 6f);
            Text resLabel = UIBuilder.AddTextKey(resRow, "options.resolution", 16, Color.white, KeyRowHeight, 130f);
            resLabel.alignment = TextAnchor.MiddleLeft;
            _resPrevButton = UIBuilder.AddButton(resRow, "<", () => DisplaySettingsStore.StepResolution(-1), KeyRowHeight, 48f);
            _resolutionText = UIBuilder.AddText(resRow, "", 16, Color.white, KeyRowHeight, 284f);
            _resNextButton = UIBuilder.AddButton(resRow, ">", () => DisplaySettingsStore.StepResolution(1), KeyRowHeight, 48f);
            DisplaySettingsStore.Changed -= RefreshDisplayRows;
            DisplaySettingsStore.Changed += RefreshDisplayRows;
            RefreshDisplayRows();
#endif
        }

        private void RefreshDisplayRows()
        {
            if (_resolutionText == null) // 브라우저판(WebGL)은 해상도 줄을 만들지 않는다 — 창 모드·해상도 버튼도 null
            {
                return;
            }

            MarkLanguageButton(_windowedButton, DisplaySettingsStore.Mode == DisplayMode.Windowed);
            MarkLanguageButton(_fullscreenButton, DisplaySettingsStore.Mode == DisplayMode.Fullscreen);

            // 창 모드가 아니면 모니터 해상도 고정 — 화살표를 끄고 "모니터" 표시
            Vector2Int r = DisplaySettingsStore.EffectiveResolution;
            bool fixedRes = DisplaySettingsStore.Mode != DisplayMode.Windowed; // 전체 화면은 모니터 해상도 고정
            _resolutionText.text = fixedRes ? Loc.Get("options.resolution_native", r.x, r.y) : r.x + " × " + r.y;
            _resPrevButton.interactable = !fixedRes;
            _resNextButton.interactable = !fixedRes;
        }

        // 언어 줄: 라벨은 두 언어를 함께 적어(언어를 잘못 골라도 찾아 되돌릴 수 있게) 번역하지 않는다. 버튼 글자도 각 언어 자기 이름 고정.
        private void AddLanguageRow(Transform parent)
        {
            Transform row = UIBuilder.AddRow(parent, "Language", KeyRowHeight);
            // 표에도 두 칸 모두 같은 글자("언어 / Language", "한국어", "English")로 둔다 — 키로 읽는 건 한글 리터럴을 코드에 남기지 않기 위해서.
            Text labelText = UIBuilder.AddText(row, Loc.Get("options.language"), 16, Color.white, KeyRowHeight, 200f);
            labelText.alignment = TextAnchor.MiddleLeft;
            _koreanButton = UIBuilder.AddButton(row, Loc.Get("options.language_ko"), () => OnLanguageClicked(GameLanguage.Korean), KeyRowHeight, 170f);
            _englishButton = UIBuilder.AddButton(row, Loc.Get("options.language_en"), () => OnLanguageClicked(GameLanguage.English), KeyRowHeight, 170f);
            foreach (Button button in new[] { _koreanButton, _englishButton })
            {
                Text text = button.GetComponentInChildren<Text>();
                text.fontSize = 16;
                UIBuilder.FitButtonLabel(text);
            }
        }

        private static Slider AddVolumeRow(Transform parent, string labelKey, UnityEngine.Events.UnityAction<float> onChanged, out Text valueText)
        {
            Transform row = UIBuilder.AddRow(parent, "Volume_" + labelKey, 30f);
            Text labelText = UIBuilder.AddTextKey(row, labelKey, 16, Color.white, 30f, 140f);
            labelText.alignment = TextAnchor.MiddleLeft;
            Slider slider = UIBuilder.AddSlider(row, 300f, 20f); // 2026-10-08: 340×24 → 300×20(손잡이 겹침 수정과 함께 작게)
            valueText = UIBuilder.AddText(row, "100", 16, Color.white, 30f, 60f);
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private void AddKeyRow(Transform parent, string labelKey, InputAction action, string compositePart)
        {
            Transform row = UIBuilder.AddRow(parent, "Key_" + labelKey, KeyRowHeight);
            var keyRow = new KeyRow
            {
                LabelKey = labelKey,
                Action = action,
                BindingIndex = FindBindingIndex(action, compositePart),
            };
            keyRow.LabelText = UIBuilder.AddTextKey(row, labelKey, 16, Color.white, KeyRowHeight, 200f);
            keyRow.LabelText.alignment = TextAnchor.MiddleLeft;
            // 키 표시 칸: 도트 파인 칸(키트 없으면 투명 = 예전처럼 글자만). 리바인딩 중 글자 색 표시는 KeyText 그대로.
            var keyBox = new GameObject("KeyBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            keyBox.transform.SetParent(row, false);
            var keyBoxElement = keyBox.GetComponent<LayoutElement>();
            keyBoxElement.preferredHeight = KeyRowHeight;
            keyBoxElement.preferredWidth = 250f;
            var keyBoxImage = keyBox.GetComponent<Image>();
            keyBoxImage.color = Color.clear;
            keyBoxImage.raycastTarget = false;
            UIBuilder.SkinBox(keyBoxImage);
            keyRow.KeyText = UIBuilder.AddText(keyBox.transform, "", 16, Color.white, KeyRowHeight);
            UIBuilder.Stretch(keyRow.KeyText.rectTransform);
            keyRow.ChangeButton = UIBuilder.AddButtonKey(row, "options.change", () => StartRebind(keyRow), KeyRowHeight, 90f);
            Text changeText = keyRow.ChangeButton.GetComponentInChildren<Text>();
            changeText.fontSize = 15;
            UIBuilder.FitButtonLabel(changeText);
            if (keyRow.BindingIndex < 0)
            {
                keyRow.ChangeButton.interactable = false;
                Debug.LogWarning($"[OptionsMenu] binding not found: {labelKey}");
            }

            _rows.Add(keyRow);
        }

        private const float KeyRowHeight = 32f; // 도트 버튼 테두리(7px × 2단위 × 위아래 = 28)보다 조금 크게

        private static void AddFixedRow(Transform parent, string labelKey, string key)
        {
            Transform row = UIBuilder.AddRow(parent, "Fixed_" + labelKey, 22f);
            Text labelText = UIBuilder.AddTextKey(row, labelKey, 15, UIBuilder.SubTextColor, 22f, 200f);
            labelText.alignment = TextAnchor.MiddleLeft;
            UIBuilder.AddText(row, key, 15, UIBuilder.SubTextColor, 22f, 250f);
            UIBuilder.AddTextKey(row, "options.fixed", 13, UIBuilder.SubTextColor, 22f, 90f);
        }
    }
}
