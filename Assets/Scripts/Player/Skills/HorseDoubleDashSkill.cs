using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 말 연속 대시(단계 5b, 옛 PlayerDash의 말 분기): 쿨다운 전에 horseDashCharges번 연달아 대시, 앞 대시가 끝난 뒤 horseSecondDashWindow 안에
    /// 다시 누르면 쿨다운 없이 이어 쓰고, 창이 지나면 그때 dashCooldown 시작. 대시 경로 대미지 horseDashDamageScale·넉백 없음.
    /// 상태(남은 횟수·이어쓰기 창)는 로컬 전용(원격 클라이언트는 동기화된 흡수 스킬로 판단) — 옛 PlayerDash 필드와 같다.
    /// 대시 쿨타임·dashCooldown·IsDashing은 PlayerDash에 두고 internal 접근자로 읽고 쓴다. 몸 통과(horsePassThroughLayers)는 모든 대시 공용(결정 ㉓A)이라 PlayerDash.
    /// </summary>
    internal sealed class HorseDoubleDashSkill : AbsorbSkillBase
    {
        private readonly HorseSkillConfig _cfg;
        private int _horseDashesLeft;
        private float _horseFollowUpWindowRemaining; // >0이면 말 연속 대시 대기 중(쿨다운 대신)

        public HorseDoubleDashSkill(AbsorbSkillContext ctx, HorseSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.HorseDoubleDash)
        {
            _cfg = cfg;
            // 옛 PlayerController.Awake의 InitHorseCharges('_horseDashesLeft = Mathf.Max(1, horseDashCharges)')와 같은 값 — Host가 Awake(EnsureInit)에서 만든다.
            _horseDashesLeft = MaxCharges;
        }

        private int MaxCharges => Mathf.Max(1, _cfg.horseDashCharges);

        /// <summary>쿨다운 전까지 남은 대시 횟수(HUD — PlayerDash.HorseDashesLeft·PlayerController.HorseDashesLeft가 위임).</summary>
        internal int DashesLeft => _horseDashesLeft;

        public override bool AllowsHit(PlayerCombat.HitKind kind) =>
            kind == PlayerCombat.HitKind.Swing || kind == PlayerCombat.HitKind.DashPath;

        public override void ModifyDash(ref PlayerDash.DashParams dash)
        {
            dash.PathDamageScale = _cfg.horseDashDamageScale;
            dash.PathKnockbackDistance = 0f; // 통과형이라 넉백 없음(통과 자체는 모든 대시 기본 — 말의 개성은 2단 + 지나간 적 대미지)
        }

        /// <summary>
        /// 옛 PlayerDash.UpdateHorseDashCharges(로컬 전용, 매 프레임). 이어쓰기 창이 지나면 dashCooldown을 시작하고, 말이 벗겨지면 횟수를 초기화한다
        /// (연속 대시 도중 벗겨졌으면 공짜 대시가 되지 않게 쿨다운도 시작).
        /// </summary>
        public override void TickLocalDash(bool equipped)
        {
            int maxCharges = MaxCharges;
            PlayerDash dash = ctx.Dash;
            if (!equipped)
            {
                if (_horseFollowUpWindowRemaining > 0f)
                {
                    dash.DashCooldownRemaining = dash.DashCooldown;
                }

                _horseFollowUpWindowRemaining = 0f;
                _horseDashesLeft = maxCharges;
                return;
            }

            if (_horseFollowUpWindowRemaining > 0f && !dash.IsDashing)
            {
                _horseFollowUpWindowRemaining -= Time.deltaTime;
                if (_horseFollowUpWindowRemaining <= 0f)
                {
                    _horseFollowUpWindowRemaining = 0f;
                    _horseDashesLeft = maxCharges;
                    dash.DashCooldownRemaining = dash.DashCooldown; // 이어쓰지 않고 창이 지나면 그때부터 쿨다운
                }
            }
        }

        /// <summary>옛 TryStartDash의 말 분기(대시 중·쿨타임 검사 뒤): 창 닫기, 횟수 -1, 남았으면 쿨다운 미루기, 마지막이면 횟수 리셋.</summary>
        public override void OnDashStart(ref PlayerDash.DashParams dash)
        {
            _horseFollowUpWindowRemaining = 0f;
            _horseDashesLeft = Mathf.Max(0, _horseDashesLeft - 1);
            dash.DeferCooldown = _horseDashesLeft > 0;
            if (!dash.DeferCooldown)
            {
                _horseDashesLeft = MaxCharges; // 마지막 대시 — 쿨다운이 끝나면 다시 전부 사용 가능
            }
        }

        /// <summary>옛 DashRoutine 끝의 '말: 이 안에 다시 누르면 쿨다운 없이 다음 대시'. DeferCooldown은 말만 켜므로 장착 여부와 무관하게
        /// 대시 시작 때 값으로 판단한다(대시 도중 스킬이 바뀌어도 옛 코드처럼 창이 열리고, 다음 프레임 TickLocalDash(false)가 쿨다운을 시작).</summary>
        public override void OnDashEnd(PlayerDash.DashParams dash)
        {
            if (dash.DeferCooldown)
            {
                _horseFollowUpWindowRemaining = _cfg.horseSecondDashWindow;
            }
        }
    }
}
