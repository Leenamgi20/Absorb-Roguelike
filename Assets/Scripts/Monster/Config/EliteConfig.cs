using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 정예몹 스탯 배율(리팩터 C4, 2026-10-12 — 예전 EliteMonsterMarker 인스펙터 값, 결정 대기 체크리스트 D: 체력 3배 / 공격력 1.5배).
    /// 12종 정예 프리팹이 모두 같은 값이라 공용 에셋 1개(Assets/Data/Monsters/EliteConfig.asset)를 공유한다.
    /// 정예 틴트·외곽선 색(연출)은 프리팹(EliteMonsterMarker)에 남는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Elite", fileName = "EliteConfig")]
    public class EliteConfig : ScriptableObject
    {
        [Tooltip("정예 최대 체력 배율 — 스폰 때(EliteMonsterMarker.OnStartServer) MonsterStub.maxHealth에 곱한다. [PLAYTEST]")]
        [SerializeField] internal float healthMultiplier = 3f;
        [Tooltip("정예 공격력 배율 — 같은 시점에 MonsterBehaviorController의 공격력 복사본에 곱한다. [PLAYTEST]")]
        [SerializeField] internal float attackDamageMultiplier = 1.5f;
    }
}
