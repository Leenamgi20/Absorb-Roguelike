using AbsorbRoguelike.Monster;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 토끼 귀 채찍(단계 5a, 결정 ㉑A): 공통 스윙 흐름(스윙 시간·대검 휘두르기·자동 연속·스윙 1회당 회복)은 그대로 타고, 스윙 판정 종류만
    /// EarWhip(긴 직선 + 거리별 배율)으로, 연출은 부채꼴·베기 프레임 대신 귀 직선으로 바꾼다(ModifyAttack). 로컬 사거리 링·귀 직선 이펙트
    /// 렌더러는 이 객체가 들고 있다(AbsorbSkillHost가 4종 객체를 계속 보관 — 스킬이 바뀌어도 링 숨김·이펙트 끝까지 재생이 기존과 같다).
    /// </summary>
    internal sealed class RabbitEarWhipSkill : AbsorbSkillBase
    {
        private readonly RabbitSkillConfig _cfg;

        // ── 토끼 귀 채찍 연출(판정과 무관) ──────────────────────────────────────────────
        private SpriteRenderer _earWhipRingRenderer;
        private string _earWhipRingKey;
        private SpriteRenderer _earWhipVfxRenderer;
        private float _earWhipVfxElapsed = -1f; // <0이면 재생 중 아님
        private static Sprite _whitePixelSprite;

        public RabbitEarWhipSkill(AbsorbSkillContext ctx, RabbitSkillConfig cfg)
            : base(ctx, cfg, AbsorbRoguelike.Absorption.AbsorbSkill.RabbitEarWhip)
        {
            _cfg = cfg;
        }

        public override void ModifyAttack(ref AttackSpec spec)
        {
            spec.SwingHitKind = PlayerCombat.HitKind.EarWhip;
            spec.UseSkillSwingFx = true;
        }

        public override bool AllowsHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.EarWhip;

        public override bool HandlesServerHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.EarWhip;

        public override bool ServerPerformSkillHit(PlayerCombat.HitKind kind, Vector2 facing, PlayerCombat.HitSession session)
        {
            return DoEarWhipHit(facing);
        }

        /// <summary>
        /// 토끼 귀 채찍(2026-09-28 개편, 결정 ㉑A, 서버 전용, 옛 PlayerCombat.DoEarWhipHit 그대로): facing 방향 길이 earWhipLength·폭 earWhipWidth
        /// 직선(박스) 판정으로 대상별 1회씩 관통한다. 플레이어 중심 → 대상 콜라이더 최근접점 거리 d로 구간을 나눈다 —
        /// d ≥ length - tip: 끝부분(×earWhipTipDamageScale, 경직 earWhipTipStunDuration, 팝업 EarWhipTip),
        /// d &lt; closeRange: 근접(×earWhipCloseDamageScale, 팝업 EarWhipClose), 나머지: 중간(×earWhipMidDamageScale, Normal).
        /// 대미지 식·적중 회복(dealtHit)·속성·보스 규칙은 DoConeHit와 같다(항상 흡수 스킬 공격 → 보스 정상 대미지).
        /// 넉백: 기본 공격과 같은 baseKnockbackScale을 전 구간에 준다 — 밀려난 적이 끝부분 거리로 다시 들어오게 돼 거리 유지 플레이와
        /// 어긋나지 않고, 끝부분 경직(0.4초)과 겹쳐도 문제없다. [PLAYTEST] 끝부분 0 넉백으로 바꿀지 플레이 후 판단.
        /// 살아있는 적(보스 포함)을 하나라도 실제로 깎았으면 true.
        /// </summary>
        private bool DoEarWhipHit(Vector2 facing)
        {
            if (!ctx.IsServer)
            {
                return false; // 서버 전용(ServerPerformHit EarWhip)
            }

            PlayerCombat combat = ctx.Combat;
            GameObject self = combat.gameObject;
            var alreadyHit = new System.Collections.Generic.HashSet<GameObject>();
            bool dealtHit = false;
            float knockback = combat.BaseKnockbackScale;

            Vector2 origin = combat.HitOrigin;
            Vector2 center = origin + facing * (_cfg.earWhipLength * 0.5f);
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f; // 박스 세로축(y) = facing
            float tipStart = _cfg.earWhipLength - _cfg.earWhipTipLength;

            foreach (Collider2D hit in Physics2D.OverlapBoxAll(center, new Vector2(_cfg.earWhipWidth, _cfg.earWhipLength), angle, combat.AttackHitLayers))
            {
                if (hit.gameObject == self || !alreadyHit.Add(hit.gameObject))
                {
                    continue; // 자기 자신, 콜라이더 여러 개로 중복된 대상 제외
                }

                float d = Vector2.Distance(origin, hit.ClosestPoint(origin));
                bool tip = d >= tipStart;
                bool close = !tip && d < _cfg.earWhipCloseRange;
                float scale = tip ? _cfg.earWhipTipDamageScale : close ? _cfg.earWhipCloseDamageScale : _cfg.earWhipMidDamageScale;
                AbsorbRoguelike.UI.DamagePopupKind kind = tip ? AbsorbRoguelike.UI.DamagePopupKind.EarWhipTip
                    : close ? AbsorbRoguelike.UI.DamagePopupKind.EarWhipClose
                    : AbsorbRoguelike.UI.DamagePopupKind.Normal;
                int damage = combat.ComputeSkillDamage(scale); // 옛 Max(1, RoundToInt(baseDamage * (1 + 분해 버프) * scale))와 같은 식

                // 잡몹/보스 공통 처리(리팩터 B안 단계 1, HitResolver) — 구간 팝업(kind), 속성 1회, 넉백 baseKnockbackScale 배율(> 0일 때).
                // 끝부분 경직은 기존처럼 '살아 있을 때 기록 직후·넉백 전'에 훅으로 실행한다.
                var info = new AbsorbRoguelike.Combat.HitInfo(damage, origin, combat.netIdentity, DamageSource.AbsorbSkill, kind);
                var options = new AbsorbRoguelike.Combat.HitOptions
                {
                    ElementApplications = 1,
                    ApplyElementToBoss = true,
                    RecordHit = true,
                    Knockback = AbsorbRoguelike.Combat.KnockbackMode.Scale,
                    KnockbackValue = knockback,
                };
                var result = AbsorbRoguelike.Combat.HitResolver.Resolve(hit.gameObject, info, options,
                    (t, isBoss) => combat.ApplyElementOnHit(t, damage, isBoss, true), combat.OnEnemyHitServer,
                    behavior =>
                    {
                        if (tip && behavior != null)
                        {
                            behavior.ApplyStun(_cfg.earWhipTipStunDuration);
                        }
                    });

                if (result.TargetKind == AbsorbRoguelike.Combat.HitTargetKind.Monster)
                {
                    dealtHit |= result.Damaged; // 적중 회복은 체력이 실제로 깎였을 때만(2026-09-28)
                    continue;
                }

                if (result.TargetKind == AbsorbRoguelike.Combat.HitTargetKind.Boss)
                {
                    dealtHit |= result.Damaged; // 개인 할당량 초과분 제외(DoConeHit와 같은 규칙)
                    if (tip)
                    {
                        var bossAttack = hit.GetComponent<BossBrain>();
                        if (bossAttack != null)
                        {
                            bossAttack.ApplyStun(_cfg.earWhipTipStunDuration); // 처치된 보스는 ApplyStun 안에서 무시
                        }
                    }
                }
            }

            return dealtHit;
        }

        /// <summary>
        /// 끝부분 사거리 링(결정 ㉑A 가독성, 옛 PlayerCombat.UpdateEarWhipRing): 로컬 플레이어가 귀 채찍을 장착하고 있을 때만 켠다.
        /// AbsorbSkillHost가 매 LateUpdate 장착 여부(equipped)를 넘기므로 흡수 교체·피격 드롭·분해·디버그 메뉴 동물 버튼 어느 경로로 바뀌어도
        /// 바로 따라간다. 텍스처는 파라미터 조합이 바뀔 때만 다시 만든다.
        /// </summary>
        public override void LateTickLocal(bool equipped)
        {
            if (!equipped)
            {
                if (_earWhipRingRenderer != null && _earWhipRingRenderer.enabled)
                {
                    _earWhipRingRenderer.enabled = false;
                }

                return;
            }

            if (_earWhipRingRenderer == null)
            {
                var go = new GameObject("EarWhipRangeRing");
                go.transform.SetParent(ctx.Transform, false);
                _earWhipRingRenderer = go.AddComponent<SpriteRenderer>();
                _earWhipRingRenderer.sortingOrder = 3; // 바닥 위, 캐릭터(10) 아래
            }

            string key = $"{_cfg.earWhipLength}|{_cfg.earWhipTipLength}|{_cfg.earWhipCloseRange}|{_cfg.earWhipRingColor}|{_cfg.earWhipCloseRingColor}";
            if (key != _earWhipRingKey)
            {
                if (_earWhipRingRenderer.sprite != null)
                {
                    Object.Destroy(_earWhipRingRenderer.sprite.texture);
                    Object.Destroy(_earWhipRingRenderer.sprite);
                }

                _earWhipRingRenderer.sprite = CreateEarWhipRingSprite(_cfg.earWhipLength, _cfg.earWhipLength - _cfg.earWhipTipLength, _cfg.earWhipCloseRange, _cfg.earWhipRingColor, _cfg.earWhipCloseRingColor);
                _earWhipRingKey = key;
            }

            _earWhipRingRenderer.transform.localPosition = Vector3.zero;
            _earWhipRingRenderer.transform.rotation = Quaternion.identity;
            _earWhipRingRenderer.enabled = true;
        }

        // 반지름 inner~outer 금색 띠 + 반지름 closeRadius 얇은 원(1px). PPU 16, 피벗 = 중심.
        private static Sprite CreateEarWhipRingSprite(float outer, float inner, float closeRadius, Color ringColor, Color closeColor)
        {
            const float ppu = 16f;
            int size = Mathf.CeilToInt(outer * ppu * 2f) + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float c = size * 0.5f;
            float outerPx = outer * ppu, innerPx = Mathf.Max(0f, inner * ppu), closePx = closeRadius * ppu;
            Color32 ring = ringColor, closeRing = closeColor, clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    Color32 col = clear;
                    if (r >= innerPx && r <= outerPx)
                    {
                        col = ring;
                    }
                    else if (closeRadius > 0f && Mathf.Abs(r - closePx) <= 0.5f)
                    {
                        col = closeRing;
                    }

                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        }

        /// <summary>귀 직선 이펙트 시작(옛 PlayerCombat.ShowEarWhipVfx) — 흰 막대가 facing 방향으로 뻗었다가 페이드한다. 오브젝트 하나를 재사용하고
        /// LateUpdate 타이머로 돌리므로 가드로 공격 코루틴이 끊겨도 남는 게 없다(0.12초 뒤 알아서 숨는다).</summary>
        public override void ShowAttackFx(Vector2 facing)
        {
            if (_earWhipVfxRenderer == null)
            {
                var go = new GameObject("EarWhipVfx");
                go.transform.SetParent(ctx.Transform, false);
                _earWhipVfxRenderer = go.AddComponent<SpriteRenderer>();
                _earWhipVfxRenderer.sprite = WhitePixelSprite;
                _earWhipVfxRenderer.sortingOrder = 11; // 캐릭터(10) 바로 위
            }

            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            _earWhipVfxRenderer.transform.localPosition = Vector3.zero;
            _earWhipVfxRenderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            _earWhipVfxElapsed = 0f;
            LateTickVisual(0f);
        }

        // 앞 40%: 길이 0 → earWhipLength로 뻗기, 뒤 60%: 알파 1 → 0 페이드. 끝나면 숨긴다(옛 PlayerCombat.UpdateEarWhipVfx).
        public override void LateTickVisual(float dt)
        {
            if (_earWhipVfxRenderer == null || _earWhipVfxElapsed < 0f)
            {
                return;
            }

            _earWhipVfxElapsed += dt;
            float duration = Mathf.Max(0.01f, _cfg.earWhipVfxDuration);
            float t = _earWhipVfxElapsed / duration;
            if (t >= 1f)
            {
                _earWhipVfxRenderer.enabled = false;
                _earWhipVfxElapsed = -1f;
                return;
            }

            const float extendPortion = 0.4f;
            float lengthT = Mathf.Clamp01(t / extendPortion);
            float alpha = t <= extendPortion ? 1f : 1f - (t - extendPortion) / (1f - extendPortion);
            _earWhipVfxRenderer.transform.localScale = new Vector3(_cfg.earWhipLength * lengthT, _cfg.earWhipWidth, 1f);
            _earWhipVfxRenderer.color = new Color(1f, 1f, 1f, alpha * 0.85f);
            _earWhipVfxRenderer.enabled = true;
        }

        // 1×1 흰 픽셀, PPU 1, 피벗 = 왼쪽 가운데 → localScale.x가 곧 길이(칸), localScale.y가 폭(칸).
        private static Sprite WhitePixelSprite
        {
            get
            {
                if (_whitePixelSprite == null)
                {
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    _whitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
                }

                return _whitePixelSprite;
            }
        }
    }
}
