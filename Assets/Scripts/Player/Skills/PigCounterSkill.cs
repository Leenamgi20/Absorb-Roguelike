using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 돼지 반격(단계 5b, 옛 PlayerGuard의 돼지 분기): 가드 무적 부채꼴을 pigGuardBlockAngle로 넓히고(기획_누락_추적 #11 미확정),
    /// 가드로 막아낸 순간 쿨타임이 끝났으면 공격자 쪽으로 기본공격 150% 반격(GDD 메인 슬롯 표).
    /// 방향 = 플레이어→공격자, 반경 = max(기본 반경, 공격자 거리 + pigCounterRadiusMargin)라 막은 공격자는 반드시 판정 안에 든다(2026-09-28 버그 수정).
    /// 판정은 서버에서만. 반격 부채꼴 연출은 호스트 본인은 직접, 원격 소유자는 PlayerGuard의 [TargetRpc] TargetShowPigCounter(래퍼 ServerShowPigCounterToOwner),
    /// 다른 화면은 PlayerCombat.ServerBroadcastConeFx. 쿨타임 시각은 미리 만든 객체가 보관 — 옛 PlayerGuard 필드처럼 스킬 교체 뒤에도 유지.
    /// </summary>
    internal sealed class PigCounterSkill : AbsorbSkillBase
    {
        private readonly PigSkillConfig _cfg;
        private float _pigCounterReadyTime; // 서버 전용 — Time.time 기준(원격 플레이어 오브젝트는 Update가 안 돌아서 타이머 대신 시각을 쓴다)

        public PigCounterSkill(AbsorbSkillContext ctx, PigSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.PigCounter)
        {
            _cfg = cfg;
        }

        /// <summary>옛 PlayerGuard.CurrentGuardBlockAngle의 돼지 분기 — 서버 판정(IsAttackBlockedByGuard)·가드 부채꼴 표시 공용.</summary>
        public override void ModifyGuardBlockAngle(ref float angle)
        {
            angle = _cfg.pigGuardBlockAngle;
        }

        /// <summary>서버 전용(PlayerGuard.OnGuardBlockedServer가 isServer 검사 뒤 부른다). 옛 돼지 분기 순서 그대로.</summary>
        public override void OnGuardBlocked(Vector2 attackerPosition)
        {
            if (Time.time < _pigCounterReadyTime)
            {
                return;
            }

            // 상황 특성 '연타 휘두르기'(GDD ⑯A): 보스가 연타하는 층에서는 반격 쿨을 줄여 연타마다 반격이 들어가게 한다.
            var dungeon = AbsorbRoguelike.Dungeon.FloorProgression.Instance;
            bool flurry = dungeon != null && dungeon.CurrentBossSituation == AbsorbRoguelike.Dungeon.BossSituation.FlurrySwing;
            _pigCounterReadyTime = Time.time + (flurry ? _cfg.pigCounterCooldownFlurry : _cfg.pigCounterCooldown);
            Vector2 toAttacker = attackerPosition - (Vector2)ctx.Transform.position;
            float distance = toAttacker.magnitude;
            Vector2 direction = distance > 0.01f ? toAttacker / distance : ctx.Controller.FacingDirection.normalized;
            PlayerCombat combat = ctx.Combat;
            float radius = Mathf.Max(combat.CurrentAttackRadius, distance + _cfg.pigCounterRadiusMargin);
            float halfAngle = combat.CurrentAttackHalfAngle; // 돼지는 기본 반격 각(attackHalfAngle)
            if (ctx.Identity.isLocalPlayer)
            {
                combat.ShowAttackDebug(direction, radius, halfAngle); // 호스트 본인
            }
            else if (ctx.Identity.connectionToClient != null)
            {
                ctx.Guard.ServerShowPigCounterToOwner(direction, radius, halfAngle); // 원격 소유자 화면(TargetShowPigCounter, 2인 검증 준비)
            }

            combat.ServerBroadcastConeFx(direction, radius, halfAngle); // 다른 화면(2인 검증 1차 후속) — RpcAttackFx(Cone, …, attackDebugDuration, attackDebugColor)

            // DoConeHit가 대상별 1회·속성 명중·보스 DamageSource.AbsorbSkill(hasSkill)·대미지 팝업을 그대로 처리한다.
            bool dealtHit = combat.DoConeHit(direction, _cfg.pigCounterDamageScale, 1f, radius, halfAngle);
            if (dealtHit && ctx.Health != null)
            {
                ctx.Health.OnDealtHit();
            }
        }
    }
}
