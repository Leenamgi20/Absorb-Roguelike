using AbsorbRoguelike.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 버튼 공통 효과음(2026-10-14): 마우스를 올리면 sfx_ui_hover, 누르면 sfx_ui_click. 로컬 화면 전용(네트워크 없음).
    /// 게임의 모든 버튼이 거치는 <see cref="UIBuilder.SkinButton"/>이 <see cref="Attach"/>로 붙인다 — 버튼마다 따로 연결하지 않는다.
    /// </summary>
    public class UiButtonSound : MonoBehaviour, IPointerEnterHandler
    {
        private Button _button;

        public static void Attach(Button button)
        {
            if (button == null || button.GetComponent<UiButtonSound>() != null)
            {
                return;
            }

            var sound = button.gameObject.AddComponent<UiButtonSound>();
            sound._button = button;
            button.onClick.AddListener(() => Sfx.Play(SfxId.UiClick));
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_button != null && _button.IsInteractable())
            {
                Sfx.Play(SfxId.UiHover);
            }
        }
    }
}
