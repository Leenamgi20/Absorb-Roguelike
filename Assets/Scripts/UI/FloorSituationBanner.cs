using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 층 시작 배너(GDD ⑮A·⑯A 상황 요구 1차): 1층 시작·다음 층 도착 때 화면 상단 중앙에
    /// "층 N — 이번 층 보스: 갑옷 — 소의 돌진이 갑옷을 깬다"를 displayDuration초 보여 주고 페이드로 숨긴다.
    /// FloorProgression.BossSituationAnnounced(currentBossSituation SyncVar 훅)를 구독하므로 호스트·원격 클라이언트
    /// 모두 각자 로컬로 표시한다. 디버그 메뉴로 특성을 바꿔도 다시 뜬다. 특성이 없음(None)이면 표시하지 않는다
    /// (bossSituationsEnabled를 끈 경우 — 값이 계속 None이라 훅도 불리지 않는다).
    /// 씬 배치 없이 코드로 만든다: 게임 시작 시 DontDestroyOnLoad 오브젝트 하나 + 전용 Canvas(Screen Space Overlay) +
    /// Text(LegacyRuntime 폰트, 새 아트 없음).
    /// </summary>
    public class FloorSituationBanner : MonoBehaviour
    {
        private const float DisplayDuration = 3.5f; // [PLAYTEST] 표시 시간(초)
        private const float FadeDuration = 0.6f;    // [PLAYTEST] 표시 뒤 페이드 아웃 시간(초)
        private const int FontSize = 26;
        private const int SortingOrder = 50;         // HUD(0)보다 위

        private CanvasGroup _group;
        private Text _text;
        private float _elapsed = float.MaxValue;
        private string _pending;

        // 2026-10-07: 같이하기 방을 만들면 대기 화면(방 코드·게임으로·방 닫기)이 떠 있는 동안 호스트 레벨이 먼저 생성된다.
        // 그동안은 보류했다가 대기 화면이 닫힐 때(게임으로·참가자 입장) 보여 주고, 방을 닫아 접속이 끊기면 버린다.
        private static bool StartMenuOpen
        {
            get
            {
                var menu = AbsorbRoguelike.Network.StartMenuController.Instance;
                return menu != null && menu.IsMenuVisible;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("FloorSituationBanner");
            DontDestroyOnLoad(go);
            go.AddComponent<FloorSituationBanner>();
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
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(900f, 64f); // 2026-10-06: 도트 패널 금 테(위아래 20씩) 안에 글자 한 줄이 들어가게 48 → 64
            var background = panel.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.6f);
            background.raycastTarget = false;
            UIBuilder.SkinPanel(background, plain: true); // 키트 없으면 예전 반투명 검정

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(panel.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(26f, 0f); // 금 테(20) 안쪽
            textRect.offsetMax = new Vector2(-26f, 0f);
            _text = textGo.AddComponent<Text>();
            _text.font = DamagePopup.BuiltinFont;
            _text.fontSize = FontSize;
            _text.color = new Color(1f, 0.9f, 0.55f);
            _text.alignment = TextAnchor.MiddleCenter;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;

            FloorProgression.BossSituationAnnounced += OnAnnounced;
        }

        private void OnDestroy()
        {
            FloorProgression.BossSituationAnnounced -= OnAnnounced;
        }

        private void OnAnnounced(BossSituation situation, int floor)
        {
            if (situation == BossSituation.None)
            {
                return;
            }

            string message = Loc.Get("banner.boss_situation", floor, situation.DisplayName(), situation.HintText());
            if (StartMenuOpen)
            {
                _pending = message;
                return;
            }

            Show(message);
        }

        private void Show(string message)
        {
            _pending = null;
            _text.text = message;
            _elapsed = 0f;
            _group.alpha = 1f;
        }

        private void Update()
        {
            if (_pending != null)
            {
                if (!Mirror.NetworkClient.active)
                {
                    _pending = null;
                }
                else if (!StartMenuOpen)
                {
                    Show(_pending);
                }
            }

            if (_elapsed > DisplayDuration + FadeDuration)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01((_elapsed - DisplayDuration) / FadeDuration);
            _group.alpha = 1f - fade;
        }
    }
}
