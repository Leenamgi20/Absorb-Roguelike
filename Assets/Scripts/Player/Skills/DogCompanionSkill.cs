using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 개 동료(단계 5c, 옛 PlayerController.ServerUpdateDogCompanion·OnStopServer 본문): 개 특성을 들고 살아 있으면 동료가 있어야 하고, 아니면 없어야 한다.
    /// 특성 교체·피격 방출·분해·디버그 해제 → 제거, 사망 → 제거 — 제거는 OnUnequip이 아니라 다음 서버 틱(옛 매 프레임 판정 타이밍 그대로).
    /// 층 이동 때는 AdvanceFloor가 소유 없는 오브젝트를 전부 치우므로 _dog가 null로 판정되어 다음 프레임에 새 층(플레이어 옆)에서 다시 스폰된다.
    /// 동료의 행동·물기(DogCompanion)는 그대로. _dog는 서버 전용 객체 필드.
    /// </summary>
    internal sealed class DogCompanionSkill : AbsorbSkillBase
    {
        private readonly DogSkillConfig _cfg;
        private DogCompanion _dog; // 서버 전용

        public DogCompanionSkill(AbsorbSkillContext ctx, DogSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.DogCompanion)
        {
            _cfg = cfg;
        }

        /// <summary>서버 매 프레임(옛 ServerUpdateDogCompanion). equipped = 지금 장착한 스킬이 개인가(옛 '_absorbSkill == DogCompanion').</summary>
        public override void TickServer(bool equipped)
        {
            bool want = equipped && _cfg.dogCompanionPrefab != null
                        && (ctx.Health == null || !ctx.Health.IsDead);
            if (want && _dog == null)
            {
                GameObject go = Object.Instantiate(_cfg.dogCompanionPrefab, ctx.Transform.position + new Vector3(0.8f, -0.3f, 0f), Quaternion.identity);
                _dog = go.GetComponent<DogCompanion>();
                if (_dog != null)
                {
                    _dog.Initialize(ctx.Controller, _cfg.dogBiteDamageScale);
                }

                NetworkServer.Spawn(go);
            }
            else if (!want && _dog != null)
            {
                NetworkServer.Destroy(_dog.gameObject);
                _dog = null;
            }
        }

        /// <summary>옛 PlayerController.OnStopServer 본문(서버 정지 — 모든 스킬 객체에 Host가 부른다).</summary>
        public override void OnStopServer()
        {
            if (_dog != null && NetworkServer.active)
            {
                NetworkServer.Destroy(_dog.gameObject);
            }

            _dog = null;
        }
    }
}
