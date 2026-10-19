using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>소 돌진·강한 넉백 수치(단계 5a, PlayerCombat에서 이름·기본값 그대로 이동).</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Ox", fileName = "OxSkillConfig")]
    public class OxSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 (GDD 메인 슬롯 표, 수치 전부 [PLAYTEST])")]
        [SerializeField] internal float oxKnockbackScale = 2f;
        [Tooltip("소: 기본 공격이 마우스 방향 돌진으로 바뀐다(무적 없음).")]
        [SerializeField] internal float oxChargeDistance = 3f;
        [SerializeField] internal float oxChargeDuration = 0.2f;
        [SerializeField] internal float oxChargeHitRadius = 0.9f;
        [SerializeField] internal float oxChargeDamageScale = 1.2f;
    }
}
