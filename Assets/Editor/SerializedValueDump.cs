using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools
{
    /// <summary>
    /// 구조 정리(리팩터) 안전망(2026-10-08, docs/02_기술/구조_개선_설계.md 단계 0). 프리팹·현재 씬·Assets/Data ScriptableObject에 저장된
    /// AbsorbRoguelike 컴포넌트의 직렬화 값을 한 줄씩("에셋 | 오브젝트 경로 | 컴포넌트 | 필드 = 값") 파일로 남기고, 기준 파일과 비교한다.
    /// 필드를 다른 컴포넌트로 옮기면 Inspector 값이 기본값으로 돌아가는 사고를 잡는 용도 — 비교 키에서 컴포넌트 이름은 빼서(같은 오브젝트 안
    /// 이동은 같은 키) 값이 바뀐 것·사라진 것·새로 생긴 것을 Tools/refactor_baseline/diff.txt에 적는다.
    /// </summary>
    public static class SerializedValueDump
    {
        private const string Dir = "Tools/refactor_baseline";
        private const string BaselinePath = Dir + "/serialized_values.txt";
        private const string CurrentPath = Dir + "/serialized_values_current.txt";
        private const string DiffPath = Dir + "/diff.txt";

        [MenuItem("Tools/Absorb/직렬화 값 - 기준 다시 저장")]
        private static void SaveBaseline() => Write(BaselinePath);

        [MenuItem("Tools/Absorb/직렬화 값 - 기준과 비교")]
        public static void Compare()
        {
            Write(CurrentPath);
            if (!File.Exists(BaselinePath))
            {
                Debug.LogError($"[SerializedValueDump] 기준 파일 없음: {BaselinePath}");
                return;
            }

            Dictionary<string, string> before = Load(BaselinePath), after = Load(CurrentPath);
            var sb = new StringBuilder();
            int changed = 0, missing = 0, added = 0;
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out string now))
                {
                    sb.Append("사라짐 | ").Append(pair.Key).Append(" = ").Append(pair.Value).Append('\n');
                    missing++;
                }
                else if (now != pair.Value)
                {
                    sb.Append("바뀜 | ").Append(pair.Key).Append(" : ").Append(pair.Value).Append(" → ").Append(now).Append('\n');
                    changed++;
                }
            }

            foreach (var pair in after)
            {
                if (!before.ContainsKey(pair.Key))
                {
                    sb.Append("새로 | ").Append(pair.Key).Append(" = ").Append(pair.Value).Append('\n');
                    added++;
                }
            }

            File.WriteAllText(DiffPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[SerializedValueDump] 바뀜 {changed} / 사라짐 {missing} / 새로 {added} → {DiffPath}");
        }

        // "에셋 | 오브젝트 | 컴포넌트 | 필드 = 값" → 키 "에셋 | 오브젝트 | 필드"
        private static Dictionary<string, string> Load(string path)
        {
            var map = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf(" = ");
                string[] parts = eq < 0 ? null : line.Substring(0, eq).Split(new[] { " | " }, System.StringSplitOptions.None);
                if (parts == null || parts.Length < 3)
                {
                    continue;
                }

                string key = parts.Length >= 4 ? $"{parts[0]} | {parts[1]} | {parts[3]}" : $"{parts[0]} | {parts[2]}";
                map[key] = line.Substring(eq + 3);
            }

            return map;
        }

        private static void Write(string path)
        {
            var sb = new StringBuilder();
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources", "Assets/Data" }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(System.StringComparer.Ordinal);
            foreach (string p in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null)
                {
                    DumpGameObject(go, p, sb);
                }
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                DumpGameObject(root, scene.path, sb);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/Data" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(p);
                if (obj != null && IsOurs(obj))
                {
                    DumpObject(obj, p + " | " + obj.GetType().Name, sb);
                }
            }

            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[SerializedValueDump] 저장: {path}");
        }

        private static bool IsOurs(Object obj) => (obj.GetType().Namespace ?? "").StartsWith("AbsorbRoguelike");

        private static void DumpGameObject(GameObject root, string label, StringBuilder sb)
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !IsOurs(mb))
                {
                    continue;
                }

                string goPath = mb.transform == root.transform ? root.name : root.name + "/" + AnimationUtility.CalculateTransformPath(mb.transform, root.transform);
                DumpObject(mb, $"{label} | {goPath} | {mb.GetType().Name}", sb);
            }
        }

        private static void DumpObject(Object obj, string prefix, StringBuilder sb)
        {
            var so = new SerializedObject(obj);
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = true;
                if (it.name == "m_Script" || it.propertyType == SerializedPropertyType.Generic)
                {
                    continue; // Generic(배열·구조체)은 자식 필드로 들어가 한 줄씩 남는다
                }

                sb.Append(prefix).Append(" | ").Append(it.propertyPath).Append(" = ").Append(Format(it)).Append('\n');
            }
        }

        private static string Format(SerializedProperty it)
        {
            switch (it.propertyType)
            {
                case SerializedPropertyType.Integer: return it.longValue.ToString();
                case SerializedPropertyType.Boolean: return it.boolValue.ToString();
                case SerializedPropertyType.Float: return it.floatValue.ToString("R");
                case SerializedPropertyType.String: return "\"" + it.stringValue + "\"";
                case SerializedPropertyType.Enum:
                    return it.enumValueIndex >= 0 && it.enumValueIndex < it.enumNames.Length ? it.enumNames[it.enumValueIndex] : it.intValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return it.objectReferenceValue == null ? "null" : AssetDatabase.GetAssetPath(it.objectReferenceValue) + "#" + it.objectReferenceValue.name;
                case SerializedPropertyType.Color: return it.colorValue.ToString("F4");
                case SerializedPropertyType.Vector2: return it.vector2Value.ToString("R");
                case SerializedPropertyType.Vector3: return it.vector3Value.ToString("R");
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize: return it.intValue.ToString();
                default: return "(" + it.propertyType + ")";
            }
        }
    }
}
