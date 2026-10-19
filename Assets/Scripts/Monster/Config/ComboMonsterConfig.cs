using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>연속 공격 부품 ②(호랑이) 고유 수치(리팩터 C4, ComboMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(없음(예고 색은 공통))은 프리팹(ComboMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Combo", fileName = "ComboMonsterConfig")]
    public class ComboMonsterConfig : MonsterStatsConfig
    {
        [Header("연속 공격 부품 ② [PLAYTEST]")]
        [SerializeField] internal int hitCount = 3;
        [SerializeField] internal float comboWindupDuration = 0.25f;
        [Tooltip("타와 타 사이 정지 시간(이후 다음 예고 시작). 타 간격(이 값 + 예고)이 플레이어 피격 무적 0.5초보다 짧으면 중간 타가 무적에 막힌다.")]
        [SerializeField] internal float comboGap = 0.3f;
        [Tooltip("마지막 타 명중 시 플레이어 넉백 거리(칸). 0이면 없음.")]
        [SerializeField] internal float lastHitKnockbackDistance = 1.5f;
    }
}
