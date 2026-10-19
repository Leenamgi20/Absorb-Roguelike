using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>양털 수치(단계 5b, sheepWoolMax는 PlayerController, sheepBurst* 3개는 PlayerCombat에서 이름·기본값 그대로 이동).
    /// 털 수 SyncVar(sheepWool)는 NetworkBehaviour가 필요해 PlayerController에 남겼다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Absorb Skill Config/Sheep", fileName = "SheepSkillConfig")]
    public class SheepSkillConfig : AbsorbSkillConfig
    {
        /// <summary>sheepWoolMax 기본값 — config가 비었을 때 PlayerController.SheepWoolMax가 옛 기본값 결과(Max(1, 3))와 같게 쓴다.</summary>
        internal const int DefaultWoolMax = 3;

        [Header("흡수 스킬 — 양 (GDD v2.33, 2026-09-29, 수치 전부 [PLAYTEST])")]
        [SerializeField] internal int sheepWoolMax = DefaultWoolMax;    // [PLAYTEST] 이 수에 도달하는 순간 털 폭발
        [SerializeField] internal float sheepBurstRadius = 2.5f;       // [PLAYTEST] 털 폭발 반경(칸)
        [SerializeField] internal float sheepBurstDamageScale = 1.5f;  // [PLAYTEST] 기본공격의 150%
        [SerializeField] internal Color sheepBurstColor = new Color(1f, 1f, 1f, 0.35f);
    }
}
