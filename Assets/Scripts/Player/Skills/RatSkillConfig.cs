using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>쥐 갉기 수치. 취약 스택 수치 3개는 2026-10-14에 StatusEffectConfig에서 옮겨 왔다(이름·값 그대로) —
    /// 취약을 거는 건 쥐 흡수 스킬뿐이라, 레벨 디자인 시트에서 쥐를 조정할 때 쥐 칸에서 바로 보이게 한다.
    /// RatGnawSkill이 취약을 걸 때 이 값을 StatusEffectReceiver.AddVulnerableStack에 넘긴다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Rat", fileName = "RatSkillConfig")]
    public class RatSkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 쥐 갉기 취약 (GDD v2.33, 수치 전부 [PLAYTEST])")]
        [Tooltip("쥐: 적 하나에 쌓이는 취약 스택 최대 개수. [PLAYTEST]")]
        [SerializeField] internal int vulnerableMaxStacks = 5;
        [Tooltip("쥐: 취약 지속 시간(초). 새로 맞히면 갱신되고, 끝나면 스택이 0이 된다. [PLAYTEST]")]
        [SerializeField] internal float vulnerableDuration = 4f;
        [Tooltip("쥐: 스택 하나당 적이 받는 명중 대미지 증가 비율(0.06 = +6%). 화상·독 도트·전기 방전에는 곱하지 않는다. [PLAYTEST]")]
        [SerializeField] internal float vulnerableDamagePerStack = 0.06f;
    }
}
