using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>제자리 브레스(용) 고유 수치(리팩터 C4, BreathMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(없음(예고 색은 공통))은 프리팹(BreathMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Breath", fileName = "BreathMonsterConfig")]
    public class BreathMonsterConfig : MonsterStatsConfig
    {
        [Header("제자리 브레스 [PLAYTEST]")]
        [Tooltip("이 거리 안에 플레이어가 있으면 멈춘다(멈춘 뒤 settleDuration이 지나야 예고 시작).")]
        [SerializeField] internal float breathTriggerRange = 2.6f;
        [Tooltip("멈춘 뒤 예고를 시작하기까지 서 있는 시간(이동 중 공격 금지).")]
        [SerializeField] internal float settleDuration = 0.2f;
        [SerializeField] internal float breathRadius = 3f;
        [Tooltip("부채꼴 전체 각도(도).")]
        [SerializeField] internal float breathAngle = 60f;
        [SerializeField] internal float breathWindupDuration = 0.8f;
    }
}
