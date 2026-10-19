using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 속성 상태 이펙트(2026-10-07) — 몸 위에 겹쳐 반복 재생하는 작은 도트 조각들. 순수 표시 전용이고 StatusEffectReceiver가
    /// 매 프레임 동기화된 스택 값으로 Tick을 부른다(각 화면이 같은 값을 보고 그리므로 새 네트워크 코드 없음).
    /// - 화상: 몸 위쪽 불꽃 1~3개(스택 1 / 3 / 5 이상)
    /// - 독: 몸에서 올라와 터지는 거품 1~3개(스택 1 / 4 / 8 이상)
    /// - 냉기: 발밑 얼음 결정 1~3단계(= 냉기 스택)
    /// - 감전: 몸 주위 지그재그 전기가 무작위로 깜빡임(스택 1 이상)
    /// - 경직(대지)·마비(전기 방전): 머리 위를 도는 별 3개(대지 갈색 / 전기 노랑)
    /// 그림: `Art_Source/fx/draw_status_fx.py` → `Resources/Fx/status_*.png`. 크기는 몸 그림 높이에 맞춰 키운다(잡몹 1배, 보스 최대 2배).
    /// 속성 색 반투명 덮기와 머리 위 글자는 StatusEffectReceiver가 그대로 그린다.
    /// **상태이상 빛(2026-10-15, 비주얼 2단계)**: Init에서 화상(주황)·감전(노랑) 점광원을 미리 만들어 두고, 상태가 걸린 동안만 켠다(감전은 깜빡임).
    /// </summary>
    public class StatusFxVisuals : MonoBehaviour
    {
        private const int OrderAboveBody = 4; // 하얀 번쩍임(+3) 위

        private static Sprite[] _flame, _bubble, _frost, _zap;
        private static Sprite _starEarth, _starElectric;
        private static bool _loaded;

        private SpriteRenderer _body;
        private readonly SpriteRenderer[] _flames = new SpriteRenderer[3];
        private readonly SpriteRenderer[] _bubbles = new SpriteRenderer[3];
        private readonly SpriteRenderer[] _stars = new SpriteRenderer[3];
        private SpriteRenderer _frostRenderer;
        private SpriteRenderer _zapRenderer;
        private float _zapTimer;
        private float _seed;
        private UnityEngine.Rendering.Universal.Light2D _burnLight, _shockLight; // 2단계 상태이상 빛(상태 걸린 동안만 enabled)

        private static readonly Vector2[] FlameSpots = { new Vector2(0f, 0.15f), new Vector2(-0.35f, -0.05f), new Vector2(0.35f, 0f) };
        private static readonly Vector2[] BubbleSpots = { new Vector2(0.2f, 0.1f), new Vector2(-0.3f, 0.2f), new Vector2(0.05f, -0.15f) };

        public void Init(SpriteRenderer body)
        {
            _body = body;
            _seed = Random.value * 10f;
            Load();
            for (int i = 0; i < 3; i++)
            {
                _flames[i] = Create("Flame" + i);
                _bubbles[i] = Create("Bubble" + i);
                _stars[i] = Create("Star" + i);
            }

            _frostRenderer = Create("Frost");
            _zapRenderer = Create("Zap");

            // 화상 불꽃·감전 번개만 발광(블룸). 독 거품·얼음·별은 그대로.
            foreach (SpriteRenderer flame in _flames)
            {
                UI.VisualFx.ApplyGlow(flame, UI.GlowKind.StatusFx);
            }

            UI.VisualFx.ApplyGlow(_zapRenderer, UI.GlowKind.StatusFx);

            UI.VisualFxConfig fx = UI.VisualFx.Settings;
            _burnLight = UI.VisualFx.AttachPointLight(transform, fx.burnLightColor, fx.burnLightRadius, fx.burnLightIntensity);
            _shockLight = UI.VisualFx.AttachPointLight(transform, fx.shockLightColor, fx.shockLightRadius, fx.shockLightIntensity);
            UI.VisualFx.SetLight(_burnLight, false);
            UI.VisualFx.SetLight(_shockLight, false);
        }

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _flame = LoadSeries("status_flame_", 0, 3);
            _bubble = LoadSeries("status_bubble_", 0, 4);
            _frost = LoadSeries("status_frost_", 1, 3);
            _zap = LoadSeries("status_zap_", 0, 3);
            _starEarth = Resources.Load<Sprite>("Fx/status_star_earth");
            _starElectric = Resources.Load<Sprite>("Fx/status_star_electric");
        }

        private static Sprite[] LoadSeries(string prefix, int first, int count)
        {
            var sprites = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                sprites[i] = Resources.Load<Sprite>("Fx/" + prefix + (first + i));
            }

            return sprites;
        }

        private SpriteRenderer Create(string name)
        {
            var go = new GameObject("StatusFx_" + name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID = _body.sortingLayerID;
            renderer.enabled = false;
            return renderer;
        }

        public void Tick(int fire, int poison, int ice, int shock, StunDisplay stun, bool hidden)
        {
            if (_body == null)
            {
                return;
            }

            bool show = !hidden && _body.enabled && _body.sprite != null;
            Bounds b = _body.bounds;
            float scale = Mathf.Clamp(b.size.y, 1f, 2f); // 몸 그림 높이(칸) — 잡몹 1, 보스(3칸 그림)는 2배 상한
            float t = Time.time + _seed;
            int order = _body.sortingOrder + OrderAboveBody;

            int flames = !show || fire <= 0 ? 0 : (fire >= 5 ? 3 : fire >= 3 ? 2 : 1);
            for (int i = 0; i < 3; i++)
            {
                var r = _flames[i];
                r.enabled = i < flames && _flame[0] != null;
                if (!r.enabled)
                {
                    continue;
                }

                r.sprite = _flame[(int)(t * 10f + i) % _flame.Length];
                Place(r, b, FlameSpots[i], scale, order);
            }

            int bubbles = !show || poison <= 0 ? 0 : (poison >= 8 ? 3 : poison >= 4 ? 2 : 1);
            for (int i = 0; i < 3; i++)
            {
                var r = _bubbles[i];
                r.enabled = i < bubbles && _bubble[0] != null;
                if (!r.enabled)
                {
                    continue;
                }

                r.sprite = _bubble[(int)(t * 6.5f + i * 1.7f) % _bubble.Length];
                Place(r, b, BubbleSpots[i], scale, order);
            }

            int frost = show ? Mathf.Clamp(ice, 0, 3) : 0;
            _frostRenderer.enabled = frost > 0 && _frost[frost - 1] != null;
            if (_frostRenderer.enabled)
            {
                _frostRenderer.sprite = _frost[frost - 1];
                _frostRenderer.sortingOrder = order;
                _frostRenderer.transform.position = new Vector3(b.center.x, b.min.y + 0.18f * scale, 0f);
                _frostRenderer.transform.localScale = Vector3.one * scale; // 도트가 늘어나지 않게 가로세로 같은 배율
            }

            // 감전: 0.08초마다 무작위 프레임, 약 60%만 보여 깜빡이게
            _zapTimer -= Time.deltaTime;
            if (!show || shock <= 0 || _zap[0] == null)
            {
                _zapRenderer.enabled = false;
            }
            else if (_zapTimer <= 0f)
            {
                _zapTimer = 0.08f;
                _zapRenderer.enabled = Random.value < 0.6f;
                _zapRenderer.sprite = _zap[Random.Range(0, _zap.Length)];
                _zapRenderer.flipX = Random.value < 0.5f;
                _zapRenderer.sortingOrder = order;
                _zapRenderer.transform.position = b.center;
                _zapRenderer.transform.localScale = Vector3.one * scale;
            }

            // 상태이상 빛: 화상이면 주황, 감전이면 노랑 깜빡임(숨김이면 둘 다 끔)
            UI.VisualFx.SetLight(_burnLight, show && fire > 0);
            bool shockOn = show && shock > 0;
            if (shockOn && _shockLight != null)
            {
                UI.VisualFxConfig fx = UI.VisualFx.Settings;
                float flicker = Mathf.PerlinNoise(t * fx.shockFlickerSpeed, _seed);
                _shockLight.intensity = fx.shockLightIntensity * (flicker > 0.45f ? 1f : 0.25f);
            }

            UI.VisualFx.SetLight(_shockLight, shockOn);

            // 경직·마비: 머리 위 타원 궤도로 도는 별 3개(뒤쪽 반은 살짝 어둡게)
            Sprite star = stun == StunDisplay.Paralyze ? _starElectric : stun == StunDisplay.Stagger ? _starEarth : null;
            for (int i = 0; i < 3; i++)
            {
                var r = _stars[i];
                r.enabled = show && star != null;
                if (!r.enabled)
                {
                    continue;
                }

                float a = t * 7f + i * (Mathf.PI * 2f / 3f);
                r.sprite = star;
                r.sortingOrder = order;
                r.transform.position = new Vector3(b.center.x + Mathf.Cos(a) * 0.32f * scale, b.max.y + 0.06f + Mathf.Sin(a) * 0.09f * scale, 0f);
                r.transform.localScale = Vector3.one * scale;
                r.color = Mathf.Sin(a) > 0f ? new Color(0.75f, 0.75f, 0.75f, 1f) : Color.white;
            }
        }

        private static void Place(SpriteRenderer r, Bounds b, Vector2 spot, float scale, int order)
        {
            r.sortingOrder = order;
            r.transform.position = new Vector3(b.center.x + spot.x * b.extents.x, b.center.y + spot.y * b.extents.y, 0f);
            r.transform.localScale = Vector3.one * scale;
        }
    }
}
