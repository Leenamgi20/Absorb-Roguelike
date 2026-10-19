using AbsorbRoguelike.Absorption;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 슬라임 몸 말랑 애니메이션(2026-10-07, 사용자 결정 A — 그림 추가 없이 코드로 늘이고 누르기). 기본 슬라임·탈 12종 모두 같은 규칙.
    /// - 이동: 통통 튀며 걷기(최대 2px 뜀, 착지 순간 납작·공중에선 살짝 길쭉). 기울기는 2026-10-07 사용자 피드백(애매함)으로 뺐다.
    /// - 멈춤: 천천히 숨쉬듯 부풀었다 줄어듦.
    /// - 흡수 홀드(AbsorptionController.IsAbsorbHolding, 모든 화면 동기화): 바라보는 쪽으로 늘어나며 떨리고, 앞쪽에서 바람 줄기가 몸으로 빨려 듦.
    /// - 탈을 얻는 순간(Gulp): "꿀꺽" — 크게 부풀었다 찌그러진 뒤 돌아옴.
    /// - 맞는 순간(히트스톱 "멈칫", 2026-10-07): 동기화된 체력이 줄면 0.12초 동안 1px씩 떨며 살짝 납작해짐(모든 화면).
    /// 구현: 루트 SpriteRenderer는 그대로 두고(색·스프라이트를 다른 코드가 쓰므로) forceRenderingOff로 숨긴 뒤, 자식 "BodyVisual"이
    /// 매 프레임 루트의 스프라이트·색·뒤집기·순서를 복사해 그리고 이 자식만 늘이고 누른다(콜라이더·무기 위치는 영향 없음).
    /// 발이 바닥에 붙어 보이도록 늘인 만큼 아래쪽을 맞춘다. 늘이고 누르는 폭은 도트가 덜 뭉개지게 10% 안팎. 수치 [PLAYTEST].
    /// 원격 플레이어는 물리 속도가 0이라 위치 변화로 속도를 잰다.
    /// 외곽선(2026-10-15, 비주얼 3단계 V8): 루트에 VisualFx.ApplyOutline(Player) — 복사 때 재질도 따라가 BodyVisual에 흰 1px 외곽선.
    /// </summary>
    [DefaultExecutionOrder(1000)] // PlayerTalSkin·가드 색 등 루트 렌더러를 바꾸는 코드 뒤에 복사
    [RequireComponent(typeof(SpriteRenderer))]
    public class SlimeBodyAnimator : MonoBehaviour
    {
        [SerializeField] private float hopsPerSecond = 3.2f;
        [SerializeField] private float hopHeightPixels = 2f;
        [SerializeField] private float squash = 0.1f;
        [SerializeField] private float breathPeriod = 1.6f;
        [SerializeField] private float breathAmount = 0.03f;
        [SerializeField] private float absorbStretch = 0.12f;
        [SerializeField] private float gulpDuration = 0.32f;
        [SerializeField] private float movingSpeedThreshold = 0.3f;
        [SerializeField] private float fullSpeed = 4.5f;

        private SpriteRenderer _root;
        private SpriteRenderer _visual;
        private AbsorptionController _absorption;
        private PlayerController _player;
        private Vector3 _lastPosition;
        private float _speed;
        private float _hopPhase;
        private float _gulpRemaining;
        private float _streakTimer;
        private Vector2 _moveDir = Vector2.right;
        private Sprite _streakSprite;
        private PlayerHealth _health;
        private int _lastHealth = -1;
        private float _hurtRemaining;
        private const float HurtShake = 0.12f;

        private void Awake()
        {
            _root = GetComponent<SpriteRenderer>();
            _absorption = GetComponent<AbsorptionController>();
            _player = GetComponent<PlayerController>();
            var go = new GameObject("BodyVisual");
            go.transform.SetParent(transform, false);
            _visual = go.AddComponent<SpriteRenderer>();
            _root.forceRenderingOff = true;
            // 3단계 V8 외곽선(2026-10-15): 로컬·원격 모든 플레이어 루트에 흰 1px 외곽선 재질 — CopyRenderer가 sharedMaterial을 BodyVisual로 복사한다.
            // 외곽선은 셰이더에서 텍셀 단위로 그려 늘이기·기울기 배율에도 도트와 같이 늘어난다(따로 굵어지거나 잘리지 않음).
            AbsorbRoguelike.UI.VisualFx.ApplyOutline(_root, AbsorbRoguelike.UI.OutlineKind.Player);
            _lastPosition = transform.position;
            _streakSprite = Resources.Load<Sprite>("Fx/absorb_streak");
            _health = GetComponent<PlayerHealth>();
        }

        /// <summary>탈을 얻은 순간의 "꿀꺽"(AbsorptionController 동기화 훅에서 부른다 — 모든 화면).</summary>
        public void Gulp()
        {
            _gulpRemaining = gulpDuration;
        }

        private void LateUpdate()
        {
            CopyRenderer();
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            float instant = delta.magnitude / dt;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-15f * dt)); // 원격 위치 보간의 들쭉날쭉을 다듬는다
            if (delta.sqrMagnitude > 0.000001f)
            {
                _moveDir = ((Vector2)delta).normalized;
            }

            bool moving = _speed > movingSpeedThreshold;
            bool holding = _absorption != null && _absorption.IsAbsorbHolding;
            float sx = 1f, sy = 1f, lift = 0f;
            Vector2 shake = Vector2.zero;

            if (holding)
            {
                // 흡수 홀드: 바라보는 쪽으로 늘어나며 떨림(가로세로 중 바라보는 축으로 늘이고 다른 축은 줄인다)
                Vector2 facing = _player != null ? _player.FacingDirection.normalized : Vector2.right;
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 40f);
                float s = absorbStretch * pulse;
                float horizontal = Mathf.Abs(facing.x);
                sx = 1f + s * horizontal - s * 0.5f * (1f - horizontal);
                sy = 1f + s * (1f - horizontal) - s * 0.5f * horizontal;
                shake = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) / 32f;
                SpawnStreaks(facing, dt);
                _hopPhase = 0f;
            }
            else if (moving)
            {
                float rate = hopsPerSecond * Mathf.Clamp(_speed / fullSpeed, 0.6f, 1.3f);
                _hopPhase += dt * rate;
                float wave = Mathf.Abs(Mathf.Sin(_hopPhase * Mathf.PI)); // 0 = 착지, 1 = 꼭대기
                lift = wave * hopHeightPixels / 32f;
                float land = 1f - Mathf.Clamp01(wave * 3f); // 착지 근처에서만 1
                sy = 1f - squash * land + squash * 0.5f * wave;
                sx = 1f + squash * land - squash * 0.4f * wave;
            }
            else
            {
                float b = Mathf.Sin(Time.time * Mathf.PI * 2f / breathPeriod) * breathAmount;
                sy = 1f + b;
                sx = 1f - b * 0.6f;
                _hopPhase = 0f;
            }

            if (_gulpRemaining > 0f)
            {
                // 꿀꺽: 0~40% 크게 부풂 → 40~75% 찌그러짐 → 원래대로
                _gulpRemaining -= dt;
                float t = 1f - Mathf.Clamp01(_gulpRemaining / gulpDuration);
                float g = t < 0.4f ? Mathf.Sin(t / 0.4f * Mathf.PI * 0.5f) * 0.22f
                    : t < 0.75f ? Mathf.Lerp(0.22f, -0.12f, (t - 0.4f) / 0.35f)
                    : Mathf.Lerp(-0.12f, 0f, (t - 0.75f) / 0.25f);
                sx *= 1f + g;
                sy *= 1f + (g > 0f ? g : g * 0.5f);
            }

            // 피격 멈칫: 체력이 줄었으면 잠깐 떨며 납작
            if (_health != null)
            {
                int hp = _health.CurrentHealth;
                if (_lastHealth >= 0 && hp < _lastHealth)
                {
                    _hurtRemaining = HurtShake;
                    // 피격 효과음(2026-10-14): 동기화된 체력이 준 것을 각 화면이 한 번씩 본다 → 두 화면 각 1회, 호스트 중복 없음.
                    AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.PlayerHurt, transform.position);
                }

                _lastHealth = hp;
            }

            if (_hurtRemaining > 0f)
            {
                _hurtRemaining -= dt;
                shake += new Vector2(Random.Range(-1, 2), Random.Range(-1, 2)) / 32f;
                sx *= 1.08f;
                sy *= 0.9f;
            }

            // 발(그림 아래 끝)이 바닥에 붙어 있게: 늘인 만큼 아래쪽을 되돌린다.
            float bottom = _visual.sprite != null ? _visual.sprite.bounds.min.y : -0.5f;
            _visual.transform.localScale = new Vector3(sx, sy, 1f);
            _visual.transform.localPosition = new Vector3(shake.x, bottom * (1f - sy) + lift + shake.y, 0f);
        }

        private void CopyRenderer()
        {
            _visual.enabled = _root.enabled;
            _visual.sprite = _root.sprite;
            _visual.color = _root.color;
            _visual.flipX = _root.flipX;
            _visual.flipY = _root.flipY;
            _visual.sortingLayerID = _root.sortingLayerID;
            _visual.sortingOrder = _root.sortingOrder;
            _visual.sharedMaterial = _root.sharedMaterial;
        }

        // 흡수 홀드 중: 앞쪽 부채꼴에서 바람 줄기가 몸으로 빨려 든다(0.05초마다 1개, 0.22초 동안 이동).
        private void SpawnStreaks(Vector2 facing, float dt)
        {
            if (_streakSprite == null)
            {
                return;
            }

            _streakTimer -= dt;
            if (_streakTimer > 0f)
            {
                return;
            }

            _streakTimer = 0.05f;
            float angle = Mathf.Atan2(facing.y, facing.x) + Random.Range(-0.6f, 0.6f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var go = new GameObject("AbsorbStreak");
            var streak = go.AddComponent<AbsorbStreak>();
            streak.Init(_streakSprite, transform, dir * Random.Range(1.2f, 1.8f), _root.sortingOrder + 2);
        }
    }
}
