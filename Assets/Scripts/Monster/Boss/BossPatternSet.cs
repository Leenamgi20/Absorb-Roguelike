using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 한 종의 패턴 세트(리팩터 C3, 2026-10-12 — 예전 BossChargeAttack 인스펙터 값 일부). 에셋: Assets/Data/Boss/.
    /// 어떤 패턴을 쓰는지·몇 페이즈에 열리는지·첫 쿨·폭주 배율 적용 여부(항목 리스트)와, 보스 단위 페이즈 규칙(체력 비율·폭주 쿨 배율·첫 쿨 값)을 둔다.
    /// 항목 순서 = 가중 무작위 후보 순서(난수 결과가 순서에 달려 있으니 바꾸면 같은 난수에서 다른 패턴이 나온다).
    /// 패턴 수치·쿨·가중치는 각 패턴 에셋(<see cref="BossPatternBase"/>)에 있다. 몸 색·문구 색·추적 거리·파편 프리팹은 보스 프리팹(BossBrain)에 남는다.
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternSet", menuName = "Absorb/Boss/Pattern Set")]
    public class BossPatternSet : ScriptableObject
    {
        /// <summary>패턴이 처음 열릴 때(시작 또는 페이즈 진입) 넣는 쿨.</summary>
        public enum FirstCooldownRule : byte
        {
            Zero,             // 바로 쓸 수 있음(A)
            FirstChargeDelay, // firstChargeDelay(B 시작, C 페이즈 2 진입)
            NewPatternDelay   // newPatternDelay(D·E 페이즈 진입)
        }

        [System.Serializable]
        public class Entry
        {
            public BossPatternBase pattern;
            [Tooltip("이 페이즈(1~4)부터 쓴다. 1이면 처음부터.")]
            [Range(1, 4)] public int unlockPhase = 1;
            public FirstCooldownRule firstCooldown = FirstCooldownRule.Zero;
            [Tooltip("폭주(페이즈 4) 중 채우는 쿨에 enrageCooldownMultiplier를 곱한다. E처럼 쿨이 이미 폭주 기준이면 끈다.")]
            public bool applyEnrageMultiplier = true;
        }

        [Tooltip("항목 순서 = 후보 순서. 근접 우선 패턴(A)은 가중 무작위 전에 먼저 본다.")]
        [SerializeField] private List<Entry> patterns = new List<Entry>();

        [Header("페이즈 (GDD 페이즈 구조 — 보스 전체 체력 비율, 올라가기만 함)")]
        [Tooltip("체력 비율이 이 값 이하가 되면 페이즈 2(C 추가). [PLAYTEST]")]
        [SerializeField] private float phase2HealthRatio = 0.7f;
        [Tooltip("페이즈 3(D 추가). [PLAYTEST]")]
        [SerializeField] private float phase3HealthRatio = 0.4f;
        [Tooltip("페이즈 4 폭주(E 추가, 쿨 감소). [PLAYTEST]")]
        [SerializeField] private float enrageHealthRatio = 0.15f;
        [Tooltip("폭주 중 새로 채우는 쿨(applyEnrageMultiplier가 켜진 항목)에 곱하는 값. [PLAYTEST]")]
        [SerializeField] private float enrageCooldownMultiplier = 0.75f;

        [Header("첫 쿨")]
        [Tooltip("FirstChargeDelay 규칙 항목의 첫 쿨(B는 시작 때, C는 페이즈 2 진입 때). [PLAYTEST]")]
        [SerializeField] private float firstChargeDelay = 2f;
        [Tooltip("NewPatternDelay 규칙 항목의 첫 쿨(D·E 페이즈 진입 때). [PLAYTEST]")]
        [SerializeField] private float newPatternDelay = 2f;

        public IReadOnlyList<Entry> Patterns => patterns;
        public float Phase2HealthRatio => phase2HealthRatio;
        public float Phase3HealthRatio => phase3HealthRatio;
        public float EnrageHealthRatio => enrageHealthRatio;
        public float EnrageCooldownMultiplier => enrageCooldownMultiplier;

        public float FirstCooldown(Entry entry)
        {
            switch (entry.firstCooldown)
            {
                case FirstCooldownRule.FirstChargeDelay:
                    return firstChargeDelay;
                case FirstCooldownRule.NewPatternDelay:
                    return newPatternDelay;
                default:
                    return 0f;
            }
        }
    }
}
