using AbsorbRoguelike.Monster;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 뱀 감기(단계 5a): 주변 원 전체(반각 180) 부채꼴, 휘두르는 동안 베기 이펙트가 한 바퀴 돈다, 맞은 잡몹 둔화.
    /// 둔화는 DoConeHit의 모든 호출(기본 스윙·돼지 반격·털 폭발·용 틱)에서 현재 스킬이 뱀일 때 걸린다 — 기존과 같음.
    /// </summary>
    internal sealed class SnakeBindSkill : AbsorbSkillBase
    {
        private readonly SnakeSkillConfig _cfg;

        public SnakeBindSkill(AbsorbSkillContext ctx, SnakeSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.SnakeBind)
        {
            _cfg = cfg;
        }

        public override void ModifyAttack(ref AttackSpec spec)
        {
            spec.Radius = _cfg.snakeAttackRadius;
            spec.HalfAngle = 180f;
            spec.DrawConeOverSlash = true;
            spec.SpinWeapon = true;
        }

        /// <summary>뱀 둔화: 생존 무관, 넉백 뒤(기존 순서 — DoConeHit가 HitResolver.Resolve 반환 직후 부른다).</summary>
        public override void ServerOnConeHitMonster(MonsterBehaviorController behavior)
        {
            behavior.ApplySlow(_cfg.snakeSlowMultiplier, _cfg.snakeSlowDuration, SlowSource.Snake);
        }
    }
}
