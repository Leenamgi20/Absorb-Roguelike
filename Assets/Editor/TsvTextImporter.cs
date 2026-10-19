using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools
{
    /// <summary>
    /// .tsv 파일을 TextAsset으로 가져온다(2026-10-06, 언어 설정). Unity 기본 TextAsset 확장자에 .tsv가 없어서
    /// Resources/Localization/strings.tsv를 Resources.Load&lt;TextAsset&gt;으로 읽으려면 이 가져오기 설정이 필요하다.
    /// 표는 스프레드시트에서 바로 열고 저장할 수 있게 .tsv 확장자를 유지한다.
    /// </summary>
    [ScriptedImporter(1, "tsv")]
    public class TsvTextImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = new TextAsset(File.ReadAllText(ctx.assetPath));
            ctx.AddObjectToAsset("text", text);
            ctx.SetMainObject(text);
        }
    }
}
