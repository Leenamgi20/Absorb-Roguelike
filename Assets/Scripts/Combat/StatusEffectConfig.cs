using UnityEngine;

namespace AbsorbRoguelike.Combat
{
    /// <summary>
    /// 속성 부가 효과(화상·독·냉기·대지·전기·취약) 수치(리팩터 C4, 2026-10-12 — 예전 StatusEffectReceiver 인스펙터 값, 이름·기본값 그대로).
    /// 잡몹 24종·보스가 모두 같은 값이라 공용 에셋 1개(Assets/Data/Combat/StatusEffectConfig.asset)를 공유한다.
    /// 효과 규칙 설명은 StatusEffectReceiver 클래스 주석. 오버레이 투명도(연출)는 리시버에 남는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Combat/Status Effect Config", fileName = "StatusEffectConfig")]
    public class StatusEffectConfig : ScriptableObject
    {
        [Header("공통")]
        [SerializeField] internal float tickInterval = 0.5f; // [PLAYTEST] 결정 ⑤A: 1 → 0.5

        [Header("화염 — 화상 스택")]
        [SerializeField] internal int fireMaxStacks = 5; // [PLAYTEST]
        [SerializeField] internal float fireDpsPerStack = 4f; // [PLAYTEST] 결정 ⑤A: 2 → 4
        [SerializeField] internal float fireDuration = 3f; // [PLAYTEST]

        [Header("독 — 중독 스택")]
        [SerializeField] internal int poisonMaxStacks = 10; // [PLAYTEST]
        [SerializeField] internal float poisonDpsPerStack = 2f; // [PLAYTEST] 결정 ⑤A: 1 → 2
        [SerializeField] internal float poisonDuration = 5f; // [PLAYTEST]

        [Header("냉기 — 스택 둔화")]
        [Tooltip("냉기 스택 1/2/3일 때 이동속도 배율. 배열 길이 = 최대 스택.")]
        [SerializeField] internal float[] iceSlowMultipliers = { 0.8f, 0.6f, 0.4f }; // [PLAYTEST]
        [SerializeField] internal float iceStackDuration = 3f; // [PLAYTEST] 새로 맞으면 갱신

        [Header("대지 — 경직")]
        [SerializeField] internal float earthStunDuration = 0.3f; // [PLAYTEST]
        [SerializeField] internal float earthStunCooldown = 1f; // [PLAYTEST] 같은 대상 재경직까지

        [Header("전기 — 감전 스택/방전")]
        [SerializeField] internal int electricDischargeStacks = 3; // [PLAYTEST] 이 스택에 도달하는 순간 방전
        [SerializeField] internal int electricDischargeDamage = 12; // [PLAYTEST]
        [SerializeField] internal float electricParalyzeDuration = 1f; // [PLAYTEST]
        [SerializeField] internal float electricStackDuration = 4f; // [PLAYTEST] 새로 맞으면 갱신

        // 취약(쥐 갉기) 수치 3개는 2026-10-14에 RatSkillConfig로 옮겼다 — 취약을 거는 건 쥐 흡수 스킬뿐.
    }
}
