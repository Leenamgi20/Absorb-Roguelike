using System.Collections.Generic;
using System.IO;
using System.Linq;
using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AbsorbRoguelike.EditorTools
{
    /// <summary>
    /// 층 속성 타일 테마 생성기(2026-10-06). Assets/Art/Tiles/tileset_{속성}_32.png(원본 example1_tileset 16px 시트와 배치가 같은 32px 시트)를
    /// 원본과 같은 칸·같은 이름으로 슬라이스하고(PPU 32 → 화면 크기 동일), 원본 Tile 에셋마다 같은 설정의 속성 Tile을 만든 뒤
    /// ElementTileTheme(원본 → 속성 짝)을 Assets/Art/Tiles/Themes/에 만든다. 다시 실행하면 덮어쓴다.
    /// </summary>
    public static class ElementTileThemeBuilder
    {
        private const string SourceSheet = "Assets/ThirdParty/EdgarExample1/Example1/example1_tileset.png";
        private const string SourceTiles = "Assets/ThirdParty/EdgarExample1/Example1/Tiles";
        private const string ArtRoot = "Assets/Art/Tiles";

        private static readonly (ElementType element, string key)[] Elements =
        {
            (ElementType.Fire, "fire"), (ElementType.Ice, "ice"), (ElementType.Electric, "electric"),
            (ElementType.Poison, "poison"), (ElementType.Earth, "earth"),
        };

        [MenuItem("Absorb/Tiles/Build Element Tile Themes")]
        public static void BuildAll()
        {
            var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(SourceSheet);
#pragma warning disable 618
            SpriteMetaData[] sourceRects = sourceImporter.spritesheet;
#pragma warning restore 618
            float scale = 2f; // 16px → 32px

            string[] tileGuids = AssetDatabase.FindAssets("t:Tile", new[] { SourceTiles });
            // RandomTile(바닥 "Floor random")처럼 Tile을 상속하고 스프라이트를 배열로 가진 타일도 포함한다.
            var sourceTiles = tileGuids.Select(g => AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).ToList();

            Directory.CreateDirectory($"{ArtRoot}/Themes");
            foreach (var (element, key) in Elements)
            {
                string sheetPath = $"{ArtRoot}/tileset_{key}_32.png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(sheetPath);
                if (importer == null)
                {
                    Debug.LogWarning($"[ElementTileThemeBuilder] {sheetPath} 없음 — 건너뜀");
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 32;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
#pragma warning disable 618
                importer.spritesheet = sourceRects.Select(r =>
                {
                    r.rect = new Rect(r.rect.x * scale, r.rect.y * scale, r.rect.width * scale, r.rect.height * scale);
                    r.border *= scale;
                    return r;
                }).ToArray();
#pragma warning restore 618
                importer.SaveAndReimport();

                var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().ToDictionary(s => s.name);
                string tileDir = $"{ArtRoot}/{key}";
                Directory.CreateDirectory(tileDir);

                // 테마 에셋은 지우지 않고 내용만 다시 채운다(씬 LevelGenerator.tileThemes 참조 유지).
                string themePath = $"{ArtRoot}/Themes/TileTheme_{key}.asset";
                var theme = AssetDatabase.LoadAssetAtPath<ElementTileTheme>(themePath);
                if (theme == null)
                {
                    theme = ScriptableObject.CreateInstance<ElementTileTheme>();
                    AssetDatabase.CreateAsset(theme, themePath);
                }

                theme.element = element;
                theme.sources.Clear();
                theme.targets.Clear();
                theme.spriteSources.Clear();
                theme.spriteTargets.Clear();
                foreach (Sprite original in AssetDatabase.LoadAllAssetsAtPath(SourceSheet).OfType<Sprite>())
                {
                    if (sprites.TryGetValue(original.name, out Sprite mappedSprite))
                    {
                        theme.spriteSources.Add(original);
                        theme.spriteTargets.Add(mappedSprite);
                    }
                }
                foreach (Tile source in sourceTiles)
                {
                    // 원본 타일을 같은 타입으로 복제한 뒤, 직렬화된 모든 Sprite 참조(단일 sprite·RandomTile의 m_Sprites 등)를
                    // 같은 이름의 속성 시트 스프라이트로 바꾼다.
                    string tilePath = $"{tileDir}/{key}_{source.name}.asset";
                    AssetDatabase.DeleteAsset(tilePath);
                    Tile tile = Object.Instantiate(source);
                    tile.name = $"{key}_{source.name}";
                    var so = new SerializedObject(tile);
                    SerializedProperty it = so.GetIterator();
                    bool missing = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is Sprite old)
                        {
                            if (sprites.TryGetValue(old.name, out Sprite mapped))
                            {
                                it.objectReferenceValue = mapped;
                            }
                            else
                            {
                                missing = true;
                                Debug.LogWarning($"[ElementTileThemeBuilder] {key}: 스프라이트 {old.name} 없음 ({source.name})");
                            }
                        }
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                    if (missing)
                    {
                        Object.DestroyImmediate(tile);
                        continue;
                    }

                    AssetDatabase.CreateAsset(tile, tilePath);
                    theme.sources.Add(source);
                    theme.targets.Add(tile);
                }

                EditorUtility.SetDirty(theme);
                Debug.Log($"[ElementTileThemeBuilder] {key}: 타일 {theme.targets.Count}개 → {themePath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
