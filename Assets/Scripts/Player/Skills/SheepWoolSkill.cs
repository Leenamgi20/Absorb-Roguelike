using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 양털(단계 5b, 옛 PlayerController.AddSheepWoolServer·PlayerGuard의 양 분기, GDD v2.33·v2.34): 가드로 막을 때마다 털 +1(서버).
    /// sheepWoolMax에 도달하는 순간 털 폭발 — 반경 sheepBurstRadius 원(DoConeHit 반각 180 = 원 전체) 기본공격 150% + 흡수 스킬 판정·속성,
    /// 파티 전원(본인 포함, 거리 무관)에게 '공격 1회 막기' 보호막(지속 시간 없음, 중첩 없음), 털 0으로 리셋.
    /// 털 수는 [SyncVar] sheepWool(HUD용 동기화)이라 일반 C# 객체로 옮길 수 없어 PlayerController에 남기고 internal 접근자로 읽고 쓴다.
    /// 연출 [TargetRpc] TargetShowSheepBurst·[ClientRpc] RpcAttackFx는 PlayerCombat에 그대로 — 여기서는 반경·색·배율을 넘겨 호출만.
    /// </summary>
    internal sealed class SheepWoolSkill : AbsorbSkillBase
    {
        private readonly SheepSkillConfig _cfg;

        public SheepWoolSkill(AbsorbSkillContext ctx, SheepSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.SheepWool)
        {
            _cfg = cfg;
        }

        /// <summary>서버 전용(PlayerGuard.OnGuardBlockedServer가 isServer 검사 뒤 부른다) — 옛 AddSheepWoolServer 순서 그대로.</summary>
        public override void OnGuardBlocked(Vector2 attackerPos)
        {
            PlayerController controller = ctx.Controller;
            int woolMax = controller.SheepWoolMax; // = Max(1, sheepWoolMax)
            controller.ServerSetSheepWool(Mathf.Min(controller.SheepWool + 1, woolMax));
            if (controller.SheepWool < woolMax)
            {
                return;
            }

            controller.ServerSetSheepWool(0);
            Vector2 facing = controller.FacingDirection.sqrMagnitude > 0.0001f ? controller.FacingDirection.normalized : Vector2.down;
            ctx.Combat.ServerSheepBurstHit(facing, _cfg.sheepBurstRadius, _cfg.sheepBurstDamageScale); // 판정 + 적중 회복

            foreach (PlayerController ally in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                var allyHealth = ally.GetComponent<PlayerHealth>();
                if (allyHealth != null && !allyHealth.IsDead)
                {
                    allyHealth.AddShield(); // 거리 무관 파티 전원
                }
            }

            Debug.Log($"[PlayerController] {controller.name} wool burst (party wool shield)");
            ctx.Combat.ServerShowSheepBurst(facing, _cfg.sheepBurstRadius, _cfg.sheepBurstColor); // 호스트 직접 / 원격 소유자 TargetShowSheepBurst / 다른 화면 RpcAttackFx(순서 그대로)
        }

        /// <summary>
        /// 옛 PlayerController.OnServerTraitChanged의 '서버이고 새 스킬이 양이 아니면 sheepWool = 0'(양이 벗겨지면 모은 털도 사라진다).
        /// - 실행 위치: 서버만(ctx.IsServer = 옛 isServer). 원격 클라의 OnSyncedSkillChanged → Equip에서도 불리지만 거기서는 아무것도 안 한다.
        /// - 양→양 재흡수(next = 양): 리셋 안 함(옛 조건과 같음).
        /// - 양 아닌 스킬끼리 교체: 이 훅이 안 불리지만 그때 털은 이미 0이라 결과 같음(털은 양 장착 중에만 오르고, 양에서 벗어날 때 여기서 0).
        /// - 순서: OnServerTraitChanged의 Host.Equip 안(옛 리셋 바로 앞 자리, 사이에 다른 코드 없음) → SetPassiveTrait보다 먼저(옛 순서와 같음).
        /// </summary>
        public override void OnUnequip(AbsorbRoguelike.Absorption.AbsorbSkill next)
        {
            if (ctx.IsServer && next != AbsorbRoguelike.Absorption.AbsorbSkill.SheepWool)
            {
                ctx.Controller.ServerSetSheepWool(0);
            }
        }
    }
}
