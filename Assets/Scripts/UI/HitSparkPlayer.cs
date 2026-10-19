using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 명중 스파크 한 번 재생 후 사라짐(2026-10-07, HitFx 참고). 대상의 자식이 아니라 독립 오브젝트라 죽는 타격에서도 끝까지 보인다.
    /// 프레임은 `Resources/Fx/hit_&lt;세트&gt;_0~3`을 세트별로 한 번만 불러 재사용한다. 매번 90° 단위로 돌려 반복감을 줄인다.
    /// </summary>
    public class HitSparkPlayer : MonoBehaviour
    {
        private const int FrameCount = 4;
        private const float FrameTime = 0.045f;  // 4프레임 ≈ 0.18초 [PLAYTEST]
        private const int SortingOrder = 24;     // 대미지 숫자(25) 바로 아래, 캐릭터·체력바 위

        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();

        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private float _elapsed;

        public static void Spawn(Vector3 position, string set, float scale)
        {
            Sprite[] frames = Load(set);
            if (frames == null)
            {
                return;
            }

            var go = new GameObject("HitSpark");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, 90f * Random.Range(0, 4));
            go.transform.localScale = Vector3.one * scale;
            var player = go.AddComponent<HitSparkPlayer>();
            player._frames = frames;
            player._renderer = go.AddComponent<SpriteRenderer>();
            player._renderer.sortingOrder = SortingOrder;
            player._renderer.sprite = frames[0];
            VisualFx.ApplyGlow(player._renderer, GlowKind.HitSpark); // 발광 재질(블룸) — 비주얼 효과 꺼짐이면 원래 재질
        }

        private static Sprite[] Load(string set)
        {
            if (Cache.TryGetValue(set, out Sprite[] cached))
            {
                return cached;
            }

            var frames = new Sprite[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                frames[i] = Resources.Load<Sprite>($"Fx/hit_{set}_{i}");
                if (frames[i] == null)
                {
                    frames = null;
                    break;
                }
            }

            Cache[set] = frames;
            return frames;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            int index = (int)(_elapsed / FrameTime);
            if (index >= _frames.Length)
            {
                Destroy(gameObject);
                return;
            }

            _renderer.sprite = _frames[index];
        }
    }
}
