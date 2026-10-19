using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 층 전환 연출(2026-10-06): 층이 생성되면(1층 시작 포함) 화면을 검게 덮었다가 밝히고, 가운데에 "N층"과 층 속성
    /// (속성색)을 잠깐 보여 준다. 층 속성 타일셋이 바뀌는 순간을 가려 주고 지금 어떤 층인지 알려 주는 용도.
    /// LevelGenerator.LevelGenerated(호스트·원격 클라이언트 각자 생성 직후)를 구독하므로 네트워크 동기화는 따로 없다.
    /// 시간은 unscaled(1인 ESC 일시정지와 무관). 씬 배치 없이 코드로 만든다(FloorSituationBanner와 같은 패턴, 새 아트 없음).
    /// 입력은 막지 않는다(검은 화면이 짧고, 막으면 협동 중 한쪽만 멈춘 느낌이 남).
    /// </summary>
    public class FloorTransitionOverlay : MonoBehaviour
    {
        private const float BlackHold = 0.35f;   // [PLAYTEST] 검은 화면 유지(초)
        private const float BlackFade = 0.6f;    // [PLAYTEST] 검은 화면이 걷히는 시간
        private const float TextHold = 1.6f;     // [PLAYTEST] 층 이름 유지(검은 화면 시작부터)
        private const float TextFade = 0.6f;
        private const int SortingOrder = 60;     // 층 배너(50)·HUD 위, 결과 화면(44)보다는 위지만 결과 화면이 뜨는 순간엔 층 생성이 없다

        private Image _black;
        private CanvasGroup _textGroup;
        private Text _title;
        private Text _subtitle;
        private float _elapsed = float.MaxValue;
        private bool _pending;
        private int _pendingFloor;
        private ElementType _pendingElement;

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
            var go = new GameObject("FloorTransitionOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<FloorTransitionOverlay>();
        }

        private void Awake()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "FloorTransitionCanvas", SortingOrder);
            canvas.GetComponent<GraphicRaycaster>().enabled = false;

            _black = UIBuilder.AddFullPanel(canvas.transform, "Black", Color.black).GetComponent<Image>();
            _black.raycastTarget = false;

            var textRoot = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
            textRoot.transform.SetParent(canvas.transform, false);
            UIBuilder.Stretch((RectTransform)textRoot.transform);
            _textGroup = textRoot.GetComponent<CanvasGroup>();
            _textGroup.blocksRaycasts = false;

            _title = MakeText(textRoot.transform, 56, new Vector2(0f, 24f), new Color(1f, 0.95f, 0.85f));
            _subtitle = MakeText(textRoot.transform, 28, new Vector2(0f, -30f), Color.white);

            SetAlpha(0f, 0f);
            LevelGenerator.LevelGenerated += OnLevelGenerated;
        }

        private void OnDestroy()
        {
            LevelGenerator.LevelGenerated -= OnLevelGenerated;
        }

        private static Text MakeText(Transform parent, int size, Vector2 offset, Color color)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, size + 16f);
            rect.anchoredPosition = offset;
            var text = go.AddComponent<Text>();
            text.font = UIBuilder.Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private void OnLevelGenerated(int floor, ElementType element)
        {
            if (StartMenuOpen)
            {
                _pending = true;
                _pendingFloor = floor;
                _pendingElement = element;
                return;
            }

            Play(floor, element);
        }

        private void Play(int floor, ElementType element)
        {
            _pending = false;
            var manager = FloorProgression.Instance;
            int maxFloors = manager != null ? manager.MaxFloors : 0;
            _title.text = maxFloors > 0 && floor == maxFloors ? Loc.Get("floor.title_final", floor) : Loc.Get("floor.title", floor);
            if (element != ElementType.None)
            {
                _subtitle.text = Loc.Get("floor.subtitle_element", element.DisplayName());
                Color tint = element.StatusTintColor();
                _subtitle.color = Color.Lerp(tint, Color.white, 0.25f);
            }
            else
            {
                _subtitle.text = string.Empty;
            }

            _elapsed = 0f;
            SetAlpha(1f, 1f);
            if (floor > 1)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.FloorEnter); // 다음 층 이동(각 화면 로컬 생성 직후 1회, 1층 시작은 제외 — 2026-10-14)
            }
        }

        private void Update()
        {
            if (_pending)
            {
                if (!Mirror.NetworkClient.active)
                {
                    _pending = false;
                }
                else if (!StartMenuOpen)
                {
                    Play(_pendingFloor, _pendingElement);
                }
            }

            if (_elapsed > TextHold + TextFade)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float black = 1f - Mathf.Clamp01((_elapsed - BlackHold) / BlackFade);
            float text = 1f - Mathf.Clamp01((_elapsed - TextHold) / TextFade);
            SetAlpha(black, text);
        }

        private void SetAlpha(float black, float text)
        {
            _black.color = new Color(0f, 0f, 0f, black);
            _black.enabled = black > 0f;
            _textGroup.alpha = text;
        }
    }
}
