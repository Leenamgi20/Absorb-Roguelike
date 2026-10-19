using AbsorbRoguelike.Player;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 층·인원 난이도 배율 계산 도우미(2026-10-13, 레벨 디자인 시트 1차). 값은 <see cref="DifficultyConfig"/> 에셋을
    /// <see cref="DungeonGameManager"/>를 통해 읽는다(MonsterMoveSpeedScale과 같은 경로) — 매니저·에셋이 없으면 모든 배율 1.
    /// 서버에서 부른다: 스폰 체력(MonsterStub·BossStub), 몬스터→플레이어 대미지(MonsterBehaviorController.DealDamage·
    /// MonsterProjectile·BossBrain.HitPlayers*). 적용 순서·제외 지점: docs/04_구현위키/레벨_디자인_시트.md.
    /// </summary>
    public static class DifficultyScaling
    {
        private static DifficultyConfig Config => DungeonGameManager.Instance != null ? DungeonGameManager.Instance.DifficultyConfig : null;

        /// <summary>지금 층(1부터). FloorProgression이 없으면 1.</summary>
        public static int CurrentFloor => FloorProgression.Instance != null ? Mathf.Max(1, FloorProgression.Instance.Floor) : 1;

        /// <summary>지금 접속한 플레이어 수(쓰러진 사람 포함, 최소 1). 서버에서는 접속 인원, 원격 클라에서는 보이는 플레이어 수.</summary>
        public static int PartyCount => Mathf.Max(1, Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length);

        /// <summary>지금 층·인원 기준 잡몹 체력 배율(디버그 표시용).</summary>
        public static float CurrentHealthScale => HealthScale(Config, CurrentFloor, PartyCount, includeParty: true);

        /// <summary>지금 층 기준 대미지 배율(디버그 표시용).</summary>
        public static float CurrentDamageScale => Config != null ? Config.FloorDamageMultiplier(CurrentFloor) : 1f;

        /// <summary>체력 배율 계산식(에디터 시트 미리보기도 같은 식을 쓴다). config가 없으면 1.</summary>
        public static float HealthScale(DifficultyConfig config, int floor, int partyCount, bool includeParty)
        {
            if (config == null)
            {
                return 1f;
            }

            return config.FloorHealthMultiplier(floor) * (includeParty ? config.PartyHealthMultiplier(partyCount) : 1f);
        }

        /// <summary>최대 체력에 층 배율 × (includeParty면 인원 배율)을 곱한다. 반올림, 최소 1.</summary>
        public static int MonsterHealth(int baseHealth, bool includeParty)
        {
            return ScaleHealth(baseHealth, HealthScale(Config, CurrentFloor, PartyCount, includeParty));
        }

        /// <summary>몬스터→플레이어 대미지에 층 배율을 곱한다. 반올림, baseDamage &gt; 0이면 최소 1, 0 이하는 그대로.</summary>
        public static int MonsterDamage(int baseDamage)
        {
            return ScaleDamage(baseDamage, CurrentDamageScale);
        }

        public static int ScaleHealth(int baseHealth, float scale) => Mathf.Max(1, Mathf.RoundToInt(baseHealth * scale));

        public static int ScaleDamage(int baseDamage, float scale) => baseDamage <= 0 ? baseDamage : Mathf.Max(1, Mathf.RoundToInt(baseDamage * scale));
    }
}
