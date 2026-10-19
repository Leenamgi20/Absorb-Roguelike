using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>원숭이 바나나 수치(단계 5c, PlayerCombat에서 이름 그대로 이동). 바나나 대미지 배율은 2026-10-14에 BananaProjectile 프리팹에서 옮겨 왔다
    /// (레벨 디자인 시트 동물 스킬 탭에서 고치게). 속도·사거리·넉백 같은 투사체 움직임은 프리팹에 남긴다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Monkey", fileName = "MonkeySkillConfig")]
    public class MonkeySkillConfig : AbsorbSkillConfig
    {
        [Header("흡수 스킬 — 원숭이 (GDD v2.33, 2026-09-29, 수치 전부 [PLAYTEST])")]
        [Tooltip("원숭이: 기본 공격 대신 마우스 방향으로 던지는 바나나 투사체 프리팹(BananaProjectile).")]
        [SerializeField] internal GameObject bananaProjectilePrefab;
        [Tooltip("원숭이: 바나나 한 발의 대미지 = 기본 공격 × 이 값(1.1 = 110%). 던지는 간격은 기본 공격 주기와 같다. [PLAYTEST]")]
        [SerializeField] internal float bananaDamageScale = 1.1f;
    }
}
