using System;
using AbsorbRoguelike.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 첫 플레이 인트로(2026-10-16): 스토리보드 컷 05·06·07·09(같이 하기면 12까지)를 그림 + 자막으로 한 장씩 넘긴다.
    /// 타이틀 '혼자 하기'·'같이 하기'를 처음 누를 때(<see cref="IsSeen"/>가 false) StartMenuController가 부르고,
    /// 옵션 '인트로 다시 보기'는 본 기록만 지워(<see cref="ResetSeen"/>) 다음 시작 때 이 진입점으로 다시 나오게 한다. 로컬 화면 전용 — 네트워크 코드 없음, 시간(timeScale)도 건드리지 않는다.
    /// 그림은 Resources/Story/cut_XX(Sprite 또는 Texture2D). 없으면 어두운 배경 + 자막만. 문서: docs/04_구현위키/첫_플레이_안내.md.
    /// </summary>
    public class StoryIntroController : MonoBehaviour
    {
        public const string SeenPrefsKey = "Absorb.IntroSeen";
        private const int CanvasSortOrder = 80; // 시작 메뉴(40)·옵션(50)·층 전환(60) 위
        private static readonly string[] SoloCuts = { "05", "06", "07", "09" };
        private static readonly string[] CoopCuts = { "05", "06", "07", "09", "12" };
        private const float FadeOutSeconds = 0.18f; // 컷 전환: 그림·자막이 살짝 어두워졌다가(2026-10-16)
        private const float FadeInSeconds = 0.3f;   // 다음 컷이 떠오른다. 시간 정지와 무관하게 unscaled 시간

        private static StoryIntroController _instance;

        /// <summary>인트로가 떠 있는지(타이틀 버튼 중복 클릭 방지).</summary>
        public static bool IsPlaying => _instance != null;

        /// <summary>인트로를 본 적이 있는지. 저장값을 못 읽으면 본 것으로 친다(시작을 막지 않게).</summary>
        public static bool IsSeen
        {
            get
            {
                try
                {
                    return PlayerPrefs.GetInt(SeenPrefsKey, 0) == 1;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[StoryIntro] PlayerPrefs read failed: {e.Message}");
                    return true;
                }
            }
        }

        public static void MarkSeen()
        {
            try
            {
                PlayerPrefs.SetInt(SeenPrefsKey, 1);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StoryIntro] PlayerPrefs save failed: {e.Message}");
            }
        }

        /// <summary>본 기록을 지운다 — 다음 시작 때 인트로가 다시 나온다(옵션 '인트로 다시 보기').</summary>
        public static void ResetSeen()
        {
            try
            {
                PlayerPrefs.DeleteKey(SeenPrefsKey);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StoryIntro] PlayerPrefs delete failed: {e.Message}");
            }
        }

        /// <summary>인트로를 띄운다. 마지막 컷을 넘기거나 건너뛰면 본 기록을 남기고 onFinished를 한 번 부른다. 이미 떠 있으면 무시.</summary>
        public static void Play(bool coop, Action onFinished)
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("StoryIntro");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<StoryIntroController>();
            _instance.Begin(coop ? CoopCuts : SoloCuts, onFinished);
        }

        private string[] _cuts;
        private int _index;
        private Action _onFinished;
        private bool _finished;
        private int _startFrame;
        private Image _picture;
        private Text _subtitle;
        private RectTransform _skipRect;
        private int _pendingIndex = -1; // 페이드아웃 중이면 다음에 보여 줄 컷
        private float _fadeOutElapsed;
        private float _fadeInElapsed = FadeInSeconds;

        private void Begin(string[] cuts, Action onFinished)
        {
            _cuts = cuts;
            _onFinished = onFinished;
            _startFrame = Time.frameCount;
            Build();
            ShowCut(0);
            _fadeInElapsed = 0f; // 첫 컷도 어둠에서 떠오르게
            SetCutAlpha(0f);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiOpen);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            TickFade();

            if (_finished || Time.frameCount == _startFrame)
            {
                return; // 인트로를 연 클릭·Enter가 같은 프레임에 다음 컷으로 넘기지 않게
            }

            bool next = false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                next = true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame
                && !RectTransformUtility.RectangleContainsScreenPoint(_skipRect, mouse.position.ReadValue(), null))
            {
                next = true; // 건너뛰기 버튼 위 클릭은 버튼이 처리한다
            }

            if (next)
            {
                if (IsFading)
                {
                    CompleteFade(); // 전환 중에 누르면 전환만 바로 끝낸다(한 번에 두 장 넘어가지 않게)
                }
                else if (_index + 1 < _cuts.Length)
                {
                    _pendingIndex = _index + 1;
                    _fadeOutElapsed = 0f;
                }
                else
                {
                    Finish();
                }
            }
        }

        private bool IsFading => _pendingIndex >= 0 || _fadeInElapsed < FadeInSeconds;

        private void TickFade()
        {
            float dt = Time.unscaledDeltaTime;
            if (_pendingIndex >= 0)
            {
                _fadeOutElapsed += dt;
                if (_fadeOutElapsed < FadeOutSeconds)
                {
                    SetCutAlpha(1f - _fadeOutElapsed / FadeOutSeconds);
                    return;
                }

                ShowCut(_pendingIndex);
                _pendingIndex = -1;
                _fadeInElapsed = 0f;
                SetCutAlpha(0f);
                return;
            }

            if (_fadeInElapsed < FadeInSeconds)
            {
                _fadeInElapsed += dt;
                SetCutAlpha(Mathf.Clamp01(_fadeInElapsed / FadeInSeconds));
            }
        }

        private void CompleteFade()
        {
            if (_pendingIndex >= 0)
            {
                ShowCut(_pendingIndex);
                _pendingIndex = -1;
            }

            _fadeInElapsed = FadeInSeconds;
            SetCutAlpha(1f);
        }

        // 그림과 자막만 흐리게 — 배경·자막 띠·건너뛰기 버튼은 그대로(자막 그림자는 글자 알파를 따라간다)
        private void SetCutAlpha(float alpha)
        {
            _picture.color = new Color(1f, 1f, 1f, alpha);
            Color c = _subtitle.color;
            c.a = alpha;
            _subtitle.color = c;
        }

        private void ShowCut(int index)
        {
            _index = index;
            string cut = _cuts[index];
            Sprite sprite = LoadCut(cut);
            _picture.sprite = sprite;
            _picture.enabled = sprite != null;
            UIBuilder.Localize(_subtitle, "intro.cut" + cut);
        }

        private static Sprite LoadCut(string cut)
        {
            string path = "Story/cut_" + cut;
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            Texture2D texture = Resources.Load<Texture2D>(path);
            return texture != null
                ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f)
                : null;
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            MarkSeen();
            Action callback = _onFinished;
            _onFinished = null;
            Destroy(gameObject);
            _instance = null; // 콜백 안에서 IsPlaying이 false로 보이게
            callback?.Invoke();
        }

        // ── UI 만들기 ─────────────────────────────────────────────

        private void Build()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "StoryIntroCanvas", CanvasSortOrder);
            GameObject root = UIBuilder.AddFullPanel(canvas.transform, "IntroBackground", new Color(0.03f, 0.03f, 0.05f, 1f)); // 뒤 클릭 차단

            var pictureGo = new GameObject("Picture", typeof(RectTransform), typeof(Image));
            pictureGo.transform.SetParent(root.transform, false);
            UIBuilder.Stretch((RectTransform)pictureGo.transform);
            _picture = pictureGo.GetComponent<Image>();
            _picture.preserveAspect = true;
            _picture.raycastTarget = false;

            // 아래 자막 띠
            var band = new GameObject("SubtitleBand", typeof(RectTransform), typeof(Image));
            band.transform.SetParent(root.transform, false);
            var bandRect = (RectTransform)band.transform;
            bandRect.anchorMin = new Vector2(0f, 0f);
            bandRect.anchorMax = new Vector2(1f, 0f);
            bandRect.pivot = new Vector2(0.5f, 0f);
            bandRect.sizeDelta = new Vector2(0f, 130f);
            var bandImage = band.GetComponent<Image>();
            bandImage.color = new Color(0f, 0f, 0f, 0.88f); // 밝은 그림 위에서도 자막이 읽히게(2026-10-16 0.7 → 0.88)
            bandImage.raycastTarget = false;

            _subtitle = UIBuilder.AddText(band.transform, "", 22, Color.white, 90f);
            var subRect = _subtitle.rectTransform;
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 1f);
            subRect.offsetMin = new Vector2(80f, 34f);
            subRect.offsetMax = new Vector2(-80f, -12f);
            _subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _subtitle.verticalOverflow = VerticalWrapMode.Truncate;
            var shadow = _subtitle.gameObject.AddComponent<Shadow>(); // 글자 뒤 1px 검은 그림자
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1f, -1f);

            Text hint = UIBuilder.AddTextKey(band.transform, "intro.next_hint", 14, UIBuilder.SubTextColor, 24f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 8f);
            hintRect.sizeDelta = new Vector2(0f, 22f);

            // 오른쪽 위 '건너뛰기'
            Button skip = UIBuilder.AddButtonKey(root.transform, "intro.skip", Finish, 40f, 140f);
            _skipRect = (RectTransform)skip.transform;
            _skipRect.anchorMin = _skipRect.anchorMax = new Vector2(1f, 1f);
            _skipRect.pivot = new Vector2(1f, 1f);
            _skipRect.anchoredPosition = new Vector2(-24f, -20f);
            _skipRect.sizeDelta = new Vector2(140f, 40f);

            UIBuilder.ApplyFont(canvas.transform);
        }
    }
}
