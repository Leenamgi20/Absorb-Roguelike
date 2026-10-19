using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Monster;
using AbsorbRoguelike.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 보스 시체 흡수 후 속성 선택 UI(uGUI). 로컬 플레이어가 스폰/해제될 때
    /// <see cref="RegisterLocalPlayer"/>/<see cref="UnregisterLocalPlayer"/>로 자기 자신의
    /// AbsorptionController를 등록받는다(호출부: AbsorptionController.OnStartLocalPlayer/OnStopLocalPlayer,
    /// 씬에 플레이어가 나중에 스폰되는 타이밍을 고려한 구조).
    ///
    /// 매 프레임 AbsorptionController.PendingBossChoiceNetId(동기화 값, 2인 검증 준비)를 폴링한다. 0→값 있음으로
    /// 바뀌면 패널을 열고(선택지는 PendingOfferedElements), 다시 0이 되면(버튼 클릭 등 다른 경로로 선택이 끝나도)
    /// 패널을 닫는다. 버튼은 AbsorptionController.ResolveBossElementChoice/DeclineBossElementChoice를 부르고,
    /// 원격 클라이언트는 그 안에서 Command로 서버에 전달된다.
    ///
    /// 씬에는 단 하나만 배치한다(항상 로컬 플레이어 1명 기준이므로 정적 참조로 등록받는다).
    /// </summary>
    public class BossChoiceUIController : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] private GameObject panelRoot;

        /// <summary>제목/설명 겸용 텍스트 1개(T4 범위 — 별도 "현재 속성" 텍스트를 추가하지 않고, 여기에
        /// 현재 속성 표시까지 같이 넣는다).</summary>
        [SerializeField] private Text descriptionText;

        [Header("속성 버튼 0")]
        [SerializeField] private Button option0Button;
        [SerializeField] private Text option0Label;

        [Header("속성 버튼 1")]
        [SerializeField] private Button option1Button;
        [SerializeField] private Text option1Label;

        [Header("유지 버튼")]
        [SerializeField] private Button keepButton;

        private static BossChoiceUIController _instance;

        /// <summary>보스 속성 선택 창이 떠 있는지(일시정지 메뉴가 이때 ESC로 열리지 않게 — 2026-09-30).</summary>
        public static bool IsOpen => _instance != null && _instance.panelRoot != null && _instance.panelRoot.activeSelf;

        private AbsorptionController _localAbsorptionController;
        private MainAbilityController _localMainAbilityController;
        private uint _lastPending; // 마지막으로 본 대기 보스 netId(0 = 없음)

        private void Awake()
        {
            _instance = this;
            UIBuilder.ApplyFont(transform);

            if (option0Button != null)
            {
                option0Button.onClick.AddListener(() => OnOptionClicked(0));
            }

            if (option1Button != null)
            {
                option1Button.onClick.AddListener(() => OnOptionClicked(1));
            }

            if (keepButton != null)
            {
                keepButton.onClick.AddListener(OnKeepClicked);
                UIBuilder.Localize(keepButton, "boss.keep"); // 프리팹 직렬화 글자('유지') 대신 언어 표
            }

            Loc.Changed += OnLanguageChanged;

            ApplySkin();
            SetPanelActive(false);
        }

        // 2026-10-06 도트 UI 키트: 선택 창 배경 = 패널, 버튼 3개 = 도트 버튼(호버·눌림은 그림 교체). 프리팹은 그대로 두고 런타임에 입힌다.
        // 패널 금 테(10px × 2단위 = 20)에 설명 글자·유지 버튼이 닿지 않게 창을 조금 키우고 글자 여백을 넓힌다. 키트가 없으면 아무것도 안 한다.
        private void ApplySkin()
        {
            if (UIBuilder.Skin == null)
            {
                return;
            }

            Transform panelTransform = descriptionText != null ? descriptionText.transform.parent
                : option0Button != null ? option0Button.transform.parent : null;
            if (panelTransform is RectTransform panelRect && panelTransform.TryGetComponent(out Image panelImage))
            {
                UIBuilder.SkinPanel(panelImage);
                panelRect.sizeDelta = new Vector2(Mathf.Max(panelRect.sizeDelta.x, 640f), Mathf.Max(panelRect.sizeDelta.y, 430f));
            }

            if (descriptionText != null)
            {
                RectOffset padding = UIBuilder.PanelPadding;
                var textRect = descriptionText.rectTransform;
                textRect.sizeDelta = new Vector2(-(padding.left + padding.right), textRect.sizeDelta.y);
                textRect.anchoredPosition = new Vector2(0f, -padding.top);
            }

            UIBuilder.SkinButton(option0Button);
            UIBuilder.SkinButton(option1Button);
            UIBuilder.SkinButton(keepButton);
        }

        private void OnDestroy()
        {
            Loc.Changed -= OnLanguageChanged;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>로컬 플레이어 스폰 시(AbsorptionController.OnStartLocalPlayer) 자기 자신을 등록한다.</summary>
        public static void RegisterLocalPlayer(AbsorptionController absorptionController)
        {
            if (_instance == null || absorptionController == null)
            {
                return; // 씬에 UI가 아직 배치되지 않은 경우(테스트 씬 등) 조용히 무시
            }

            _instance._localAbsorptionController = absorptionController;
            _instance._localMainAbilityController = absorptionController.GetComponent<MainAbilityController>();
            _instance._lastPending = 0;
            _instance.SetPanelActive(false);
        }

        /// <summary>로컬 플레이어 해제 시(AbsorptionController.OnStopLocalPlayer) 등록을 해제한다.</summary>
        public static void UnregisterLocalPlayer(AbsorptionController absorptionController)
        {
            if (_instance == null || _instance._localAbsorptionController != absorptionController)
            {
                return;
            }

            _instance._localAbsorptionController = null;
            _instance._localMainAbilityController = null;
            _instance._lastPending = 0;
            _instance.SetPanelActive(false);
        }

        private void Update()
        {
            if (_localAbsorptionController == null)
            {
                return;
            }

            uint pending = _localAbsorptionController.PendingBossChoiceNetId;
            if (pending == _lastPending)
            {
                return;
            }

            _lastPending = pending;

            if (pending != 0)
            {
                OpenPanel();
            }
            else
            {
                SetPanelActive(false);
            }
        }

        private void OpenPanel()
        {
            // 선택지는 흡수한 플레이어별로 정해진다(BossStub.PrepareOfferFor, 서버). 0번 = 이 층 속성(①), 1번 = 무작위(②).
            // 원격 소유 클라이언트도 볼 수 있게 AbsorptionController의 동기화 값으로 읽는다(2인 검증 준비).
            ElementType[] offered = _localAbsorptionController.PendingOfferedElements;

            bool hasOption0 = offered != null && offered.Length > 0;
            bool hasOption1 = offered != null && offered.Length > 1;

            if (option0Button != null)
            {
                option0Button.gameObject.SetActive(hasOption0);
            }

            if (option0Label != null && hasOption0)
            {
                option0Label.text = offered[0].DisplayName();
            }

            if (option1Button != null)
            {
                option1Button.gameObject.SetActive(hasOption1);
            }

            if (option1Label != null && hasOption1)
            {
                option1Label.text = offered[1].DisplayName();
            }

            if (descriptionText != null)
            {
                ElementType current = _localMainAbilityController != null
                    ? _localMainAbilityController.CurrentElement
                    : ElementType.None;
                string stageNote = hasOption0 ? "\n" + Loc.Get("boss.choice_floor_note", offered[0].DisplayName()) : "";
                descriptionText.text = Loc.Get("boss.choice_description", current.DisplayName(), stageNote);
            }

            SetPanelActive(true);
        }

        // 버튼 클릭 즉시 반응하도록 다음 프레임 폴링을 기다리지 않고 바로 패널을 닫는다(T5).
        // _lastPending은 지금 대기 id로 둔다 — 원격 클라이언트는 서버가 대기를 풀 때까지(SyncVar 도착) 같은 id가 남아 있어서,
        // null로 비우면 다음 프레임에 패널이 다시 열린다. 서버가 0으로 풀면 그때 변화로 감지돼 닫힘 상태가 유지된다.
        private void OnOptionClicked(int offeredElementIndex)
        {
            _localAbsorptionController?.ResolveBossElementChoice(offeredElementIndex);
            _lastPending = _localAbsorptionController != null ? _localAbsorptionController.PendingBossChoiceNetId : 0;
            SetPanelActive(false);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiSelect); // 속성 선택 확정(로컬, 2026-10-14)
        }

        private void OnKeepClicked()
        {
            _localAbsorptionController?.DeclineBossElementChoice();
            _lastPending = _localAbsorptionController != null ? _localAbsorptionController.PendingBossChoiceNetId : 0;
            SetPanelActive(false);
        }

        // 승리는 2026-10-07부터 마지막 층 보스를 흡수하지 않고 금빛 포탈로 나갈 때(FloorProgression.RpcRunVictory) — 여기서는 띄우지 않는다.

        // 선택 창이 떠 있는 동안 언어를 바꾸면(일시정지 → 옵션) 글자를 새 언어로 다시 쓴다.
        private void OnLanguageChanged()
        {
            if (panelRoot != null && panelRoot.activeSelf && _localAbsorptionController != null)
            {
                OpenPanel();
            }
        }

        private void SetPanelActive(bool active)
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(active);
            }
        }
    }
}
