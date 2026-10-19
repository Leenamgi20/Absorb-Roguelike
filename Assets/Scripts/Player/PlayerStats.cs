using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 플레이어 능력치 수정치 모음(리팩터 B안 단계 3, 2026-10-08). PlayerController에 흩어져 있던 패시브 특성·메인 능력 속성
    /// 수정치를 옮겨 왔다(분해 버프는 2026-10-12 사용자 결정으로 제거). 기본값(attackDamage·moveSpeed)은 각 컴포넌트의 SerializeField에 남고,
    /// 최종 합산 식(연산 순서·반올림·Max(1,…))도 PlayerCombat(공격)·PlayerMotor(이동)에 있다 — 여기는 수정치 값만 들고 있다.
    /// MonoBehaviour(NetworkBehaviour 아님)이고 SerializeField가 없다 — 값은 서버 판정에서만 쓰이고 동기화하지 않는다.
    /// 누가 값을 바꾸는지(이벤트 구독)는 모른다: PlayerController가 특성·속성 이벤트를 받아 Set*을 부른다.
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        private float _passiveMoveSpeedMultiplier = 1f;
        private int _passiveAttackDamageBonus;
        private int _mainAbilityDamageBonus;

        /// <summary>패시브 특성의 이동속도 배율(특성 없음 = 1).</summary>
        public float PassiveMoveSpeedMultiplier => _passiveMoveSpeedMultiplier;

        /// <summary>공격력 고정 가산치 합(패시브 특성 + 메인 능력 속성). 기본 공격력에 더해 쓴다.</summary>
        public int AttackDamageBonusSum => _passiveAttackDamageBonus + _mainAbilityDamageBonus;

        /// <summary>
        /// 흡수한 패시브 특성의 수치를 적용/해제한다(옛 PlayerController.ApplyPassiveTrait의 수치 부분, 식 그대로).
        /// 슬롯이 1개뿐이라 항상 덮어쓴다. 강화(상위) 특성 1.5배는 2026-10-12 제거 — 특성 값 그대로.
        /// </summary>
        public void SetPassiveTrait(AbsorbRoguelike.Absorption.PassiveTraitData trait)
        {
            if (trait == null)
            {
                _passiveMoveSpeedMultiplier = 1f;
                _passiveAttackDamageBonus = 0;
                return;
            }

            _passiveMoveSpeedMultiplier = trait.MoveSpeedMultiplier;
            _passiveAttackDamageBonus = trait.AttackDamageBonus;
        }

        /// <summary>메인 능력(무기×속성)의 현재 속성이 주는 대미지 보너스. 패시브 특성과는 별개 축이라 서로 덮어쓰지 않는다.</summary>
        public void SetMainAbilityDamageBonus(int bonus)
        {
            _mainAbilityDamageBonus = bonus;
        }
    }
}
