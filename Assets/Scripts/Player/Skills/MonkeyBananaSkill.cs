using System.Collections;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 원숭이 바나나(단계 5c, 옛 PlayerCombat의 원숭이 분기·CmdThrowBanana 본문 — 결정 ㉘-2 c): 휘두르기 대신 마우스 방향으로 바나나를 던진다.
    /// 공격 주기는 기본 공격과 같다(스윙 시간 + 재사용 대기). [Command] CmdThrowBanana는 NetworkBehaviour가 필요해 PlayerCombat에 그대로 있고
    /// (원격 클라이언트도 동작), 서버가 Host.Current(이 객체일 때만)의 ServerOnAttackCommand로 본문을 돌린다 — 5b 닭 CmdLayEgg와 같은 패턴.
    /// 바나나 명중(ServerOnBananaHit)·BananaProjectile은 그대로. _nextBananaTime(서버 전용)은 미리 만든 객체 필드(옛 PlayerCombat 필드처럼 유지).
    /// </summary>
    internal sealed class MonkeyBananaSkill : AbsorbSkillBase
    {
        private readonly MonkeySkillConfig _cfg;
        private float _nextBananaTime; // 서버 전용 — Command 연타 방지

        public MonkeyBananaSkill(AbsorbSkillContext ctx, MonkeySkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.MonkeyBanana)
        {
            _cfg = cfg;
        }

        /// <summary>원격 판정 요청 검증: 어떤 판정 요청도 받지 않는다(옛 switch — 원숭이는 Swing 거부, 바나나 판정은 서버 투사체).</summary>
        public override bool AllowsHit(PlayerCombat.HitKind kind) => false;

        /// <summary>옛 PerformSingleSwing의 원숭이 분기. 끝의 IsAttacking=false·yield break는 PlayerCombat의 TryReplaceAttack 공통 처리.</summary>
        public override bool TryReplaceAttack(Vector2 facing, out IEnumerator routine)
        {
            routine = ThrowRoutine(facing);
            return true;
        }

        private IEnumerator ThrowRoutine(Vector2 facing)
        {
            ctx.Combat.SendThrowBanana(facing); // [Command] CmdThrowBanana(PlayerCombat) — 호스트도 Command 경로(옛 코드와 같음)
            yield return new WaitForSeconds(ctx.Combat.AttackSwingDuration);
        }

        /// <summary>서버(CmdThrowBanana 본문): dir 방향으로 바나나를 스폰한다. 연결 소유 없이 스폰해 층 이동 정리에 같이 걸린다.
        /// 원숭이 여부는 호출부(Host.Current)가 이미 걸렀다.</summary>
        public override void ServerOnAttackCommand(Vector2 dir)
        {
            if (_cfg.bananaProjectilePrefab == null || (ctx.Health != null && ctx.Health.IsDead) || Time.time < _nextBananaTime)
            {
                return;
            }

            PlayerCombat combat = ctx.Combat;
            _nextBananaTime = Time.time + (combat.AttackSwingDuration + combat.AttackRecoveryDelay) * 0.8f; // 기본 공격 주기 — 약간 여유(지연 흔들림)
            Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
            GameObject go = Object.Instantiate(_cfg.bananaProjectilePrefab, (Vector2)ctx.Transform.position + d * 0.5f, Quaternion.identity);
            var banana = go.GetComponent<BananaProjectile>();
            if (banana != null)
            {
                banana.Initialize(d, ctx.Controller, _cfg.bananaDamageScale); // 소유자 = PlayerController(BananaProjectile → _owner.Combat.ServerOnBananaHit)
            }

            NetworkServer.Spawn(go);
        }
    }
}
