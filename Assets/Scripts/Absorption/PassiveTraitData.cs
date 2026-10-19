using AbsorbRoguelike.Localization;
using UnityEngine;

namespace AbsorbRoguelike.Absorption
{
    /// <summary>흡수하면 기본 공격 동작 자체가 바뀌는 동물 스킬(GDD 메인 슬롯 표, 2026-09-23 첫 구현 3종).</summary>
    public enum AbsorbSkill
    {
        None,
        TigerCombo,
        SnakeBind,
        OxKnockback,
        // 2026-09-28 추가 4종 — 기존 값 뒤에만 붙인다(에셋에 정수로 직렬화되므로 순서 변경 금지).
        HorseDoubleDash, // 2026-09-28 개편: 옛 차지 돌진 → 2단 대시 + 적 통과 경로 대미지(유저 플레이 피드백). 정수값 4 유지
        PigCounter,
        RoosterEgg, // 2026-09-28 개편: 옛 급강하 무적이 기본 대시 무적과 겹쳐 쓸모없어 → 대시마다 알(아군 회복). 정수값 6 유지
        RabbitEarWhip, // 2026-09-28 개편(결정 ㉑A): 옛 대시 쿨·무적·뒷발차기 → 귀 채찍 기본공격, 정수값 7 유지
        // 2026-09-29 남은 12지신 5종 (GDD v2.33) — 뒤에만 붙인다.
        RatGnaw,       // 8: 명중마다 취약 스택
        DragonBreath,  // 9: 제자리 브레스, 이동 중 공격 불가(결정 ㉘-1 가)
        SheepWool,     // 10: 가드 막기마다 털, 3개면 털 폭발+보호막
        MonkeyBanana,  // 11: 바나나 원거리 던지기(결정 ㉘-2 c)
        DogCompanion   // 12: 개 동료가 최근 때린 적을 물음
    }

    /// <summary>
    /// 흡수로 얻는 패시브 특성 하나를 나타내는 데이터. GDD상 최종적으로는 동물 종류마다 완전히 다른
    /// 능력치/효과를 가지지만, 지금은 "흡수하면 슬롯의 효과가 실제로 바뀌고, 새로 흡수하면 기존 효과가
    /// 사라진다"는 교체 루프 자체를 검증하는 단계라 효과 종류를 두 가지(이동속도, 공격력 보너스)로만
    /// 한정해뒀다. 실제 동물 특성 로스터는 이후 콘텐츠 작업에서 이 클래스에 필드를 늘려가며 채운다.
    /// </summary>
    [CreateAssetMenu(fileName = "PassiveTrait", menuName = "AbsorbRoguelike/Passive Trait")]
    public class PassiveTraitData : ScriptableObject
    {
        [SerializeField] private string displayName = "Unnamed Trait";
        [SerializeField] private float moveSpeedMultiplier = 1f;
        [SerializeField] private int attackDamageBonus;
        [Tooltip("흡수하면 기본 공격이 이 동물 스킬로 바뀐다. None이면 수치 보너스만.")]
        [SerializeField] private AbsorbSkill skill = AbsorbSkill.None;
        [Tooltip("언어 표(strings.tsv) 키 id — trait.<id>.name / .desc / .short. 비우면 스킬 enum 이름(예: TigerCombo). displayName은 표에 키가 없을 때의 폴백.")]
        [SerializeField] private string locId;

        public AbsorbSkill Skill => skill;
        /// <summary>원본 이름(에셋 값, 한국어). 화면 표시는 <see cref="LocalizedName"/>을 쓴다 — 이건 로그·키 누락 폴백용.</summary>
        public string DisplayName => displayName;
        /// <summary>언어 표 키 id(trait.&lt;id&gt;.*). 네트워크로는 이 id를 보내고 받는 쪽이 자기 언어로 표시한다.</summary>
        public string LocId => string.IsNullOrEmpty(locId) ? skill.ToString() : locId;
        /// <summary>현재 언어 이름. 표에 키가 없으면 displayName.</summary>
        public string LocalizedName => Loc.TryGet(NameKey(LocId), out string name) ? name : displayName;

        public static string NameKey(string id) => $"trait.{id}.name";
        public float MoveSpeedMultiplier => moveSpeedMultiplier;
        public int AttackDamageBonus => attackDamageBonus;
    }
}
