using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>갉아먹기(쥐) 고유 수치(리팩터 C4, GnawMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(갉힌 자국 표시 색·크기·간격·높이)은 프리팹(GnawMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Gnaw", fileName = "GnawMonsterConfig")]
    public class GnawMonsterConfig : MonsterStatsConfig
    {
        [Header("갉아먹기 [PLAYTEST]")]
        [Tooltip("이 수만큼 자국이 차는 물기가 강한 물기가 된다.")]
        [SerializeField] internal int maxStacks = 3;
        [Tooltip("자국이 가득 차는 물기의 대미지 배율.")]
        [SerializeField] internal float burstMultiplier = 3f;
        [Tooltip("대상과 이 거리(칸) 이상 벌어지면 자국 0.")]
        [SerializeField] internal float resetDistance = 2f;
        [Tooltip("마지막 물기 후 이 시간(초)이 지나면 자국 0.")]
        [SerializeField] internal float resetTime = 1.5f;
    }
}
