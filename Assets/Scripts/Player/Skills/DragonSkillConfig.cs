using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>용 제자리 브레스 수치(단계 5c, PlayerCombat에서 이름·기본값 그대로 이동).</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Dragon", fileName = "DragonSkillConfig")]
    public class DragonSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 용 (GDD v2.33, 2026-09-29, 수치 전부 [PLAYTEST])")]
        [Tooltip("용 제자리 브레스(속사형, 2026-09-29 개정): 틱 1회당 기본공격 대비 대미지 배율 — 빠르고 약하게. [PLAYTEST]")]
        [SerializeField] internal float dragonBreathDamageScale = 0.25f;
        [Tooltip("용 브레스 틱 간격(초). 기본 공격 한 주기(스윙+재사용 대기 약 0.75초) 대신 이 간격으로 계속 뿜는다. [PLAYTEST]")]
        [SerializeField] internal float dragonBreathTickInterval = 0.15f;
        [SerializeField] internal float dragonBreathRadius = 4f;       // [PLAYTEST] 부채꼴 반경(칸)
        [SerializeField] internal float dragonBreathHalfAngle = 35f;   // [PLAYTEST] 반각(도)
        [Tooltip("용 브레스 연출 색(속성이 없을 때). 속성이 있으면 속성 색을 쓴다. [PLAYTEST]")]
        [SerializeField] internal Color dragonBreathColor = new Color(1f, 0.55f, 0.15f, 0.4f);
    }
}
