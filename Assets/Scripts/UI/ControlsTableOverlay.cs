using System;
using System.Collections.Generic;
using AbsorbRoguelike.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 조작법 표(2026-10-16, 첫 플레이 안내): 일시정지 메뉴 '조작법'이 연다. 8줄 — 이동·공격·가드·대시·흡수·부활·지도·메뉴.
    /// 키 글자는 옵션에서 바꾼 키(<see cref="InputBindingStore.Shared"/>)를 그대로 읽는다. '확인' 또는 ESC로 닫는다 —
    /// 닫힌 프레임(<see cref="LastClosedFrame"/>)의 ESC는 일시정지 메뉴가 다시 처리하지 않는다. Time.timeScale은 건드리지 않는다.
    /// </summary>
    public class ControlsTableOverlay : MonoBehaviour
    {
        private const int CanvasSortOrder = 47; // 일시정지 메뉴(45) 위, 옵션(50) 아래
        private const string KeyboardMouseGroup = "Keyboard&Mouse";
        private const float RowHeight = 32f;

        private static ControlsTableOverlay _instance;

        public static bool IsOpen { get; private set; }

        /// <summary>마지막으로 닫힌 프레임 — 같은 프레임의 ESC를 일시정지 메뉴가 다시 처리하지 않게.</summary>
        public static int LastClosedFrame { get; private set; } = -1;

        private GameObject _root;
        private Action _onClosed;
        private readonly List<KeyValuePair<Text, Func<string>>> _keyTexts = new List<KeyValuePair<Text, Func<string>>>();

        public static void Open(Action onClosed)
        {
            if (_instance == null)
            {
                var go = new GameObject("ControlsTableOverlay");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<ControlsTableOverlay>();
                _instance.Build();
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

        private void OnDestroy()
        {
            if (_instance == this)
            {
                IsOpen = false;
                _instance = null;
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
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
            RefreshKeys();
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiOpen);
        }

        private void CloseInternal()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            LastClosedFrame = Time.frameCount;
            _root.SetActive(false);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiOpen);
            Action callback = _onClosed;
            _onClosed = null;
            callback?.Invoke();
        }

        private void RefreshKeys()
        {
            foreach (KeyValuePair<Text, Func<string>> pair in _keyTexts)
            {
                pair.Key.text = pair.Value();
            }
        }

        /// <summary>
        /// 키보드·마우스 바인딩 표시 글자(옵션 리바인딩 반영, 홀드 같은 인터랙션 표기 없음). 컴포지트(이동)는 parts 순서대로 "W/A/S/D".
        /// FirstPlayHints도 같은 함수를 쓴다.
        /// </summary>
        public static string KeyDisplay(InputAction action, params string[] compositeParts)
        {
            if (action == null)
            {
                return Loc.Get("options.key_none");
            }

            var mask = InputBinding.MaskByGroup(KeyboardMouseGroup);
            var labels = new List<string>();
            if (compositeParts != null && compositeParts.Length > 0)
            {
                foreach (string part in compositeParts)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        InputBinding b = action.bindings[i];
                        if (b.isPartOfComposite && mask.Matches(b) && string.Equals(b.name, part, StringComparison.OrdinalIgnoreCase))
                        {
                            labels.Add(action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions));
                            break;
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding b = action.bindings[i];
                    if (!b.isComposite && !b.isPartOfComposite && mask.Matches(b))
                    {
                        labels.Add(action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions));
                        break;
                    }
                }
            }

            labels.RemoveAll(string.IsNullOrEmpty);
            return labels.Count > 0 ? string.Join("/", labels) : Loc.Get("options.key_none");
        }

        public static string MoveKeys() => KeyDisplay(InputBindingStore.Shared.Gameplay.Move, "up", "left", "down", "right");

        // ── UI 만들기 ─────────────────────────────────────────────

        private void Build()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "ControlsTableCanvas", CanvasSortOrder);
            _root = UIBuilder.AddFullPanel(canvas.transform, "ControlsPanel", new Color(0f, 0f, 0f, 0.6f)); // 뒤(일시정지 메뉴) 클릭 차단
            Transform column = UIBuilder.AddColumn(_root.transform, "ControlsColumn", new Vector2(560f, 470f), UIBuilder.BoxColor);
            column.GetComponent<VerticalLayoutGroup>().spacing = 4f;

            UIBuilder.AddTextKey(column, "controls.title", 26, Color.white, 40f);
            UIBuilder.AddDivider(column);

            PlayerControls.GameplayActions g = InputBindingStore.Shared.Gameplay;
            AddKeyRow(column, "controls.row_move", MoveKeys);
            AddKeyRow(column, "controls.row_attack", () => KeyDisplay(g.Attack));
            AddKeyRow(column, "controls.row_guard", () => KeyDisplay(g.Guard));
            AddKeyRow(column, "controls.row_dash", () => KeyDisplay(g.Dash));
            AddKeyRow(column, "controls.row_absorb", () => KeyDisplay(g.Absorb));
            AddKeyRow(column, "controls.row_revive", () => KeyDisplay(g.Revive));
            AddKeyRow(column, "controls.row_map", () => "Tab");  // 고정 키 — 옵션 창 표기(options.fixed 줄)와 같은 글자
            AddKeyRow(column, "controls.row_map_teleport", () => Loc.Get("controls.key_map_click")); // 지도에서 방 클릭 = 순간이동(FloorMapService 규칙: 클리어한 방, 전투·쓰러짐 중 불가)
            AddKeyRow(column, "controls.row_menu", () => "ESC");

            UIBuilder.AddDivider(column);
            Transform bottom = UIBuilder.AddRow(column, "Bottom", 42f);
            bottom.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            UIBuilder.AddButtonKey(bottom, "controls.ok", CloseInternal, 42f);

            _root.SetActive(false);
        }

        private void AddKeyRow(Transform parent, string labelKey, Func<string> keyText)
        {
            Transform row = UIBuilder.AddRow(parent, "Row_" + labelKey, RowHeight);
            Text label = UIBuilder.AddTextKey(row, labelKey, 16, Color.white, RowHeight, 260f);
            label.alignment = TextAnchor.MiddleLeft;
            UIBuilder.FitButtonLabel(label);

            var keyBox = new GameObject("KeyBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            keyBox.transform.SetParent(row, false);
            var element = keyBox.GetComponent<LayoutElement>();
            element.preferredHeight = RowHeight;
            element.preferredWidth = 220f;
            var boxImage = keyBox.GetComponent<Image>();
            boxImage.color = Color.clear;
            boxImage.raycastTarget = false;
            UIBuilder.SkinBox(boxImage);
            Text key = UIBuilder.AddText(keyBox.transform, "", 16, Color.white, RowHeight);
            UIBuilder.Stretch(key.rectTransform);
            UIBuilder.FitButtonLabel(key);
            _keyTexts.Add(new KeyValuePair<Text, Func<string>>(key, keyText));
        }
    }
}
