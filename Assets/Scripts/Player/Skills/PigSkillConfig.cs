using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>돼지 반격 수치(단계 5b, PlayerGuard에서 이름·기본값 그대로 이동).</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Pig", fileName = "PigSkillConfig")]
    public class PigSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 돼지 반격 (GDD 메인 슬롯 표, 수치 전부 [PLAYTEST])")]
        [Tooltip("돼지: 가드 무적 부채꼴 폭. 기본 guardBlockAngle(120) 확장치 — 기획_누락_추적 #11 미확정. [PLAYTEST]")]
        [SerializeField] internal float pigGuardBlockAngle = 180f;
        [SerializeField] internal float pigCounterDamageScale = 1.5f; // [PLAYTEST] 반격 대미지 = 기본공격의 150%
        [SerializeField] internal float pigCounterCooldown = 1.5f; // [PLAYTEST] 반격 재발동 쿨타임(초)
        [Tooltip("상황 특성 '연타 휘두르기' 층에서의 돼지 반격 쿨타임(초). 연타 간격(0.45초)보다 짧아 매 타격마다 반격한다. [PLAYTEST]")]
        [SerializeField] internal float pigCounterCooldownFlurry = 0.4f;
        [Tooltip("돼지 반격 반경 여유(칸): 반경 = max(기본 반경, 공격자 거리 + 이 값) — 막은 공격자는 반드시 맞는다. [PLAYTEST]")]
        [SerializeField] internal float pigCounterRadiusMargin = 0.5f;
    }
}
