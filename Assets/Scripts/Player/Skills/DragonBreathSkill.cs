using System.Collections;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 용 제자리 브레스(단계 5c, 옛 PlayerCombat의 용 분기 — 결정 ㉘-1 가, 2026-09-29 속사형 개정): 공격 버튼을 누르고 있는 동안
    /// dragonBreathTickInterval마다 약한 부채꼴 틱(HitKind.DragonTick). 넉백 없음, 속성은 기본 공격과 같은 빈도(한 주기에 1번). 대검 휘두르기 연출 없음.
    /// 이동 입력 중에는 공격이 막히고(LocksAttackWhileMoving), 누른 채 멈추면 다시 뿜는다(ResumesAttackWhenStopped). 재사용 대기 없이 틱 간격만으로
    /// 이어 뿜고(SkipsAttackRecovery), 쏘는 동안에도 무기가 마우스를 따라 돈다(WeaponFollowsFacingWhileAttacking).
    /// 서버 판정은 FindServerHitOwner 경로(판정 시점 스킬 재확인 없음 — 옛 ServerPerformHit DragonTick case도 스킬을 다시 보지 않았다, 5a 호랑이와 같은 이유).
    /// _nextDragonElementTime(서버)은 미리 만든 객체 필드라 옛 PlayerCombat 필드처럼 스킬 교체 뒤에도 유지된다.
    /// </summary>
    internal sealed class DragonBreathSkill : AbsorbSkillBase
    {
        private readonly DragonSkillConfig _cfg;
        private float _nextDragonElementTime; // 용 브레스 속성 적용 가능 시각(서버) — 기본 공격 한 주기에 1회

        public DragonBreathSkill(AbsorbSkillContext ctx, DragonSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.DragonBreath)
        {
            _cfg = cfg;
        }

        public override bool LocksAttackWhileMoving => true;
        public override bool ResumesAttackWhenStopped => true;
        public override bool SkipsAttackRecovery => true;
        public override bool WeaponFollowsFacingWhileAttacking => true;

        /// <summary>옛 PerformSingleSwing의 용 분기. 끝의 IsAttacking=false·yield break는 PlayerCombat의 TryReplaceAttack 공통 처리.</summary>
        public override bool TryReplaceAttack(Vector2 facing, out IEnumerator routine)
        {
            routine = BreathTickRoutine(facing);
            return true;
        }

        private IEnumerator BreathTickRoutine(Vector2 facing)
        {
            PlayerCombat combat = ctx.Combat;
            combat.ShowAttackDebug(facing, _cfg.dragonBreathRadius, _cfg.dragonBreathHalfAngle, _cfg.dragonBreathTickInterval, DragonBreathTint(), followFacing: true);
            combat.ShowBreathLight(); // 2단계 브레스 빛(표시 전용, 부채꼴 이펙트와 함께 사라짐)
            // 판정·속성 빈도(_nextDragonElementTime)·틱 적중 회복은 서버(ServerPerformSkillHit DragonTick). 틱 1회 = 묶음 1개(옛 '++_hitStep').
            combat.RequestHit(PlayerCombat.HitKind.DragonTick, facing, combat.NextHitStep());

            yield return new WaitForSeconds(_cfg.dragonBreathTickInterval);
        }

        /// <summary>원격 판정 요청 검증: 브레스 틱만(옛 switch — 용은 Swing 거부, DragonTick만 허용).</summary>
        public override bool AllowsHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.DragonTick;

        public override bool HandlesServerHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.DragonTick;

        /// <summary>옛 ServerPerformHit DragonTick case. 속성은 기본 공격과 같은 빈도(스윙 + 재사용 대기 한 주기에 1회, 2026-09-29 유저 피드백: 보스에서 너무 강함).</summary>
        public override bool ServerPerformSkillHit(PlayerCombat.HitKind kind, Vector2 facing, PlayerCombat.HitSession session)
        {
            PlayerCombat combat = ctx.Combat;
            int elementApps = 0;
            if (Time.time >= _nextDragonElementTime)
            {
                elementApps = 1;
                _nextDragonElementTime = Time.time + combat.AttackSwingDuration + combat.AttackRecoveryDelay;
            }

            return combat.DoConeHit(facing, _cfg.dragonBreathDamageScale, 0f, _cfg.dragonBreathRadius, _cfg.dragonBreathHalfAngle, elementApps);
        }

        /// <summary>옛 MinSessionInterval DragonTick — 틱 간격.</summary>
        public override bool TryGetMinHitInterval(PlayerCombat.HitKind kind, out float interval)
        {
            interval = _cfg.dragonBreathTickInterval;
            return kind == PlayerCombat.HitKind.DragonTick;
        }

        /// <summary>옛 ServerBroadcastHitFx DragonTick — BreathCone(반경·반각·틱 간격·브레스 색).</summary>
        public override bool TryGetFollowingHitFx(PlayerCombat.HitKind kind, out float radius, out float halfAngle, out float duration, out Color color)
        {
            radius = _cfg.dragonBreathRadius;
            halfAngle = _cfg.dragonBreathHalfAngle;
            duration = _cfg.dragonBreathTickInterval;
            color = DragonBreathTint();
            return kind == PlayerCombat.HitKind.DragonTick;
        }

        /// <summary>브레스 연출 색: 속성이 없으면 dragonBreathColor, 있으면 속성 색(StatusTintColor) + dragonBreathColor 알파(옛 PlayerCombat.DragonBreathTint).</summary>
        private Color DragonBreathTint()
        {
            var element = ctx.MainAbility != null ? ctx.MainAbility.CurrentElement : AbsorbRoguelike.Abilities.ElementType.None;
            if (element == AbsorbRoguelike.Abilities.ElementType.None)
            {
                return _cfg.dragonBreathColor;
            }

            Color c = AbsorbRoguelike.Abilities.ElementTypeExtensions.StatusTintColor(element);
            c.a = _cfg.dragonBreathColor.a;
            return c;
        }
    }
}
