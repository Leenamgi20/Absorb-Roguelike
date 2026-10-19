using AbsorbRoguelike.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 코드로 만드는 임시 uGUI 헬퍼(2026-09-30, 일시정지·옵션 창용). 색·폰트는 시작 메뉴(StartMenuController)와 같게 맞춘다
    /// (폰트 = HUD와 같은 LegacyRuntime 내장 폰트, <see cref="DamagePopup.BuiltinFont"/>). 기존 시작 메뉴 코드는 옮기지 않았다.
    /// 2026-10-06: 도트 UI 키트(<see cref="UISkin"/>)가 있으면 패널·버튼·슬라이더를 그 그림(9-slice)으로 그린다. 다른 화면도
    /// Skin* 함수로 같은 그림을 입힌다. 키트가 없으면 예전 단색 상자.
    /// </summary>
    public static class UIBuilder
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        public static readonly Color BoxColor = new Color(0.12f, 0.13f, 0.17f, 0.98f);
        public static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.3f, 0.95f);
        public static readonly Color SectionColor = new Color(0.65f, 0.8f, 1f);
        public static readonly Color SubTextColor = new Color(0.8f, 0.8f, 0.85f);
        public static readonly Color WarningColor = new Color(1f, 0.45f, 0.4f);

        public static Font Font => DamagePopup.BuiltinFont;

        public static UISkin Skin => UISkin.Current;

        /// <summary>프리팹에 직렬화된 Text(예전 내장 폰트)를 게임 공용 도트 폰트로 바꾼다 — HUD·보스 선택·결과 화면 Awake에서 호출.</summary>
        public static void ApplyFont(Transform root)
        {
            if (root == null || Font == null)
            {
                return;
            }

            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.font == null || text.font.name == "LegacyRuntime")
                {
                    text.font = Font;
                }
            }
        }

        /// <summary>도트 패널(9-slice) 안쪽 여백 — 금 테 10px × 2단위 + 숨 쉴 틈.</summary>
        public static RectOffset PanelPadding => Skin != null ? new RectOffset(26, 26, 22, 22) : new RectOffset(16, 16, 12, 12);

        /// <summary>패널 그림 입히기(보석 모서리 기본, plain이면 보석 없음). 키트가 없으면 아무것도 안 한다.</summary>
        public static void SkinPanel(Image image, bool plain = false)
        {
            if (image == null || Skin == null)
            {
                return;
            }

            ApplySliced(image, plain ? Skin.panelPlain : Skin.panel);
        }

        /// <summary>패널 안 파인 칸(목록 칸·하위 구역).</summary>
        public static void SkinBox(Image image)
        {
            if (image == null || Skin == null)
            {
                return;
            }

            ApplySliced(image, Skin.box);
        }

        /// <summary>버튼을 도트 버튼으로: 기본·호버(=선택)·눌림·비활성 4장을 스프라이트 교체로.</summary>
        public static void SkinButton(Button button)
        {
            UiButtonSound.Attach(button); // 버튼 공통 효과음(호버·클릭, 2026-10-14) — 스킨이 없어도 붙인다
            if (button == null || Skin == null)
            {
                return;
            }

            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            ApplySliced(image, Skin.button);
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = Skin.buttonHover,
                selectedSprite = Skin.buttonHover,
                pressedSprite = Skin.buttonPressed,
                disabledSprite = Skin.buttonDisabled,
            };
        }

        /// <summary>게이지(체력바 등) 틀·채움. 채움은 흰 그림이라 기존 색(틴트)을 그대로 둔다.</summary>
        public static void SkinBar(Image frame, Image fill)
        {
            if (Skin == null)
            {
                return;
            }

            if (frame != null)
            {
                ApplySliced(frame, Skin.barFrame);
            }

            if (fill != null && Skin.barFill != null)
            {
                fill.sprite = Skin.barFill;
                if (fill.type != Image.Type.Filled)
                {
                    fill.type = Image.Type.Sliced;
                }
            }
        }

        /// <summary>금 구분선 한 줄(세로 목록 안에 넣는다).</summary>
        public static void AddDivider(Transform parent, float height = 10f)
        {
            var go = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (Skin != null && Skin.divider != null)
            {
                ApplySliced(image, Skin.divider);
            }
            else
            {
                image.color = new Color(1f, 1f, 1f, 0.15f);
            }
        }

        private static void ApplySliced(Image image, Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;
        }

        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>화면 전체를 덮는 어두운 패널(뒤 클릭 차단).</summary>
        public static GameObject AddFullPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            go.GetComponent<Image>().color = color;
            return go;
        }

        /// <summary>가운데 고정 크기 세로 목록.</summary>
        public static Transform AddColumn(Transform parent, string name, Vector2 size, Color? background = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            if (background.HasValue)
            {
                var image = go.AddComponent<Image>();
                image.color = background.Value;
                SkinPanel(image);
            }

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = background.HasValue ? PanelPadding : new RectOffset(16, 16, 12, 12);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go.transform;
        }

        public static Transform AddRow(Transform parent, string name, float height, float spacing = 8f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return go.transform;
        }

        public static Text AddText(Transform parent, string content, int size, Color color, float height, float width = -1f)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var element = go.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            if (width > 0f)
            {
                element.preferredWidth = width;
            }

            var text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        public static Button AddButton(Transform parent, string label, UnityAction onClick, float height, float width = -1f)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var element = go.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            if (width > 0f)
            {
                element.preferredWidth = width;
            }

            go.GetComponent<Image>().color = ButtonColor;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            SkinButton(button);

            Text text = AddText(go.transform, label, 18, Color.white, height);
            Stretch((RectTransform)text.transform);
            FitButtonLabel(text);
            return button;
        }

        // ── 언어 설정(2026-10-06): 키를 받는 버전. 고정 글자는 LocalizedText가 언어 변경 때 다시 쓴다. ──

        /// <summary>키의 글자를 넣고 LocalizedText를 붙여 언어가 바뀌면 따라 바뀌게 한다(고정 글자 전용).</summary>
        public static Text Localize(Text text, string key)
        {
            if (text == null)
            {
                return null;
            }

            var localized = text.GetComponent<LocalizedText>();
            if (localized == null)
            {
                localized = text.gameObject.AddComponent<LocalizedText>();
            }

            localized.SetKey(key);
            return text;
        }

        /// <summary>버튼 글자를 키로(버튼 안 Text에 LocalizedText).</summary>
        public static Button Localize(Button button, string key)
        {
            if (button != null)
            {
                Localize(button.GetComponentInChildren<Text>(true), key);
            }

            return button;
        }

        public static Text AddTextKey(Transform parent, string key, int size, Color color, float height, float width = -1f)
        {
            return Localize(AddText(parent, Loc.Get(key), size, color, height, width), key);
        }

        public static Button AddButtonKey(Transform parent, string key, UnityAction onClick, float height, float width = -1f)
        {
            Button button = AddButton(parent, Loc.Get(key), onClick, height, width);
            button.gameObject.name = key;
            return Localize(button, key);
        }

        /// <summary>
        /// 버튼 글자 넘침 정책: 한 줄이 넘치면 줄바꿈 대신 글자를 줄인다(Best Fit, 최소 10). 영어가 한국어보다 길어도
        /// 버튼 밖으로 나가지 않게 — 넘치지 않는 글자는 원래 크기 그대로라 도트 글꼴이 흐려지지 않는다.
        /// </summary>
        public static void FitButtonLabel(Text text)
        {
            if (text == null)
            {
                return;
            }

            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = text.fontSize;
            text.resizeTextMinSize = Mathf.Min(10, text.fontSize);
        }

        /// <summary>0~100 정수 슬라이더(배경·채움·손잡이 이미지 포함).</summary>
        public static Slider AddSlider(Transform parent, float width, float height)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(go.transform, false);
            var bgRect = (RectTransform)background.transform;
            bgRect.anchorMin = new Vector2(0f, 0.35f);
            bgRect.anchorMax = new Vector2(1f, 0.65f);
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0.15f, 0.16f, 0.2f, 1f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
            fillAreaRect.offsetMin = new Vector2(4f, 0f);
            fillAreaRect.offsetMax = new Vector2(-4f, 0f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = (RectTransform)fill.transform;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = SectionColor;

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            Stretch(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(16f, 0f);
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = Color.white;

            if (Skin != null)
            {
                ApplySliced(background.GetComponent<Image>(), Skin.sliderTrack);
                ApplySliced(fill.GetComponent<Image>(), Skin.sliderFill);
                if (Skin.sliderHandle != null)
                {
                    handleImage.sprite = Skin.sliderHandle;
                    handleImage.type = Image.Type.Simple;
                    // 손잡이는 슬라이더 높이로 늘어나므로(앵커 위아래 끝) sizeDelta.y는 0 — 예전 32는 높이에 더해져 아래 줄과 겹쳤다(2026-10-08).
                    // 폭은 도트 손잡이 비율(10×16)에 맞춘다.
                    handleRect.sizeDelta = new Vector2(height * 10f / 16f, 0f);
                }
            }

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            return slider;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
