using AbsorbRoguelike.Abilities;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 명중 이펙트 진입점(2026-10-07). 순수 표시 전용 — 각 화면이 로컬로만 만든다.
    /// - 직접 명중(무기·흡수 스킬·뱉기·귀 채찍): 맞은 대상이 한 프레임 하얗게 번쩍이고(HitWhiteFlash), 몸 위에 스파크(HitSparkPlayer).
    ///   호출부: MonsterStub/BossStub의 대미지 숫자 경로(호스트는 바로, 원격은 RpcShowDamage) — 새 RPC 없음.
    ///   귀 채찍 끝부분 강타는 큰 금색 스파크, 근접 약타는 작은 기본 스파크(예전 HitFlash 별을 대체).
    /// - 속성이 걸리는 순간: StatusEffectReceiver가 동기화된 스택이 오르는 걸 보고 속성 색 스파크를 띄운다.
    /// - 효과음(2026-10-14): 직접 명중 = sfx_hit(귀 채찍 끝 sfx_hit_strong) + 잡몹이면 sfx_monster_hurt. 속성 효과음은 StatusEffectReceiver가 '새로 걸림'에만.
    /// 그림: `Art_Source/fx/draw_hit_spark.py` → `Resources/Fx/hit_&lt;세트&gt;_0~3.png`(32×32, 4프레임).
    /// </summary>
    public static class HitFx
    {
        public static void OnDirectHit(GameObject target, DamagePopupKind kind)
        {
            if (target == null || !kind.IsDirectHit())
            {
                return;
            }

            HitWhiteFlash.Flash(target, shake: true);
            var local = Mirror.NetworkClient.localPlayer != null ? Mirror.NetworkClient.localPlayer.GetComponent<AbsorbRoguelike.Player.PlayerController>() : null;
            if (local != null)
            {
                local.Combat.NotifyNearbyHit(target.transform.position); // 내 스윙이면 히트스톱
            }
            Vector3 pos = BodyCenter(target) + (Vector3)(Random.insideUnitCircle * 0.15f);

            // 효과음(2026-10-14): 여기는 호스트는 서버 경로, 원격은 RpcShowDamage에서 한 번씩만 불린다 → 화면마다 1회.
            // 귀 채찍 끝부분 = 강타, 그 외 = 일반 명중. 잡몹이면 '끽' 피격음을 겹친다(보스는 명중음만).
            AbsorbRoguelike.Audio.Sfx.Play(kind == DamagePopupKind.EarWhipTip ? AbsorbRoguelike.Audio.SfxId.HitStrong : AbsorbRoguelike.Audio.SfxId.Hit, pos);
            if (target.GetComponent<AbsorbRoguelike.Monster.MonsterStub>() != null)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.MonsterHurt, pos);
            }

            switch (kind)
            {
                case DamagePopupKind.EarWhipTip:
                    HitSparkPlayer.Spawn(pos, "gold", 1.4f);
                    break;
                case DamagePopupKind.EarWhipClose:
                    HitSparkPlayer.Spawn(pos, "base", 0.7f);
                    break;
                default:
                    HitSparkPlayer.Spawn(pos, "base", 1f);
                    break;
            }
        }

        public static void OnElementApplied(GameObject target, ElementType element, float scale = 0.8f)
        {
            if (target == null || element == ElementType.None)
            {
                return;
            }

            Vector3 pos = BodyCenter(target) + (Vector3)(Random.insideUnitCircle * 0.25f);
            HitSparkPlayer.Spawn(pos, SetName(element), scale);
        }

        private static string SetName(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return "fire";
                case ElementType.Ice: return "ice";
                case ElementType.Electric: return "electric";
                case ElementType.Poison: return "poison";
                case ElementType.Earth: return "earth";
                default: return "base";
            }
        }

        // 몸 그림의 가운데(보스 96px 그림은 아래쪽 여백이 적어 bounds 중심 = 몸 중심 근처).
        private static Vector3 BodyCenter(GameObject target)
        {
            var body = target.GetComponent<SpriteRenderer>();
            return body != null && body.sprite != null ? body.bounds.center : target.transform.position;
        }
    }
}
