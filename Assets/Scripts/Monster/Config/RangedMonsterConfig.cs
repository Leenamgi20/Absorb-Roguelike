using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>원거리 개체·투사체 부품 ④(원숭이) 고유 수치(리팩터 C4, RangedMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(projectilePrefab(참조)·projectileTelegraphWidth(예고 폭). 투사체 속도·사거리·판정 반경은 MonsterBanana 프리팹(MonsterProjectile))은 프리팹(RangedMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Ranged", fileName = "RangedMonsterConfig")]
    public class RangedMonsterConfig : MonsterStatsConfig
    {
        [Header("원거리 개체 전용")]
        [SerializeField] internal float keepDistance = 2.5f;

        [Header("투사체 부품 ④ [PLAYTEST]")]
        [Tooltip("몸 중심에서 투사체가 나오는 거리.")]
        [SerializeField] internal float muzzleOffset = 0.5f;
    }
}
