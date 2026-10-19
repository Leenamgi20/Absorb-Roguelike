using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 맞은 대상이 아주 잠깐 하얗게 번쩍이고 부르르 떪(2026-10-07, HitFx 참고). 몸 색(color)은 정예 틴트·보스 경직 등이 매 프레임 쓰고 있어
    /// 건드리지 않고, 몸 그림을 복사한 자식 오버레이를 `Resources/Fx/SpriteSolidColor` 셰이더(단색)로 덮는다
    /// (StatusEffectReceiver 오버레이와 같은 방식, 그보다 위 순서). 처음 맞을 때 대상에 붙고 이후 재사용한다.
    /// 흔들림(히트스톱 "멈칫", 2026-10-07): 몸 렌더러는 루트에 붙어 있어 루트를 움직이면 물리가 흔들리므로, 흔들리는 동안만 루트 그림을
    /// forceRenderingOff로 숨기고 같은 그림의 복사본을 1px씩 떨며 그린다. 순수 표시 전용(판정·위치는 그대로).
    /// 외곽선(2026-10-15, 비주얼 3단계 V8): 흔들림 복사본은 몸의 sharedMaterial까지 복사해 정예 외곽선이 흔들리는 동안에도 보인다.
    /// 하얀 번쩍임 오버레이는 별도 자식 렌더러라 몸 재질을 바꾸지 않는다 → 번쩍임이 끝나면 외곽선 몸이 그대로 다시 보인다.
    /// </summary>
    public class HitWhiteFlash : MonoBehaviour
    {
        private const float Duration = 0.07f;   // [PLAYTEST]
        private const float ShakeDuration = 0.1f; // [PLAYTEST]
        private const int OrderAboveBody = 3;  // 상태 오버레이(+1)·벽 경직 오버레이(+2) 위

        private static Material _material;
        private static bool _materialLoaded;

        private SpriteRenderer _body;
        private SpriteRenderer _overlay;
        private SpriteRenderer _shakeCopy;
        private float _remaining;
        private float _shakeRemaining;

        public static void Flash(GameObject target, bool shake = false)
        {
            var body = target.GetComponent<SpriteRenderer>();
            if (body == null || Material == null)
            {
                return;
            }

            var flash = target.GetComponent<HitWhiteFlash>();
            if (flash == null)
            {
                flash = target.AddComponent<HitWhiteFlash>();
                flash._body = body;
                var go = new GameObject("HitWhiteFlash");
                go.transform.SetParent(target.transform, false);
                flash._overlay = go.AddComponent<SpriteRenderer>();
                flash._overlay.sharedMaterial = Material;
                flash._overlay.sortingLayerID = body.sortingLayerID;
                var copy = new GameObject("HitShakeCopy");
                copy.transform.SetParent(target.transform, false);
                flash._shakeCopy = copy.AddComponent<SpriteRenderer>();
                flash._shakeCopy.enabled = false;
            }

            flash._remaining = Duration;
            if (shake)
            {
                flash._shakeRemaining = ShakeDuration;
            }

            flash.Sync();
        }

        private static Material Material
        {
            get
            {
                if (!_materialLoaded)
                {
                    _materialLoaded = true;
                    var shader = Resources.Load<Shader>("Fx/SpriteSolidColor");
                    if (shader != null)
                    {
                        _material = new Material(shader) { color = Color.white };
                    }
                }

                return _material;
            }
        }

        private void LateUpdate()
        {
            if (_remaining <= 0f && _shakeRemaining <= 0f)
            {
                return;
            }

            _remaining -= Time.deltaTime;
            _shakeRemaining -= Time.deltaTime;
            Sync();
        }

        private void OnDisable()
        {
            if (_body != null)
            {
                _body.forceRenderingOff = false;
            }
        }

        private void Sync()
        {
            bool bodyVisible = _body != null && _body.enabled;
            bool shaking = _shakeRemaining > 0f && bodyVisible;
            Vector3 jitter = shaking ? new Vector3(Random.Range(-1, 2), Random.Range(-1, 2), 0f) / 32f : Vector3.zero;

            // 흔들림: 루트 그림을 숨기고 복사본을 떨며 그린다
            _body.forceRenderingOff = shaking;
            _shakeCopy.enabled = shaking;
            if (shaking)
            {
                _shakeCopy.sprite = _body.sprite;
                _shakeCopy.sharedMaterial = _body.sharedMaterial; // 외곽선 재질(3단계 V8)도 따라가 흔들리는 동안 외곽선이 사라지지 않게
                _shakeCopy.color = _body.color;
                _shakeCopy.flipX = _body.flipX;
                _shakeCopy.flipY = _body.flipY;
                _shakeCopy.sortingLayerID = _body.sortingLayerID;
                _shakeCopy.sortingOrder = _body.sortingOrder;
                _shakeCopy.transform.localPosition = jitter;
            }

            bool show = _remaining > 0f && bodyVisible;
            _overlay.enabled = show;
            if (!show)
            {
                return;
            }

            _overlay.sprite = _body.sprite;
            _overlay.flipX = _body.flipX;
            _overlay.flipY = _body.flipY;
            _overlay.sortingOrder = _body.sortingOrder + OrderAboveBody;
            _overlay.transform.localPosition = jitter;
        }
    }
}
