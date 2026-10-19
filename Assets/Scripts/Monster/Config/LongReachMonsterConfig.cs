using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>긴 직선 공격 + 후퇴 깡총(토끼) 고유 수치(리팩터 C4, LongReachMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(없음(예고 색은 공통))은 프리팹(LongReachMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Long Reach", fileName = "LongReachMonsterConfig")]
    public class LongReachMonsterConfig : MonsterStatsConfig
    {
        [Header("긴 직선 공격 [PLAYTEST]")]
        [SerializeField] internal float reachLength = 4f;
        [SerializeField] internal float reachWidth = 0.8f;
        [Tooltip("이 앞쪽 거리부터 끝부분(가중 대미지).")]
        [SerializeField] internal float tipStart = 3f;
        [SerializeField] internal float tipDamageMultiplier = 1.5f;

        [Header("후퇴 깡총 [PLAYTEST]")]
        [SerializeField] internal float hopDistance = 1.5f;
        [SerializeField] internal float hopDuration = 0.2f;
    }
}
