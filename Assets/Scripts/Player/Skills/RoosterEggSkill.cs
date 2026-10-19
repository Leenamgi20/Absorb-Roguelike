using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 닭 알(단계 5b, 옛 PlayerDash의 닭 분기·CmdLayEgg 본문): 대시 경로 30% 스치는 대미지(넉백 없음, 결정 ⑱A)와 대시마다 대시 시작 위치에 알.
    /// [Command] CmdLayEgg은 NetworkBehaviour가 필요해 PlayerDash에 그대로 있다 — 로컬 플레이어가 스킬과 무관하게 대시마다 보내고(호스트 포함),
    /// 서버가 Host.Current(이 객체일 때만)의 ServerOnDashCommand로 본문을 돌린다. 닭 판정은 서버.
    /// 알 목록은 서버 전용 — 미리 만든 객체가 보관해 옛 PlayerDash._eggs처럼 스킬 교체 뒤에도 유지된다(층 정리·만료로 사라진 알은 다음 알 때 정리).
    /// </summary>
    internal sealed class RoosterEggSkill : AbsorbSkillBase
    {
        private readonly RoosterSkillConfig _cfg;
        private readonly List<GameObject> _eggs = new List<GameObject>(); // 서버 전용

        public RoosterEggSkill(AbsorbSkillContext ctx, RoosterSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.RoosterEgg)
        {
            _cfg = cfg;
        }

        public override bool AllowsHit(PlayerCombat.HitKind kind) =>
            kind == PlayerCombat.HitKind.Swing || kind == PlayerCombat.HitKind.DashPath;

        public override void ModifyDash(ref PlayerDash.DashParams dash)
        {
            // 무적은 기본 대시 무적만(옛 연장 무적 제거 — 기본 대시와 겹쳐 쓸모없다는 피드백). 알은 대시 시작 때 CmdLayEgg로 낳는다.
            dash.PathDamageScale = _cfg.roosterDashDamageScale; // 30% 스치는 대미지 유지(결정 ⑱A)
            dash.PathKnockbackDistance = 0f; // GDD: "스치는" 대미지 — 넉백 없음
        }

        /// <summary>서버(CmdLayEgg 본문): 닭 흡수 중이면 알을 스폰한다. 플레이어별 최대 maxEggsPerPlayer개 — 넘으면 가장 오래된 알 제거.
        /// 연결 소유 없이 스폰해서 층 이동 정리(AdvanceFloor)에 같이 걸린다. 닭 여부는 호출부(Host.Current)가 이미 걸렀다.</summary>
        public override void ServerOnDashCommand(Vector2 position)
        {
            if (_cfg.roosterEggPrefab == null || (ctx.Health != null && ctx.Health.IsDead))
            {
                return;
            }

            _eggs.RemoveAll(egg => egg == null); // 먹힘/만료/층 정리로 이미 사라진 알
            while (_eggs.Count >= Mathf.Max(1, _cfg.maxEggsPerPlayer))
            {
                GameObject oldest = _eggs[0];
                _eggs.RemoveAt(0);
                NetworkServer.Destroy(oldest);
            }

            GameObject eggObject = Object.Instantiate(_cfg.roosterEggPrefab, position, Quaternion.identity);
            var egg = eggObject.GetComponent<AbsorbRoguelike.Absorption.RoosterEgg>();
            if (egg != null)
            {
                egg.ownerNetId = ctx.Identity.netId;
                egg.SetHealAmount(_cfg.eggHealAmount);
            }

            NetworkServer.Spawn(eggObject);
            _eggs.Add(eggObject);
        }
    }
}
