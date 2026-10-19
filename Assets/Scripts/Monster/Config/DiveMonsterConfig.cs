using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>급강하(닭) 고유 수치(리팩터 C4, DiveMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(liftHeight(상승 연출))은 프리팹(DiveMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Dive", fileName = "DiveMonsterConfig")]
    public class DiveMonsterConfig : MonsterStatsConfig
    {
        [Header("급강하 [PLAYTEST]")]
        [Tooltip("이 거리 안에 플레이어가 있으면 멈춰서 급강하 예고를 시작한다.")]
        [SerializeField] internal float diveTriggerRange = 3.5f;
        [SerializeField] internal float landingRadius = 1.3f;
        [SerializeField] internal float diveWindupDuration = 0.7f;
        [Tooltip("예고가 끝난 뒤 착지점까지 날아가는 시간.")]
        [SerializeField] internal float diveDuration = 0.2f;
        [Tooltip("착지 후 멈춰 있는 시간(후딜 대신).")]
        [SerializeField] internal float landPause = 0.4f;
        [Tooltip("벽 앞에서 착지점을 잘라낼 때 남기는 거리(몸 반경 정도).")]
        [SerializeField] internal float wallMargin = 0.4f;
    }
}
