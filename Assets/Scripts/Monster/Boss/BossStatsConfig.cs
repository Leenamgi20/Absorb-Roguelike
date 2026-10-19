using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 몸 수치(리팩터 C4, 2026-10-12 — 예전 BossStub 인스펙터 값, 이름·기본값·Tooltip 그대로). 에셋: Assets/Data/Boss/&lt;보스&gt;_Stats.asset.
    /// 체력·개인 할당량·맨몸 무기 대미지·갑옷·재생처럼 '보스가 맞는 쪽' 규칙이라 '어떤 패턴을 언제 쓰나'를 담는 BossPatternSet(C3)과 나눠 둔다.
    /// 막힘 번쩍임 시간(연출)은 프리팹(BossStub.deflectFlashDuration)에 남는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Boss/Stats", fileName = "BossStats")]
    public class BossStatsConfig : ScriptableObject
    {
        [Tooltip("스폰 때 BossStub.maxHealth(SyncVar)에 넣는 값.")]
        [SerializeField] internal int maxHealth = 300;

        [Header("개인 할당량 & 지원 전환 (GDD 참고, 결정 대기 체크리스트 G)")]
        [Tooltip("체크하면 파티원 수만큼 체력을 나눠 개인 할당량으로 관리한다. 체크 안 하면 기존처럼 공유 체력풀로 동작한다.")]
        [SerializeField] internal bool usesPersonalQuota;
        [SerializeField] internal int supportGaugeThreshold = 30; // [PLAYTEST] 지원 발동까지 필요한 초과 대미지 누적량
        [SerializeField] internal int supportHealAmount = 20; // [PLAYTEST] 지원 발동 시 회복량

        [Header("보스 대미지 규칙 (GDD, 프로토타입 검증 중)")]
        [Tooltip("무기 기본 공격이 보스에게 주는 고정 대미지. 파편 뱉기·흡수 스킬만 제대로 된 딜 수단이 되도록.")]
        [SerializeField] internal int weaponDamageToBoss = 1; // [PLAYTEST]

        [Header("상황 특성 — 갑옷 (GDD ⑮A, FloorProgression.CurrentBossSituation == Armored)")]
        [SerializeField] internal int armorMax = 100; // [PLAYTEST]
        [Tooltip("갑옷이 남아 있는 동안 받는 대미지 배율(올림, 최소 1). [PLAYTEST]")]
        [SerializeField] internal float armorDamageScale = 0.4f;
        [SerializeField] internal int armorBreakOxCharge = 40; // [PLAYTEST] 소 돌진 명중 1회당 갑옷 감소
        [SerializeField] internal int armorBreakAbsorbSkill = 5; // [PLAYTEST] 그 외 흡수 스킬 직접 명중 1회당
        [SerializeField] internal int armorBreakSpit = 10; // [PLAYTEST] 파편 뱉기 명중 1회당
        [Tooltip("쥐 갉기 명중 1회당 갑옷 추가 감소(흡수 스킬 기본 감소에 더해짐). 소가 없을 때의 두 번째 해법(GDD v2.33). [PLAYTEST]")]
        [SerializeField] internal int armorBreakRatExtra = 5;

        [Header("상황 특성 — 재생 (GDD ⑮A, Regenerating)")]
        [Tooltip("초당 보스 최대 체력의 몇 %를 회복하는지. 화상 스택이 있으면 멈춘다. [PLAYTEST]")]
        [SerializeField] internal float regenPercentPerSecond = 1f;
    }
}
