using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>뱀 감기 수치(단계 5a, PlayerCombat에서 이름·기본값 그대로 이동).</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Snake", fileName = "SnakeSkillConfig")]
    public class SnakeSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 (GDD 메인 슬롯 표, 수치 전부 [PLAYTEST])")]
        [SerializeField] internal float snakeSlowMultiplier = 0.6f; // 이동속도 40% 감소
        [SerializeField] internal float snakeSlowDuration = 1.5f;
        [Tooltip("뱀: 주변 원 전체(360°).")]
        [SerializeField] internal float snakeAttackRadius = 1.6f;
    }
}
