using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 호랑이 연속 할퀴기(단계 5a): 좁고 긴 부채꼴, 한 번 휘두를 때 3연타(1타 + 스윙 1/3·2/3 지점 추가 타 2회),
    /// 타마다 tigerHitDamageScale, 마지막 타에만 넉백(GDD 메인 슬롯 표). 추가 타 묶음 검증(같은 Swing step·Followups &lt; 2)과
    /// Followups 증가는 PlayerCombat.ServerPerformHit/ValidateHitRequest에 그대로 있다.
    /// </summary>
    internal sealed class TigerComboSkill : AbsorbSkillBase
    {
        private readonly TigerSkillConfig _cfg;

        public TigerComboSkill(AbsorbSkillContext ctx, TigerSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.TigerCombo)
        {
            _cfg = cfg;
        }

        public override void ModifyAttack(ref AttackSpec spec)
        {
            spec.Radius = _cfg.tigerAttackRadius;
            spec.HalfAngle = _cfg.tigerAttackHalfAngle;
            spec.DamageScale = _cfg.tigerHitDamageScale; // 1타
            spec.KnockbackScale = 0f;                    // 1타 넉백 없음
            spec.ExtraHits = 2;
            spec.DrawConeOverSlash = true;
        }

        public override bool AllowsHit(PlayerCombat.HitKind kind) =>
            kind == PlayerCombat.HitKind.Swing || kind == PlayerCombat.HitKind.TigerFollowup;

        public override bool HandlesServerHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.TigerFollowup;

        /// <summary>추가 타 1회(서버). session.Followups는 PlayerCombat이 이미 올렸다 — 2번째(마지막) 추가 타에만 넉백.</summary>
        public override bool ServerPerformSkillHit(PlayerCombat.HitKind kind, Vector2 facing, PlayerCombat.HitSession session)
        {
            bool last = session.Followups >= 2; // 호랑이 연속 할퀴기: 마지막 타에만 넉백(GDD 메인 슬롯 표)
            return ctx.Combat.DoConeHit(facing, _cfg.tigerHitDamageScale, last ? 1f : 0f);
        }
    }
}
