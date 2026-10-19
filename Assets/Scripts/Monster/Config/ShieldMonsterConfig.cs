using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>정면 방어 부품 ⑤(돼지) 고유 수치(리팩터 C4, ShieldMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(방패 색·번쩍임 시간·위치·크기, facingSyncMinAngle(동기화량 절약값))은 프리팹(ShieldMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Shield", fileName = "ShieldMonsterConfig")]
    public class ShieldMonsterConfig : MonsterStatsConfig
    {
        [Header("정면 방어 부품 ⑤ [PLAYTEST]")]
        [SerializeField] internal float shieldHalfAngle = 60f;
        [SerializeField] internal float frontDamageMultiplier = 0.2f;
        [SerializeField] internal float counterWindupDuration = 0.3f;
        [SerializeField] internal float counterCooldown = 2f;
        [Tooltip("방패(정면)가 도는 최대 속도(도/초). 플레이어가 돌아 들어갈 틈.")]
        [SerializeField] internal float shieldTurnSpeed = 90f;
        [Tooltip("반격을 휘두른 직후 방패를 내리는 시간(초) — 이 동안은 정면 감소 없음.")]
        [SerializeField] internal float shieldDownDuration = 1f;
    }
}
