using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 대시 먼지(2026-10-08, 사용자 에셋 Pimen "Smoke n Dust 03" — https://pimen.itch.io/smoke-n-dust-03, 표기 의무 없음).
    /// 대시를 시작한 자리에 옆으로 차는 큰 먼지(`dash_dust_kick`, 64×64)가 한 번 퍼지고, 대시하는 동안 지나간 발밑에 작은 먼지
    /// (`dash_dust_trail`, 32×32)가 이어서 남는다. 원본이 바닥에 묻혀 안 보여 `Art_Source/fx/brighten_dash_dust.py`로 밝게 + 1px 외곽선.
    /// 순수 표시 전용(판정 없음). 각 화면이 로컬로 그린다: 대시한 본인은 바로, 다른 화면은 PlayerDash.RpcDashDust로.
    /// 그림은 오른쪽으로 갈 때 뒤(왼쪽)로 퍼지는 먼지라, 왼쪽으로 대시할 때만 좌우를 뒤집는다(피벗 = 먼지가 처음 생기는 발 자리).
    /// </summary>
    public class DashDustFx : MonoBehaviour
    {
        private const float FeetOffsetY = -0.42f;   // 슬라임 몸 바닥(그림 가운데에서 13~14px 아래)
        private const float TrailInterval = 0.05f;  // [PLAYTEST] 꼬리 먼지 간격
        private const float TrailScale = 0.6f;
        private const int SortingOrderBelowBody = 9; // 몸(10) 아래

        private Sprite[] _trailFrames;
        private Transform _target;
        private bool _flip;
        private int _sortingLayerId;
        private float _trailRemaining;
        private float _trailTimer;

        public static void Play(Transform target, Vector2 direction, float duration, Sprite[] kickFrames, Sprite[] trailFrames)
        {
            if (target == null)
            {
                return;
            }

            var go = new GameObject("DashDust");
            var fx = go.AddComponent<DashDustFx>();
            fx._trailFrames = trailFrames != null && trailFrames.Length > 0 ? trailFrames : null;
            fx._target = target;
            fx._flip = direction.x < -0.01f;
            var body = target.GetComponent<SpriteRenderer>();
            fx._sortingLayerId = body != null ? body.sortingLayerID : 0;
            fx._trailRemaining = fx._trailFrames != null ? duration : 0f;
            fx._trailTimer = TrailInterval;
            if (kickFrames != null && kickFrames.Length > 0)
            {
                fx.Spawn(kickFrames, 1f, 1f);
            }
        }

        private void Update()
        {
            if (_trailRemaining <= 0f || _target == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            _trailRemaining -= dt;
            _trailTimer -= dt;
            if (_trailTimer <= 0f)
            {
                _trailTimer = TrailInterval;
                Spawn(_trailFrames, TrailScale, 0.9f);
            }
        }

        private void Spawn(Sprite[] frames, float scale, float alpha)
        {
            var go = new GameObject("DashDustPuff");
            go.transform.position = _target.position + new Vector3(0f, FeetOffsetY, 0f);
            go.transform.localScale = Vector3.one * scale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID = _sortingLayerId;
            renderer.sortingOrder = SortingOrderBelowBody;
            renderer.flipX = _flip;
            renderer.color = new Color(1f, 1f, 1f, alpha);
            var puff = go.AddComponent<DashDustPuff>();
            puff.Init(frames, renderer);
        }
    }

    /// <summary>먼지 한 덩이: 프레임을 한 번 재생하고 사라진다(빈 프레임은 건너뜀).</summary>
    public class DashDustPuff : MonoBehaviour
    {
        private const float FrameTime = 0.05f;
        private Sprite[] _frames;
        private SpriteRenderer _renderer;
        private int _count;
        private float _elapsed;

        public void Init(Sprite[] frames, SpriteRenderer renderer)
        {
            _frames = frames;
            _renderer = renderer;
            _count = frames.Length;
            _renderer.sprite = frames[0];
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            int index = (int)(_elapsed / FrameTime);
            if (index >= _count)
            {
                Destroy(gameObject);
                return;
            }

            _renderer.sprite = _frames[index];
        }
    }
}
