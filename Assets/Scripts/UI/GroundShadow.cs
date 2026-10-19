using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 발밑 타원 그림자(2026-10-08, 아트_UI_규격 식별 규칙 "스프라이트에는 그림자를 그리지 않고 발밑 원형 그림자를 일괄 적용").
    /// 플레이어·잡몹·정예·보스가 Awake에서 붙인다(<see cref="Attach"/>). 몸 그림의 불투명 부분(스프라이트 Tight 메시 꼭짓점)으로
    /// 발 위치·폭을 재서, 그 폭에 맞는 도트 타원을 코드로 만들어(폭별 캐시, 점 필터·PPU 32) 몸 아래에 깐다. 그림이 바뀌면(보스 외형,
    /// 탈) 다시 잰다. 몸 렌더러가 꺼지면(흡수한 보스 시체 숨김 등) 같이 숨는다. 슬라임이 통통 뛸 때는 몸만 뜨고 그림자는 바닥에 남는다.
    /// 순수 표시 전용, 각 화면 로컬.
    /// **발밑 번호 고리(2026-10-15, 비주얼 2단계)**: 플레이어(PlayerIdentityInfo)이고 2명 이상일 때만(머리 위 번호표와 같은 규칙) 그림자보다 조금 큰
    /// 번호 색 얇은 타원 고리(정수 px 두께, 32PPU 점 필터)를 그림자 바로 위에 그린다. 쓰러짐 중에도, 효과 토글(F1)과 무관하게 유지.
    /// </summary>
    public class GroundShadow : MonoBehaviour
    {
        private const float PixelsPerUnit = 32f;
        private const float WidthRatio = 0.75f;   // [PLAYTEST] 몸 폭 대비 그림자 폭
        private const float HeightRatio = 0.32f;  // 타원 세로/가로
        private const float Alpha = 0.32f;        // [PLAYTEST]
        private const int OrderBelowBody = 6;     // 몸(10) 아래, 바닥 위

        private static readonly Dictionary<int, Sprite> SpriteCache = new Dictionary<int, Sprite>();

        private SpriteRenderer _body;
        private SpriteRenderer _shadow;
        private Sprite _measured;
        private SpriteRenderer _ring;               // 발밑 번호 고리(플레이어만)
        private AbsorbRoguelike.Player.PlayerIdentityInfo _identity;
        private int _shadowWidthPx;
        private int _ringKey = -1;
        private static readonly Dictionary<int, Sprite> RingCache = new Dictionary<int, Sprite>();

        public static void Attach(GameObject target)
        {
            if (target == null || target.GetComponent<GroundShadow>() != null)
            {
                return;
            }

            var body = target.GetComponent<SpriteRenderer>();
            if (body == null)
            {
                return;
            }

            var shadow = target.AddComponent<GroundShadow>();
            shadow._body = body;
            var go = new GameObject("GroundShadow");
            go.transform.SetParent(target.transform, false);
            shadow._shadow = go.AddComponent<SpriteRenderer>();
            shadow._shadow.color = new Color(0f, 0f, 0f, Alpha);

            shadow._identity = target.GetComponent<AbsorbRoguelike.Player.PlayerIdentityInfo>();
            if (shadow._identity != null)
            {
                var ringGo = new GameObject("FootRing");
                ringGo.transform.SetParent(go.transform, false); // 그림자와 같은 자리(가운데)
                shadow._ring = ringGo.AddComponent<SpriteRenderer>();
                shadow._ring.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_body == null || _shadow == null)
            {
                return;
            }

            bool show = _body.enabled && _body.sprite != null;
            _shadow.enabled = show;
            if (!show)
            {
                if (_ring != null)
                {
                    _ring.enabled = false;
                }

                return;
            }

            _shadow.sortingLayerID = _body.sortingLayerID;
            _shadow.sortingOrder = _body.sortingOrder - OrderBelowBody;
            if (_body.sprite != _measured)
            {
                Measure(_body.sprite);
            }

            UpdateRing();
        }

        private void UpdateRing()
        {
            if (_ring == null)
            {
                return;
            }

            bool show = _identity != null && AbsorbRoguelike.Player.PlayerIdentityInfo.All.Count >= 2 && _shadowWidthPx > 0;
            _ring.enabled = show;
            if (!show)
            {
                return;
            }

            VisualFxConfig fx = VisualFx.Settings;
            int thickness = Mathf.Max(1, fx.footRingThicknessPx);
            int widthPx = _shadowWidthPx + fx.footRingPadXPx * 2;
            int heightPx = ShadowHeightPx(_shadowWidthPx) + fx.footRingPadYPx * 2;
            int key = (widthPx * 1024 + heightPx) * 16 + Mathf.Min(thickness, 15);
            if (key != _ringKey)
            {
                _ringKey = key;
                _ring.sprite = RingSprite(widthPx, heightPx, thickness, key);
            }

            Color color = AbsorbRoguelike.Player.PlayerIdentityInfo.ColorForNumber(_identity.PlayerNumber);
            color.a = fx.footRingAlpha;
            _ring.color = color;
            _ring.sortingLayerID = _shadow.sortingLayerID;
            _ring.sortingOrder = _shadow.sortingOrder + 1; // 그림자 바로 위, 몸 아래
        }

        // 불투명 부분의 아래 끝·폭(스프라이트 로컬 단위)으로 그림자 크기·위치를 정한다.
        private void Measure(Sprite sprite)
        {
            _measured = sprite;
            Vector2[] vertices = sprite.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue;
            foreach (Vector2 v in vertices)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y);
            }

            int widthPx = Mathf.Max(6, Mathf.RoundToInt((maxX - minX) * PixelsPerUnit * WidthRatio));
            _shadowWidthPx = widthPx;
            _shadow.sprite = EllipseSprite(widthPx);
            // 타원 가운데를 메시 아래 끝에서 2px 위로 — Tight 메시는 테두리 여유(1~3px)가 있어 실제 발끝보다 조금 아래다(측정: 슬라임 메시 -0.50, 실제 -0.42)
            float centerX = Mathf.Round((minX + maxX) * 0.5f * PixelsPerUnit) / PixelsPerUnit;
            _shadow.transform.localPosition = new Vector3(centerX, minY + 2f / PixelsPerUnit, 0f);
        }

        private static Sprite EllipseSprite(int widthPx)
        {
            if (SpriteCache.TryGetValue(widthPx, out Sprite cached) && cached != null)
            {
                return cached;
            }

            int heightPx = ShadowHeightPx(widthPx);
            var texture = new Texture2D(widthPx, heightPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "GroundShadow_" + widthPx,
            };
            var pixels = new Color32[widthPx * heightPx];
            float rx = widthPx * 0.5f, ry = heightPx * 0.5f;
            for (int y = 0; y < heightPx; y++)
            {
                for (int x = 0; x < widthPx; x++)
                {
                    float dx = (x + 0.5f - rx) / rx;
                    float dy = (y + 0.5f - ry) / ry;
                    pixels[y * widthPx + x] = dx * dx + dy * dy <= 1f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, widthPx, heightPx), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.name = texture.name;
            SpriteCache[widthPx] = sprite;
            return sprite;
        }

        private static int ShadowHeightPx(int widthPx) => Mathf.Max(3, Mathf.RoundToInt(widthPx * HeightRatio));

        // 바깥 타원 안 + 안쪽 타원(두께만큼 작은) 밖 = 고리. 흰색으로 그리고 색은 SpriteRenderer.color로.
        private static Sprite RingSprite(int widthPx, int heightPx, int thickness, int key)
        {
            if (RingCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            var texture = new Texture2D(widthPx, heightPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "FootRing_" + widthPx + "x" + heightPx + "_" + thickness,
            };
            var pixels = new Color32[widthPx * heightPx];
            float rx = widthPx * 0.5f, ry = heightPx * 0.5f;
            float irx = Mathf.Max(0.01f, rx - thickness), iry = Mathf.Max(0.01f, ry - thickness);
            for (int y = 0; y < heightPx; y++)
            {
                for (int x = 0; x < widthPx; x++)
                {
                    float px = x + 0.5f - rx, py = y + 0.5f - ry;
                    bool inOuter = (px * px) / (rx * rx) + (py * py) / (ry * ry) <= 1f;
                    bool inInner = (px * px) / (irx * irx) + (py * py) / (iry * iry) < 1f;
                    pixels[y * widthPx + x] = inOuter && !inInner ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, widthPx, heightPx), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.name = texture.name;
            RingCache[key] = sprite;
            return sprite;
        }
    }
}
