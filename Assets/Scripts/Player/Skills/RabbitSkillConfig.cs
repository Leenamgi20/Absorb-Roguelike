using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>토끼 귀 채찍 수치(단계 5a, PlayerCombat에서 이름·기본값 그대로 이동).</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Rabbit", fileName = "RabbitSkillConfig")]
    public class RabbitSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 토끼 귀 채찍 (2026-09-28 개편, 결정 ㉑A, 수치 전부 [PLAYTEST])")]
        [Tooltip("귀 채찍 직선 판정 길이(칸) = 끝부분 사거리 링 바깥 반경. [PLAYTEST]")]
        [SerializeField] internal float earWhipLength = 4.0f;
        [Tooltip("귀 채찍 직선 판정 폭(칸). [PLAYTEST]")]
        [SerializeField] internal float earWhipWidth = 0.6f;
        [Tooltip("끝부분 구간 길이(칸): 거리 ≥ length - tip이면 끝부분 강타. [PLAYTEST]")]
        [SerializeField] internal float earWhipTipLength = 1.0f;
        [Tooltip("근접 구간(칸): 거리 < closeRange이면 약한 근접 타격. [PLAYTEST]")]
        [SerializeField] internal float earWhipCloseRange = 1.5f;
        [SerializeField] internal float earWhipTipDamageScale = 1.5f;   // [PLAYTEST] 끝부분 = 기본공격의 150%
        [SerializeField] internal float earWhipMidDamageScale = 1.0f;   // [PLAYTEST] 중간 = 100%
        [SerializeField] internal float earWhipCloseDamageScale = 0.7f; // [PLAYTEST] 근접 = 70%
        [SerializeField] internal float earWhipTipStunDuration = 0.4f;  // [PLAYTEST] 끝부분 명중 경직(초, 대지 경직과 같은 ApplyStun 슬롯)
        [SerializeField] internal float earWhipVfxDuration = 0.12f;     // [PLAYTEST] 귀 직선 이펙트가 뻗었다 사라지는 시간(초)
        [SerializeField] internal Color earWhipRingColor = new Color(1f, 0.82f, 0.25f, 0.28f);      // [PLAYTEST] 끝부분 사거리 링(반투명 금색)
        [SerializeField] internal Color earWhipCloseRingColor = new Color(1f, 1f, 1f, 0.12f);       // [PLAYTEST] 근접 경계 얇은 원(더 옅게)
    }
}
