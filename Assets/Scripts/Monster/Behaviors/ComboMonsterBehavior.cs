using AbsorbRoguelike.Player;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 연속 공격 부품 ②(잡몹 패턴 1차, 2026-10-02 — 호랑이). 짧은 예고(comboWindupDuration) + 기본 직사각형 판정을 hitCount회 반복한다.
    /// 타마다 대상 쪽으로 다시 조준하고 예고가 새로 연하게 시작해 진해진다(StartWindup이 windupSerial을 올려 모든 화면이 재시작 —
    /// 별도 comboIndex SyncVar 없이 같은 값 하나로 동기화). 타 사이 comboGap초 정지. 마지막 타만 lastHitKnockbackDistance 넉백(명중 효과 ⑥).
    /// 시퀀스 동안 공격 쿨다운은 흐르지 않고, 마지막 타 뒤 기본 후딜(recovery 후퇴)로 끝난다. 수치 전부 [PLAYTEST].
    /// </summary>
    public class ComboMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 ComboMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private ComboMonsterConfig Cfg => Config<ComboMonsterConfig>();

        protected override System.Type ConfigType => typeof(ComboMonsterConfig);

        private int _hitIndex;
        private float _gapRemaining;
        private Transform _comboTarget;

        protected override float FirstWindupDuration => Cfg.comboWindupDuration;

        protected override void OnAttackStarted(Transform target)
        {
            _hitIndex = 0;
            _comboTarget = target;
        }

        protected override void OnWindupFinished(Transform target)
        {
            bool last = _hitIndex >= Cfg.hitCount - 1;
            if (target != null)
            {
                PerformComboHit(target, last);
            }

            if (last)
            {
                return; // 기본 후딜(RecoveryRemaining)로 마무리
            }

            _hitIndex++;
            _comboTarget = target;
            RecoveryRemaining = 0f;
            _gapRemaining = Cfg.comboGap;
            InAttackSequence = true;
        }

        protected override void TickAttackSequence()
        {
            Rigidbody.linearVelocity = Vector2.zero;
            _gapRemaining -= Time.deltaTime;
            if (_gapRemaining > 0f)
            {
                return;
            }

            // 대상이 사라졌으면(사망·접속 종료) 남은 연타를 끊는다.
            if (_comboTarget == null)
            {
                InAttackSequence = false;
                RecoveryRemaining = Stats.recoveryDuration;
                return;
            }

            Vector2 dir = ((Vector2)_comboTarget.position - Rigidbody.position).normalized;
            StartWindup(_comboTarget, dir, Cfg.comboWindupDuration);
        }

        private void PerformComboHit(Transform target, bool last)
        {
            if (!IsInArea(target.position, AttackDirection, Stats.attackAreaLength, Stats.attackAreaWidth, out _))
            {
                return;
            }

            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (health != null)
            {
                // 중간 타는 넉백 0(기본 onHitKnockbackDistance 대신), 마지막 타만 lastHitKnockbackDistance.
                DealDamage(health, attackDamage, last ? Cfg.lastHitKnockbackDistance : 0f);
            }
        }
    }
}
