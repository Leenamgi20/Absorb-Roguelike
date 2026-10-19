using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 흡수·방출 이펙트(2026-10-07). 순수 표시 전용, 각 화면 로컬.
    /// - Stream: 흡수가 성사되는 순간 대상 자리에서 정령 빛 알갱이 18개가 휘어지며 플레이어에게 빨려 든다(AbsorptionController.RpcAbsorbStream).
    /// - Puff: 탈(흡수 특성)이 생기거나 바뀌는 순간 몸에서 "퐁" 연기 4프레임(AbsorptionController 동기화 훅).
    /// - Emit: 특성을 잃는 순간(피격 방출 등) 금빛 별 6개가 몸에서 사방으로 튀고 회색 연기(같은 훅, 새 값이 없음일 때).
    /// 그림: `Art_Source/fx/draw_absorb_fx.py` → `Resources/Fx/absorb_pellet_0~1`, `absorb_puff_0~3`, `emit_star`.
    /// 대상 자식이 아닌 독립 오브젝트라 흡수로 사라지는 몬스터와 무관하게 끝까지 재생된다.
    /// </summary>
    public class AbsorbFx : MonoBehaviour
    {
        private const int SortingOrder = 23; // 명중 스파크(24)·대미지 숫자(25) 아래, 캐릭터 위
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        private enum Kind { Stream, Puff, Emit }

        private Kind _kind;
        private Transform _target;
        private Vector3 _origin;
        private float _elapsed;
        private readonly List<SpriteRenderer> _parts = new List<SpriteRenderer>();
        private readonly List<Vector3> _bend = new List<Vector3>();   // Stream: 곡선 조절점 / Emit: 날아갈 방향·거리
        private readonly List<float> _delay = new List<float>();
        private SpriteRenderer _puff;

        // 2026-10-07 사용자 피드백 "더 크게, 눈에 잘 보이게": 알갱이 2배 크기·18개·0.5초, 연기·별 1.6배.
        private const int StreamCount = 18;
        private const float StreamTravel = 0.5f;
        private const float StreamStagger = 0.025f;
        private const float StreamScale = 2f;
        private const float PuffScale = 1.6f;
        private const float EmitScale = 1.6f;
        private const float PuffFrameTime = 0.06f;
        private const int EmitCount = 6;
        private const float EmitDuration = 0.45f;

        private static Sprite Load(string name)
        {
            if (!Cache.TryGetValue(name, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>("Fx/" + name);
                Cache[name] = sprite;
            }

            return sprite;
        }

        private static AbsorbFx Create(string name, Kind kind, Vector3 origin, Transform target)
        {
            var go = new GameObject(name);
            go.transform.position = origin;
            var fx = go.AddComponent<AbsorbFx>();
            fx._kind = kind;
            fx._origin = origin;
            fx._target = target;
            return fx;
        }

        private SpriteRenderer AddPart(Sprite sprite)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = SortingOrder;
            _parts.Add(r);
            return r;
        }

        public static void Stream(Vector3 from, Transform to)
        {
            Sprite big = Load("absorb_pellet_0");
            Sprite small = Load("absorb_pellet_1");
            if (to == null || big == null)
            {
                return;
            }

            var fx = Create("AbsorbStream", Kind.Stream, from, to);
            Vector3 dir = to.position - from;
            Vector3 side = new Vector3(-dir.y, dir.x, 0f).normalized;
            for (int i = 0; i < StreamCount; i++)
            {
                var r = fx.AddPart(i % 3 == 0 ? big : small);
                VisualFx.ApplyGlow(r, GlowKind.Absorb); // 흡수 알갱이 발광(블룸)
                r.enabled = false;
                // 시작점 근처에 흩어 두고, 중간 조절점을 좌우로 휘게 해 소용돌이처럼 빨려 들게 한다.
                fx._bend.Add(from + (Vector3)(Random.insideUnitCircle * 0.5f) + side * Random.Range(-1.6f, 1.6f) + dir * 0.4f);
                fx._delay.Add(i * StreamStagger);
                r.transform.position = from + (Vector3)(Random.insideUnitCircle * 0.3f);
            }
        }

        public static void Puff(Transform at)
        {
            if (at == null || Load("absorb_puff_0") == null)
            {
                return;
            }

            var fx = Create("AbsorbPuff", Kind.Puff, at.position, at);
            fx._puff = fx.AddPart(Load("absorb_puff_0"));
            fx.transform.localScale = Vector3.one * PuffScale;
        }

        public static void Emit(Vector3 position)
        {
            Sprite star = Load("emit_star");
            if (star == null)
            {
                return;
            }

            var fx = Create("TraitEmit", Kind.Emit, position, null);
            for (int i = 0; i < EmitCount; i++)
            {
                var starRenderer = fx.AddPart(star);
                starRenderer.transform.localScale = Vector3.one * EmitScale;
                VisualFx.ApplyGlow(starRenderer, GlowKind.Absorb); // 방출 금빛 별 발광(회색 연기는 그대로)
                float a = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / EmitCount;
                fx._bend.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * Random.Range(0.7f, 1.0f));
            }

            var puff = Create("TraitEmitPuff", Kind.Puff, position, null); // 회색 연기
            puff._puff = puff.AddPart(Load("absorb_puff_0"));
            puff.transform.localScale = Vector3.one * PuffScale;
            puff._puff.color = new Color(0.75f, 0.75f, 0.8f, 1f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            switch (_kind)
            {
                case Kind.Stream:
                    TickStream();
                    break;
                case Kind.Puff:
                    TickPuff();
                    break;
                case Kind.Emit:
                    TickEmit();
                    break;
            }
        }

        private void TickStream()
        {
            Vector3 end = _target != null ? _target.position : _origin;
            bool alive = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                float t = (_elapsed - _delay[i]) / StreamTravel;
                var r = _parts[i];
                if (t < 0f)
                {
                    alive = true;
                    continue;
                }

                if (t >= 1f)
                {
                    r.enabled = false;
                    continue;
                }

                alive = true;
                float e = t * t; // 처음엔 느리게, 끝에서 빨려 들듯 빠르게
                if (!r.enabled)
                {
                    r.enabled = true;
                }

                Vector3 a = Vector3.Lerp(_origin, _bend[i], e);
                Vector3 b = Vector3.Lerp(_bend[i], end, e);
                r.transform.position = Vector3.Lerp(a, b, e);
                r.transform.localScale = Vector3.one * (StreamScale * Mathf.Lerp(1f, 0.6f, t));
            }

            if (!alive)
            {
                Destroy(gameObject);
            }
        }

        private void TickPuff()
        {
            int frame = (int)(_elapsed / PuffFrameTime);
            if (frame >= 4)
            {
                Destroy(gameObject);
                return;
            }

            if (_target != null)
            {
                transform.position = _target.position;
            }

            _puff.sprite = Load("absorb_puff_" + frame);
        }

        private void TickEmit()
        {
            float t = _elapsed / EmitDuration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float ease = 1f - (1f - t) * (1f - t);
            for (int i = 0; i < _parts.Count; i++)
            {
                Vector3 offset = _bend[i] * ease + Vector3.up * (0.35f * Mathf.Sin(t * Mathf.PI)); // 살짝 솟았다 퍼짐
                _parts[i].transform.position = _origin + offset;
                _parts[i].transform.rotation = Quaternion.Euler(0f, 0f, t * 360f * (i % 2 == 0 ? 1f : -1f));
                _parts[i].color = new Color(1f, 1f, 1f, t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f);
            }
        }
    }
}
