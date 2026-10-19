using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>말 연속 대시 수치(단계 5b, PlayerDash에서 이름·기본값 그대로 이동).
    /// 모든 대시 공용인 horsePassThroughLayers(결정 ㉓A)와 말·닭 공용 dashPathHitRadius는 PlayerDash에 남겼다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Horse", fileName = "HorseSkillConfig")]
    public class HorseSkillConfig : AbsorbSkillConfig
    {
        /// <summary>horseDashCharges 기본값 — config가 비었을 때 HorseDashesLeft가 옛 초기값(Max(1, 2))과 같게 쓴다.</summary>
        internal const int DefaultDashCharges = 2;

        [Header("흡수 스킬 — 말 대시 (GDD 메인 슬롯 표, 2026-09-28 추가, 수치 전부 [PLAYTEST])")]
        [Tooltip("말(2026-09-28 개편): 쿨다운 전에 연달아 쓸 수 있는 대시 횟수. [PLAYTEST]")]
        [SerializeField] internal int horseDashCharges = DefaultDashCharges;
        [Tooltip("말: 앞 대시가 끝난 뒤 다음 대시를 쿨다운 없이 이어 쓸 수 있는 시간(초). 지나면 dashCooldown 시작. [PLAYTEST]")]
        [SerializeField] internal float horseSecondDashWindow = 0.6f;
        [SerializeField] internal float horseDashDamageScale = 1f; // [PLAYTEST] 통과하며 주는 경로 대미지 = 기본공격의 100%, 넉백 없음
    }
}
