using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>개 동료 수치(단계 5c, PlayerController에서 이름 그대로 이동). 물기 대미지 배율은 2026-10-14에 DogCompanion 프리팹에서 옮겨 왔다
    /// (레벨 디자인 시트 동물 스킬 탭에서 고치게). 따라다니기·물기 간격 같은 동료 움직임은 프리팹에 남긴다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Dog", fileName = "DogSkillConfig")]
    public class DogSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 개 (GDD v2.33, 2026-09-29, 수치 전부 [PLAYTEST])")]
        [Tooltip("개: 특성을 든 동안 서버가 스폰하는 동료 프리팹(DogCompanion).")]
        [SerializeField] internal GameObject dogCompanionPrefab;
        [Tooltip("개: 동료가 한 번 물 때의 대미지 = 기본 공격 × 이 값(0.6 = 60%). 물기 간격은 DogCompanion 프리팹의 biteInterval. [PLAYTEST]")]
        [SerializeField] internal float dogBiteDamageScale = 0.6f;
    }
}
