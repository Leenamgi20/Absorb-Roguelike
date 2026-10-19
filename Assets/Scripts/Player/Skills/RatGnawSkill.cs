using AbsorbRoguelike.Monster;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 쥐 갉기(단계 5c, 옛 PlayerCombat의 쥐 분기 2곳): 기본 휘두르기 그대로 + ① DoConeHit 명중 플래그 RatGnaw(갑옷 추가 깎기 — 옛
    /// 'rat ? HitFlags.RatGnaw : None'), ② 살아있는 적을 맞힐 때마다 취약 스택 +1(옛 OnEnemyHitServer 쥐 분기 — LastHitTarget/LastHitTime 기록 뒤).
    /// 취약 스택 수치(최대 스택·지속·스택당 증가)는 RatSkillConfig에 있고(2026-10-14 StatusEffectConfig에서 이동), 걸 때 함께 넘긴다.
    /// 검증은 기본(Swing만 허용 — 옛 switch에서 쥐는 Swing만 통과).
    /// </summary>
    internal sealed class RatGnawSkill : AbsorbSkillBase
    {
        private readonly RatSkillConfig _cfg;

        public RatGnawSkill(AbsorbSkillContext ctx, RatSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.RatGnaw)
        {
            _cfg = cfg;
        }

        public override void ModifyAttack(ref AttackSpec spec)
        {
            spec.HitFlags = AbsorbRoguelike.Combat.HitFlags.RatGnaw;
        }

        /// <summary>서버(호출부 PlayerCombat.OnEnemyHitServer가 isServer·null을 이미 걸렀다): 대미지를 준 뒤에 쌓으므로 이번 타격은 배율 전 값.</summary>
        public override void OnHitEnemy(GameObject target)
        {
            var receiver = target.GetComponent<StatusEffectReceiver>();
            if (receiver != null)
            {
                receiver.AddVulnerableStack(_cfg.vulnerableMaxStacks, _cfg.vulnerableDuration, _cfg.vulnerableDamagePerStack);
            }
        }
    }
}
