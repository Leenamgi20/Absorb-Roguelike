using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 몬스터 전체 난이도 배율(리팩터 C2, 2026-10-12 — 예전 DungeonGameManager 인스펙터 값). 에셋: Assets/Data/Dungeon/DifficultyConfig.asset.
    /// 런타임에서는 <see cref="DungeonGameManager.MonsterMoveSpeedScale"/> 등 정적 속성으로 읽는다(매니저·에셋이 없으면 1).
    /// 층·인원 배율(2026-10-13, 레벨 디자인 시트 1차)은 <see cref="DifficultyScaling"/>이 읽어 스폰 체력·플레이어 피격 대미지에 곱한다.
    /// </summary>
    [CreateAssetMenu(fileName = "DifficultyConfig", menuName = "Absorb/Dungeon/Difficulty Config")]
    public class DifficultyConfig : ScriptableObject
    {
        [Header("몬스터 전체 난이도 배율 (2026-10-06, 레벨 디자인 시트 전 임시 손잡이)")]
        [Tooltip("모든 잡몹·정예몹 추격 이동속도 배율. 1 = 프리팹 값 그대로. [PLAYTEST]")]
        [SerializeField, Range(0.3f, 1.5f)] private float monsterMoveSpeedScale = 0.75f;
        [Tooltip("공격 쿨다운 배율(클수록 공격 간격이 길어짐). [PLAYTEST]")]
        [SerializeField, Range(0.5f, 3f)] private float monsterAttackCooldownScale = 1.35f;
        [Tooltip("공격 예고(윈드업) 길이 배율(클수록 피할 시간이 길어짐). [PLAYTEST]")]
        [SerializeField, Range(0.5f, 3f)] private float monsterWindupScale = 1.25f;

        [Header("층·인원 배율 (2026-10-13, 레벨 디자인 시트 1차)")]
        [Tooltip("층별 몬스터 최대 체력 배율(칸 0 = 1층 … 칸 4 = 5층). 적용: 잡몹·정예·보스 스폰 체력(정예 배율 뒤에 곱함). 제외: 플레이어 체력. 배열이 짧으면 마지막 값. [PLAYTEST]")]
        [SerializeField] private float[] floorHealthMultipliers = { 1f, 1.15f, 1.3f, 1.45f, 1.6f };
        [Tooltip("층별 몬스터→플레이어 대미지 배율(칸 0 = 1층 … 칸 4 = 5층). 적용: 잡몹·정예 공격·투사체, 보스 패턴 타격. 제외: 플레이어→몬스터 대미지, 상태이상 도트·방전, 방 정리. 배열이 짧으면 마지막 값. [PLAYTEST]")]
        [SerializeField] private float[] floorDamageMultipliers = { 1f, 1.1f, 1.2f, 1.3f, 1.4f };
        [Tooltip("인원별 몬스터 최대 체력 배율(칸 0 = 1인 … 칸 3 = 4인). 적용: 잡몹·정예 스폰 체력(층 배율과 곱함). 제외: 보스(보스는 인원별 개인 할당량 규칙). 인원 = 스폰 순간 접속 인원(쓰러진 사람 포함). [PLAYTEST]")]
        [SerializeField] private float[] partyHealthMultipliers = { 1f, 1.5f, 2f, 2.5f };

        public float MonsterMoveSpeedScale => monsterMoveSpeedScale;
        public float MonsterAttackCooldownScale => monsterAttackCooldownScale;
        public float MonsterWindupScale => monsterWindupScale;

        /// <summary>층 체력 배율(층 1부터). 배열이 짧으면 마지막 값, 비었으면 1, 1 미만은 1층 값.</summary>
        public float FloorHealthMultiplier(int floor) => Pick(floorHealthMultipliers, floor);

        /// <summary>층 대미지 배율(층 1부터). 규칙은 <see cref="FloorHealthMultiplier"/>와 같다.</summary>
        public float FloorDamageMultiplier(int floor) => Pick(floorDamageMultipliers, floor);

        /// <summary>인원 체력 배율(1인부터). 규칙은 <see cref="FloorHealthMultiplier"/>와 같다.</summary>
        public float PartyHealthMultiplier(int count) => Pick(partyHealthMultipliers, count);

        private static float Pick(float[] values, int oneBasedIndex)
        {
            if (values == null || values.Length == 0)
            {
                return 1f;
            }

            int index = Mathf.Clamp(oneBasedIndex - 1, 0, values.Length - 1);
            return values[index];
        }
    }
}
