using System.Collections;
using System.Collections.Generic;
using AbsorbRoguelike.Monster;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 소 돌진(단계 5a): 기본 공격이 마우스 방향 짧은 돌진으로 바뀐다(무적 없음, TryReplaceAttack). 몸에 부딪힌 적에게 대미지 + 강한 넉백,
    /// 한 번 돌진에 같은 적은 한 번만. 대검 부채꼴 판정(DoConeHit — 돼지 반격 등)의 넉백 기준도 oxKnockbackScale(ModifyAttack).
    /// </summary>
    internal sealed class OxKnockbackSkill : AbsorbSkillBase
    {
        private readonly OxSkillConfig _cfg;

        public OxKnockbackSkill(AbsorbSkillContext ctx, OxSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.OxKnockback)
        {
            _cfg = cfg;
        }

        public override void ModifyAttack(ref AttackSpec spec)
        {
            spec.KnockbackBaseScale = _cfg.oxKnockbackScale;
        }

        public override bool TryReplaceAttack(Vector2 facing, out IEnumerator routine)
        {
            routine = ChargeRoutine(facing);
            return true;
        }

        public override bool AllowsHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.OxCharge;

        public override bool HandlesServerHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.OxCharge;

        public override bool ServerPerformSkillHit(PlayerCombat.HitKind kind, Vector2 facing, PlayerCombat.HitSession session)
        {
            return ChargeHitOnce(session.HitSet);
        }

        public override float GetHitSessionDuration(PlayerCombat.HitKind kind) =>
            kind == PlayerCombat.HitKind.OxCharge ? _cfg.oxChargeDuration : 0f;

        public override bool TryGetHitFx(PlayerCombat.HitKind kind, out float radius, out float halfAngle, out float duration)
        {
            if (kind != PlayerCombat.HitKind.OxCharge)
            {
                return base.TryGetHitFx(kind, out radius, out halfAngle, out duration);
            }

            radius = _cfg.oxChargeHitRadius;
            halfAngle = 180f;
            duration = _cfg.oxChargeDuration;
            return true;
        }

        // 소 흡수 스킬: 휘두르기 대신 마우스 방향으로 짧게 돌진하며, 몸에 부딪힌 적에게 대미지 + 강한 넉백.
        // 무적은 없다(대시와 구분). 한 번 돌진에 같은 적은 한 번만 맞는다. (옛 PlayerCombat.OxChargeRoutine 그대로)
        private IEnumerator ChargeRoutine(Vector2 facing)
        {
            ctx.Controller.IsOxCharging = true;
            ctx.Guard.ForceOff();
            ctx.Combat.ShowAttackDebug(facing, _cfg.oxChargeHitRadius, 180f, _cfg.oxChargeDuration);

            // 이동은 소유 클라이언트(ClientToServer NetworkTransform)가 하고, 판정은 프레임마다 OxCharge 요청으로 서버가 한다.
            // 같은 돌진(step) 안에서 대상별 1회·적중 회복 1회는 서버 HitSession이 지킨다.
            int chargeStep = ctx.Combat.NextHitStep();
            Vector2 velocity = facing * (_cfg.oxChargeDistance / _cfg.oxChargeDuration);

            float elapsed = 0f;
            while (elapsed < _cfg.oxChargeDuration)
            {
                ctx.Rigidbody.linearVelocity = velocity;
                ctx.Combat.RequestHit(PlayerCombat.HitKind.OxCharge, facing, chargeStep);
                elapsed += Time.deltaTime;
                yield return null;
            }

            ctx.Rigidbody.linearVelocity = Vector2.zero;
            ctx.Controller.IsOxCharging = false;
        }

        /// <summary>소 돌진 판정 1회(서버 전용, 옛 PlayerCombat.OxChargeHitOnce 그대로) — 원 반경 oxChargeHitRadius, hitSet으로 돌진 1회당 대상별 1회
        /// (대미지 oxChargeDamageScale, 넉백 oxKnockbackScale, 보스 OxCharge 갑옷 플래그, 속성 대상별 1회). 살아있는 적을 실제로 깎았으면 true.</summary>
        private bool ChargeHitOnce(HashSet<GameObject> hitSet)
        {
            if (!ctx.IsServer)
            {
                return false;
            }

            PlayerCombat combat = ctx.Combat;
            GameObject self = combat.gameObject;
            Vector2 origin = combat.HitOrigin;
            bool dealtHit = false;
            int damage = combat.ComputeSkillDamage(_cfg.oxChargeDamageScale);
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(origin, _cfg.oxChargeHitRadius, combat.AttackHitLayers))
            {
                if (hit.gameObject == self || hit.isTrigger || !hitSet.Add(hit.gameObject))
                {
                    continue;
                }

                // 잡몹/보스 공통 처리(리팩터 B안 단계 1, HitResolver). 잡몹 넉백은 oxKnockbackScale 배율을 0 확인 없이 항상 호출(기존 그대로).
                // 상황 특성 '갑옷'(GDD ⑮A): 소 돌진 명중만 OxCharge로 표시해 갑옷을 크게 깎는다(BossStub.armorBreakOxCharge).
                // 속성은 hitSet으로 돌진 1회당 대상별 1회, 소 돌진은 흡수 스킬이므로 보스에게도 적용.
                var info = new AbsorbRoguelike.Combat.HitInfo(damage, origin, combat.netIdentity, DamageSource.AbsorbSkill,
                    flags: AbsorbRoguelike.Combat.HitFlags.OxCharge);
                var options = new AbsorbRoguelike.Combat.HitOptions
                {
                    ElementApplications = 1,
                    ApplyElementToBoss = true,
                    RecordHit = true,
                    Knockback = AbsorbRoguelike.Combat.KnockbackMode.ScaleAlways,
                    KnockbackValue = _cfg.oxKnockbackScale,
                };
                var result = AbsorbRoguelike.Combat.HitResolver.Resolve(hit.gameObject, info, options,
                    (t, isBoss) => combat.ApplyElementOnHit(t, damage, isBoss, true), combat.OnEnemyHitServer);
                // 체력이 실제로 깎였을 때만 — 개인 할당량 초과분은 제외(2026-09-28)
                dealtHit |= result.Damaged;
            }

            return dealtHit;
        }
    }
}
