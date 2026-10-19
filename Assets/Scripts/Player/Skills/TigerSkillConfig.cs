using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>호랑이 연속 할퀴기 수치(단계 5a, PlayerCombat에서 이름·기본값 그대로 이동).</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Tiger", fileName = "TigerSkillConfig")]
    public class TigerSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 (GDD 메인 슬롯 표, 수치 전부 [PLAYTEST])")]
        [SerializeField] internal float tigerHitDamageScale = 0.4f; // 3연타 합계 120%
        [Tooltip("호랑이: 좁고 긴 부채꼴.")]
        [SerializeField] internal float tigerAttackRadius = 2.4f;
        [SerializeField] internal float tigerAttackHalfAngle = 25f;
    }
}
