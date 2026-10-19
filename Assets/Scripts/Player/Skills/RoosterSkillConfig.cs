using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>닭 대시·알 수치(단계 5b, PlayerDash에서 이름·기본값 그대로 이동). 말·닭 공용 dashPathHitRadius는 PlayerDash에 남겼다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Rooster", fileName = "RoosterSkillConfig")]
    public class RoosterSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 닭 대시 (GDD 메인 슬롯 표, 2026-09-28 추가, 수치 전부 [PLAYTEST])")]
        // 닭 경로 30% 스치는 대미지는 GDD '공격기 위주' 원칙 때문에 유지(2026-09-28 결정 ⑱A 확정).
        [SerializeField] internal float roosterDashDamageScale = 0.3f; // [PLAYTEST] 기본공격의 30%, 넉백 없음
        [Tooltip("닭(2026-09-28 개편): 대시마다 대시 시작 위치에 낳는 알(아군 회복) 프리팹.")]
        [SerializeField] internal GameObject roosterEggPrefab;
        [SerializeField] internal int maxEggsPerPlayer = 3; // [PLAYTEST] 넘으면 가장 오래된 알부터 사라진다
        [Tooltip("닭: 알을 먹었을 때 회복하는 체력(본인·팀원 모두). 2026-10-14 RoosterEgg 프리팹에서 옮겨 옴. [PLAYTEST]")]
        [SerializeField] internal int eggHealAmount = 10;
    }
}
