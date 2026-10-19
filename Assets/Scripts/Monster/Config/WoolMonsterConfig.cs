using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>털 폭발(양) 고유 수치(리팩터 C4, WoolMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(털 표시 위치·색)은 프리팹(WoolMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Wool", fileName = "WoolMonsterConfig")]
    public class WoolMonsterConfig : MonsterStatsConfig
    {
        [Header("털 폭발 [PLAYTEST]")]
        [SerializeField] internal int woolMax = 3;
        [SerializeField] internal float burstRadius = 2.2f;
        [SerializeField] internal float burstWindupDuration = 0.7f;
        [Tooltip("털이 가득 찼을 때 이 거리 안이면 멈춰서 털 폭발 예고를 시작한다.")]
        [SerializeField] internal float burstTriggerRange = 1.8f;
    }
}
