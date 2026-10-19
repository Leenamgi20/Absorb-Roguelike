using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 토끼 귀 채찍 명중 번쩍임(2026-09-28, 결정 ㉑A 가독성). 끝부분 강타는 크고 밝은 금색 별, 근접 타격은 작고 흐린 회색 별.
    /// 순수 표시 전용 — DamagePopup.Spawn이 kind(EarWhipTip/EarWhipClose)를 보고 같이 띄우므로, 기존 MonsterStub/BossStub
    /// RpcShowDamage 경로를 그대로 타 원격 클라이언트에도 보인다(새 RPC 없음). 대상 자식이 아닌 독립 GameObject라
    /// 죽는 타격에서 대상이 Destroy돼도 끝까지 재생된다. 스프라이트는 코드로 한 번만 만들어 재사용한다.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        /// <summary>팝업 위치(머리 위 top + 12px)에서 몸 쪽으로 내리는 거리 — 16px 몸 기준 대략 중심. [PLAYTEST]</summary>
        public const float OffsetBelowPopup = 20f * DamagePopup.Pixel;

        private const int SortingOrder = 24; // 대미지 숫자(25) 바로 아래, 캐릭터·체력바 위

        private static Sprite _starSprite;

        private SpriteRenderer _renderer;
        private Color _color;
        private float _lifetime;
        private float _startScale;
        private float _endScale;
        private float _elapsed;

        public static void Spawn(Vector3 worldPos, bool strong)
        {
            var go = new GameObject(strong ? "HitFlashTip" : "HitFlashClose");
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-15f, 15f));
            var flash = go.AddComponent<HitFlash>();
            flash._renderer = go.AddComponent<SpriteRenderer>();
            flash._renderer.sprite = StarSprite;
            flash._renderer.sortingOrder = SortingOrder;
            if (strong)
            {
                flash._color = new Color(1f, 0.88f, 0.3f, 1f);   // [PLAYTEST] 밝은 금색
                flash._lifetime = 0.2f;
                flash._startScale = 0.8f;
                flash._endScale = 2.2f;
            }
            else
            {
                flash._color = new Color(0.75f, 0.75f, 0.75f, 0.5f); // [PLAYTEST] 흐린 회색
                flash._lifetime = 0.15f;
                flash._startScale = 0.5f;
                flash._endScale = 0.8f;
            }

            flash.Apply(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / _lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            Apply(t);
        }

        private void Apply(float t)
        {
            float eased = 1f - (1f - t) * (1f - t); // 빠르게 커졌다가 느려짐
            transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, eased);
            Color c = _color;
            c.a = _color.a * (1f - t);
            _renderer.color = c;
        }

        // 16×16 4방향 별(십자 + 가운데 마름모), PPU 16 → 1칸 크기. 흰색이라 SpriteRenderer.color로 물든다.
        private static Sprite StarSprite
        {
            get
            {
                if (_starSprite != null)
                {
                    return _starSprite;
                }

                const int size = 16;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var pixels = new Color32[size * size];
                float c = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
                        // 가느다란 십자 팔(끝으로 갈수록 가늘게) + 가운데 마름모
                        bool arm = (dx <= 0.5f && dy <= c) || (dy <= 0.5f && dx <= c)
                                   || (dx <= 1.5f && dy <= c * 0.55f) || (dy <= 1.5f && dx <= c * 0.55f);
                        bool core = dx + dy <= 3f;
                        pixels[y * size + x] = arm || core ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();
                _starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                return _starSprite;
            }
        }
    }
}
