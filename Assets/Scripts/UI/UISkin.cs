using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 도트 UI 키트(2026-10-06, 테마 "봉인 석실"). 그림 원본은 `Art_Source/ui/draw_ui_kit.py`가 찍은 `Assets/Art/UI/Kit/*.png`
    /// (PPU 50 + 9-slice 테두리 → 1280×720 기준 캔버스에서 아트 1픽셀 = UI 2단위). 에셋 하나를 `Resources/UISkin`에 두고
    /// <see cref="Current"/>로 읽는다. 없으면 null — 그때 UIBuilder는 예전 단색 상자로 그린다.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/UI Skin")]
    public class UISkin : ScriptableObject
    {
        [Header("패널")]
        public Sprite panel;
        [Tooltip("모서리 보석 없는 패널(작은 창·배너).")]
        public Sprite panelPlain;
        [Tooltip("패널 안 파인 칸(목록 칸·하위 구역).")]
        public Sprite box;
        public Sprite divider;

        [Header("버튼(스프라이트 교체)")]
        public Sprite button;
        public Sprite buttonHover;
        public Sprite buttonPressed;
        public Sprite buttonDisabled;

        [Header("슬라이더·체크박스")]
        public Sprite sliderTrack;
        public Sprite sliderFill;
        public Sprite sliderHandle;
        public Sprite toggleBox;
        public Sprite toggleCheck;

        [Header("게이지(체력바 등) — 채움은 흰색이라 코드 색으로 틴트")]
        public Sprite barFrame;
        public Sprite barFill;

        private static UISkin _current;
        private static bool _loaded;

        public static UISkin Current
        {
            get
            {
                if (!_loaded)
                {
                    _current = Resources.Load<UISkin>("UISkin");
                    _loaded = true;
                }

                return _current;
            }
        }
    }
}
