using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 쓰러짐·부활 안내 문구(2026-10-07, 화면 아래 가운데 한 줄). 로컬 플레이어 기준으로 매 프레임 골라 그린다:
    /// 1) 내가 쓰러졌으면 관전 안내(downed.spectating — 동료가 부활 키를 3초 누르면 부활),
    /// 2) 살아 있고 근처(부활 거리)에 쓰러진 팀원이 있으면 "[키] 길게 눌러 부활"(revive.prompt),
    /// 3) 서버가 체력 부족으로 부활을 거부했으면 잠깐 revive.need_health.
    /// 키 이름은 옵션에서 바꾼 키(InputAction.GetBindingDisplayString). 매 프레임 Loc.Get으로 다시 만들어 언어 변경도 바로 반영된다.
    /// 씬 배치 없이 코드로 만든다(FloorSituationBanner와 같은 패턴). 수치·흐름: docs/04_구현위키/쓰러짐_부활_관전.md.
    /// </summary>
    public class DownedReviveHud : MonoBehaviour
    {
        private const int SortingOrder = 12; // 팝업(10) 위, 지도(15)·런 종료(20) 아래
        private const float MessageDuration = 2f;

        private static string _messageKey;
        private static float _messageRemaining;

        private GameObject _panel;
        private Text _text;
        private string _lastText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("DownedReviveHud");
            DontDestroyOnLoad(go);
            go.AddComponent<DownedReviveHud>();
        }

        /// <summary>잠깐 띄울 안내(Loc 키). 부활 거부(체력 부족) 등.</summary>
        public static void ShowMessage(string locKey)
        {
            _messageKey = locKey;
            _messageRemaining = MessageDuration;
        }

        private void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("Panel", typeof(RectTransform));
            _panel.transform.SetParent(transform, false);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 120f); // 아래 HUD(체력 바·스킬 칸) 위
            rect.sizeDelta = new Vector2(760f, 44f);
            var background = _panel.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);
            background.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_panel.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
            _text = textGo.AddComponent<Text>();
            _text.font = DamagePopup.BuiltinFont;
            _text.fontSize = 20;
            _text.color = Color.white;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (_messageRemaining > 0f)
            {
                _messageRemaining -= Time.unscaledDeltaTime;
            }

            string text = PickText();
            bool show = !string.IsNullOrEmpty(text);
            if (_panel.activeSelf != show)
            {
                _panel.SetActive(show);
            }

            if (show && text != _lastText)
            {
                _text.text = text;
            }

            _lastText = text;
        }

        private static string PickText()
        {
            if (NetworkClient.localPlayer == null || RunEndScreenController.IsShown)
            {
                return null;
            }

            var health = NetworkClient.localPlayer.GetComponent<PlayerHealth>();
            var revive = NetworkClient.localPlayer.GetComponent<PlayerRevive>();
            if (health == null || revive == null)
            {
                return null;
            }

            if (health.IsDowned)
            {
                return Loc.Get("downed.spectating", PlayerRevive.ReviveKeyDisplay());
            }

            if (health.IsDead)
            {
                return null; // 솔로 사망 — 결과 화면이 처리
            }

            if (_messageRemaining > 0f && !string.IsNullOrEmpty(_messageKey))
            {
                return Loc.Get(_messageKey);
            }

            if (!revive.IsReviving && revive.FindDownedTargetInRange(false) != null)
            {
                return Loc.Get("revive.prompt", PlayerRevive.ReviveKeyDisplay());
            }

            return null;
        }
    }
}
