using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 동물 흡수 스킬 수치의 공통 기반(리팩터 B안 단계 5a). 스킬마다 하위 클래스 1개 + 에셋 1개(Assets/Data/AbsorbSkills/)를 둔다.
    /// 공용 필드는 없다 — 스킬별 수치는 PlayerCombat에 있던 이름·타입·기본값 그대로 하위 클래스로 옮겼다.
    /// AbsorbSkillHost가 SerializeField로 참조하고, 스킬 객체(AbsorbSkillBase)가 읽는다.
    /// </summary>
    public abstract class AbsorbSkillConfig : ScriptableObject
    {
    }
}
