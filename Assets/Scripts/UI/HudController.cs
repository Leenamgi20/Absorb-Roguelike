using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 플레이어 HUD(docs/03_아트/아트_UI_규격.md "HUD" Canvas, Sort Order 0): 체력, 재화, 현재 속성, 현재 패시브 특성,
    /// 대시 쿨타임, 이 방에서 흡수 사용 여부. 로컬 플레이어 컴포넌트를 매 프레임 읽기만 한다.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] private RectTransform healthFill;
        [SerializeField] private Text healthText;
        [SerializeField] private Text infoText;

        private GameObject _player;
        private PlayerHealth _health;
        private PlayerCurrency _currency;
        private MainAbilityController _ability;
        private AbsorptionController _absorption;
        private PlayerController _controller;

        // 2026-10-06 도트 UI 키트: 체력바 틀(HealthBack)·채움(HealthFill)에 게이지 그림을 입힌다(채움 색은 프리팹 값 유지).
        // 정보 글자(InfoText)는 배경 없이 그림자만 있어 그대로 둔다. 키트가 없으면 아무것도 바꾸지 않는다.
        private void Awake()
        {
            UIBuilder.ApplyFont(transform);
            if (healthFill == null || UIBuilder.Skin == null)
            {
                return;
            }

            var frame = healthFill.parent != null ? healthFill.parent.GetComponent<Image>() : null;
            UIBuilder.SkinBar(frame, healthFill.GetComponent<Image>());
            // 채움 위치는 프리팹 그대로(안쪽 4 = 틀 그림의 외곽선+금 테 2px × 2단위, 그 안은 어두운 홈).
        }

        private void Update()
        {
            if (NetworkClient.localPlayer == null)
            {
                return;
            }

            if (_player != NetworkClient.localPlayer.gameObject)
            {
                _player = NetworkClient.localPlayer.gameObject;
                _health = _player.GetComponent<PlayerHealth>();
                _currency = _player.GetComponent<PlayerCurrency>();
                _ability = _player.GetComponent<MainAbilityController>();
                _absorption = _player.GetComponent<AbsorptionController>();
                _controller = _player.GetComponent<PlayerController>();
            }

            float ratio = _health.MaxHealth > 0 ? (float)_health.CurrentHealth / _health.MaxHealth : 0f;
            healthFill.anchorMax = new Vector2(ratio, 1f);
            // 글자는 매 프레임 이 자리에서 Loc.Get으로 다시 쓰므로 언어를 바꾸면 다음 프레임에 바로 반영된다(따로 Changed 구독 불필요).
            healthText.text = _health.HasShield
                ? Loc.Get("hud.hp_shield", _health.CurrentHealth, _health.MaxHealth)
                : Loc.Get("hud.hp", _health.CurrentHealth, _health.MaxHealth);

            string element = _ability != null ? _ability.CurrentElement.DisplayName() : "-";
            // 특성은 동기화 값(SyncedTraitId·SyncedSkill)으로 읽는다 — CurrentTrait는 서버 전용이라 원격 본인 HUD에서는 비어 있다(2인 검증 준비).
            string trait = _absorption != null && !string.IsNullOrEmpty(_absorption.SyncedTraitId)
                ? Loc.Get("hud.trait_value", _absorption.SyncedTraitName, SkillLabel(_absorption.SyncedSkill))
                : Loc.Get("hud.trait_none");
            string dash = _controller.DashCooldownRemaining > 0f ? Loc.Get("hud.seconds", _controller.DashCooldownRemaining) : Loc.Get("hud.ready");
            if (_controller.DashCooldownRemaining <= 0f && _absorption != null
                && _absorption.SyncedSkill == AbsorbRoguelike.Absorption.AbsorbSkill.HorseDoubleDash)
            {
                dash = Loc.Get("hud.horse_dashes_left", _controller.HorseDashesLeft); // 말: 쿨다운 전까지 남은 대시 횟수
            }
            string absorb = _absorption != null && _absorption.HasAbsorbedThisRoom ? Loc.Get("hud.absorb_used") : Loc.Get("hud.absorb_ready");
            int currency = _currency != null ? _currency.RunCurrency : 0;

            string heal = _health.HealCooldownRemaining > 0f ? Loc.Get("hud.seconds", _health.HealCooldownRemaining) : Loc.Get("hud.heal_ready");

            string mouth = _absorption != null && _absorption.HasDebris ? Loc.Get("hud.mouth_debris") : Loc.Get("hud.mouth_empty");

            var dungeon = AbsorbRoguelike.Dungeon.FloorProgression.Instance;
            string floor = dungeon != null ? $"{dungeon.Floor} / {dungeon.MaxFloors}" : "-";

            // 층 속성(보스 선택지 ①) — 예: "층 2 / 5 · 화염 (동물: 소, 뱀)". 아직 없으면(None) 생략.
            if (dungeon != null && dungeon.CurrentFloorElement != ElementType.None)
            {
                floor += $" · {dungeon.CurrentFloorElement.DisplayName()}";
            }

            // 보스 상황 특성 — 예: "층 2 / 5 · 화염 · 갑옷 (동물: ...)". 없음(None)이면 생략.
            if (dungeon != null && dungeon.CurrentBossSituation != AbsorbRoguelike.Dungeon.BossSituation.None)
            {
                floor += $" · {AbsorbRoguelike.Dungeon.BossSituationExtensions.DisplayName(dungeon.CurrentBossSituation)}";
            }

            string animals = dungeon != null ? dungeon.AnimalPoolLabel : null;
            if (!string.IsNullOrEmpty(animals))
            {
                floor += Loc.Get("hud.floor_animals", animals);
            }

            // 양: 모은 털 수(동기화 값 — 원격 클라이언트도 보인다).
            string woolLine = _absorption != null && _absorption.SyncedSkill == AbsorbRoguelike.Absorption.AbsorbSkill.SheepWool
                ? "\n" + Loc.Get("hud.line_wool", _controller.SheepWool, _controller.SheepWoolMax)
                : "";

            infoText.text = Loc.Get("hud.line_floor", floor)
                + "\n" + Loc.Get("hud.line_currency", currency)
                + "\n" + Loc.Get("hud.line_element", element)
                + "\n" + Loc.Get("hud.line_trait", trait) + woolLine
                + "\n" + Loc.Get("hud.line_dash", dash)
                + "\n" + Loc.Get("hud.line_absorb", absorb)
                + "\n" + Loc.Get("hud.line_heal", heal)
                + "\n" + Loc.Get("hud.line_mouth", mouth)
                + DifficultyDebugLine();
        }

        // 디버그 메뉴 '몬스터 수치 표시'(F1)가 켜졌을 때만 — 층·인원 배율 확인용(에디터·개발 빌드, 한국어 고정 디버그 문구).
        // 원격 클라이언트는 동기화된 층 번호와 자기 화면에 보이는 플레이어 수로 계산한다.
        private static string DifficultyDebugLine()
        {
            if (!DebugMenuController.ShowMonsterStats)
            {
                return "";
            }

            return $"\n[디버그] 층 {AbsorbRoguelike.Dungeon.DifficultyScaling.CurrentFloor} · 인원 {AbsorbRoguelike.Dungeon.DifficultyScaling.PartyCount}"
                + $" · 체력 ×{AbsorbRoguelike.Dungeon.DifficultyScaling.CurrentHealthScale:0.##} · 공격 ×{AbsorbRoguelike.Dungeon.DifficultyScaling.CurrentDamageScale:0.##}";
        }

        // 스킬 설명 = 언어 표 trait.<스킬 enum 이름>.desc(특성 id 기본값과 같은 이름).
        private static string SkillLabel(AbsorbRoguelike.Absorption.AbsorbSkill skill)
        {
            return skill == AbsorbRoguelike.Absorption.AbsorbSkill.None ? Loc.Get("hud.no_skill") : Loc.Get($"trait.{skill}.desc");
        }
    }
}
