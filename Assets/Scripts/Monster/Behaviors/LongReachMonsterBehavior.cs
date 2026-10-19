using AbsorbRoguelike.Player;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 긴 직선 공격 + 후퇴 깡총(잡몹 패턴 1차, 2026-10-02 — 토끼). 사거리 reachLength(4칸)에서 멈춰 폭 reachWidth의 긴 직선 예고 →
    /// 예고가 끝난 순간 직선 안이면 명중, 끝부분(앞쪽 거리 tipStart~reachLength)이면 대미지 × tipDamageMultiplier(흡수 스킬 귀 채찍과
    /// 같은 "끝이 아프다" 정체성). 공격 직후 기본 후딜(천천히 후퇴) 대신 공격 반대 방향으로 hopDistance를 hopDuration 동안 빠르게 깡총 물러난다.
    /// 수치 전부 [PLAYTEST].
    /// </summary>
    public class LongReachMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 LongReachMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private LongReachMonsterConfig Cfg => Config<LongReachMonsterConfig>();

        protected override System.Type ConfigType => typeof(LongReachMonsterConfig);

        private float _hopRemaining;

        protected override void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            TelegraphUtil.PlaceLine(telegraph, direction, Cfg.reachLength, Cfg.reachWidth);
        }

        protected override State DecideState(Transform target)
        {
            if (target == null)
            {
                return State.Idle;
            }

            return Vector2.Distance(transform.position, target.position) <= Cfg.reachLength ? State.Attack : State.Chase;
        }

        protected override void PerformAttack(Transform target)
        {
            if (!IsInArea(target.position, AttackDirection, Cfg.reachLength, Cfg.reachWidth, out float forward))
            {
                return;
            }

            bool tip = forward >= Cfg.tipStart;
            int damage = tip ? Mathf.RoundToInt(attackDamage * Cfg.tipDamageMultiplier) : attackDamage;
            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (health != null)
            {
                DealDamage(health, damage);
            }

            Debug.Log($"[LongReachMonster] {name} attacks {target.name} for {damage}{(tip ? " (tip)" : string.Empty)}");
        }

        protected override void OnWindupFinished(Transform target)
        {
            base.OnWindupFinished(target);

            // 후딜 대신 깡총 후퇴(시퀀스 동안 쿨다운 정지 → 착지 후부터 쿨).
            RecoveryRemaining = 0f;
            _hopRemaining = Cfg.hopDuration;
            InAttackSequence = true;
        }

        protected override void TickAttackSequence()
        {
            if (_hopRemaining <= 0f || Cfg.hopDuration <= 0f)
            {
                Rigidbody.linearVelocity = Vector2.zero;
                InAttackSequence = false;
                return;
            }

            _hopRemaining -= Time.deltaTime;
            Rigidbody.linearVelocity = -AttackDirection * (Cfg.hopDistance / Cfg.hopDuration * SlowFactor);
        }
    }
}
