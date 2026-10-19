using System.Collections.Generic;
using AbsorbRoguelike.Abilities;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 층 속성별 타일 테마(2026-10-06). 방 템플릿은 Edgar 예제 타일(example1_*)로 칠해 둔 그대로 두고,
    /// 층 생성 직후 <see cref="Apply"/>가 생성된 레벨의 모든 Tilemap에서 sources[i] → targets[i]로 바꿔 끼운다.
    /// 짝은 에디터 메뉴 "Absorb/Tiles/Build Element Tile Themes"가 같은 스프라이트 이름으로 맞춰 만든다
    /// (원본 example1_tileset 16px 격자와 배치가 같은 32px 시트 Assets/Art/Tiles/tileset_{속성}_32.png).
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Dungeon/Element Tile Theme", fileName = "ElementTileTheme")]
    public class ElementTileTheme : ScriptableObject
    {
        public ElementType element;
        public List<TileBase> sources = new List<TileBase>();
        public List<TileBase> targets = new List<TileBase>();
        [Tooltip("타일이 아니라 SpriteRenderer로 놓인 원본 시트 스프라이트(복도 문 Door horizontal/vertical 등) → 같은 이름의 속성 스프라이트.")]
        public List<Sprite> spriteSources = new List<Sprite>();
        public List<Sprite> spriteTargets = new List<Sprite>();

        public void Apply(GameObject levelRoot)
        {
            if (levelRoot == null)
            {
                return;
            }

            var spriteMap = new Dictionary<Sprite, Sprite>();
            for (int i = 0; i < Mathf.Min(spriteSources.Count, spriteTargets.Count); i++)
            {
                if (spriteSources[i] != null && spriteTargets[i] != null)
                {
                    spriteMap[spriteSources[i]] = spriteTargets[i];
                }
            }

            foreach (SpriteRenderer renderer in levelRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite != null && spriteMap.TryGetValue(renderer.sprite, out Sprite mapped))
                {
                    renderer.sprite = mapped;
                }
            }

            foreach (Tilemap tilemap in levelRoot.GetComponentsInChildren<Tilemap>(true))
            {
                int count = Mathf.Min(sources.Count, targets.Count);
                for (int i = 0; i < count; i++)
                {
                    if (sources[i] != null && targets[i] != null)
                    {
                        tilemap.SwapTile(sources[i], targets[i]);
                    }
                }
            }
        }
    }
}
