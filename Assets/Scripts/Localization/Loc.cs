using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.Localization
{
    /// <summary>게임 표시 언어. 값은 PlayerPrefs에 정수로 저장되므로 순서를 바꾸지 않는다 — 새 언어는 끝에 추가.</summary>
    public enum GameLanguage
    {
        Korean = 0,
        English = 1,
    }

    /// <summary>
    /// 자체 문자열 표(2026-10-06, 언어 설정). Resources/Localization/strings.tsv(열: key, ko, en)를 첫 접근 때 읽는다.
    /// 화면 글자는 Loc.Get(key) 또는 Loc.Get(key, 인자...)(string.Format)로 가져온다. 키가 없거나 영어 칸이 비면 한국어,
    /// 그것도 없으면 키 자체를 돌려준다(에디터에서는 키마다 한 번 경고). 언어는 각 클라이언트가 자기 설정으로 표시하고
    /// 네트워크로는 동기화하지 않는다 — 서버가 클라이언트에 보내는 문구는 <see cref="Pack"/>으로 키+인자를 보낸 뒤
    /// 받는 쪽에서 <see cref="Unpack"/>으로 자기 언어로 만든다. 자세한 규칙: docs/04_구현위키/언어_설정.md.
    /// </summary>
    public static class Loc
    {
        private const string PrefKey = "language";
        private const string ResourcePath = "Localization/strings";

        /// <summary>Pack 문자열의 키·인자 구분 문자(Unit Separator). 일반 글자에는 나오지 않는다.</summary>
        private const char PackSeparator = '\u001F';

        /// <summary>Pack 인자 앞에 붙이면 받는 쪽에서 그 인자도 키로 번역한다(예: "@element.Fire").</summary>
        public const string KeyArgPrefix = "@";

        private static Dictionary<string, string[]> _table;
        private static GameLanguage? _current;
        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>언어가 바뀌었을 때(SetLanguage). 고정 글자(LocalizedText)·화면 갱신이 구독한다.</summary>
        public static event Action Changed;

        public static GameLanguage Current
        {
            get
            {
                if (!_current.HasValue)
                {
                    _current = LoadSavedLanguage();
                }

                return _current.Value;
            }
        }

        // 도메인 리로드 없이 Play를 다시 시작해도 표를 새로 읽게 한다(표 수정 즉시 반영).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _table = null;
            _current = null;
            _warned.Clear();
        }

        /// <summary>언어를 바꾸고 저장한 뒤 Changed를 알린다. 같은 언어면 아무것도 안 한다.</summary>
        public static void SetLanguage(GameLanguage language)
        {
            if (Current == language)
            {
                return;
            }

            _current = language;
            try
            {
                PlayerPrefs.SetInt(PrefKey, (int)language);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Loc] language save failed: {e.Message}");
            }

            Changed?.Invoke();
        }

        /// <summary>키의 현재 언어 글자. 없으면 한국어 → 키 자체.</summary>
        public static string Get(string key)
        {
            if (TryGet(key, out string value))
            {
                return value;
            }

            WarnMissing(key);
            return key ?? "";
        }

        /// <summary>키의 글자에 인자를 넣는다(string.Format — {0}, {1:0.0} 등).</summary>
        public static string Get(string key, params object[] args)
        {
            string format = Get(key);
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                WarnMissing(key + " (format)");
                return format;
            }
        }

        /// <summary>표에 키가 있으면 현재 언어 글자(영어 칸이 비면 한국어)를 준다. 경고는 내지 않는다.</summary>
        public static bool TryGet(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            EnsureTable();
            if (!_table.TryGetValue(key, out string[] row))
            {
                return false;
            }

            int index = (int)Current;
            value = index < row.Length && !string.IsNullOrEmpty(row[index]) ? row[index] : row[0];
            return value != null;
        }

        public static bool Has(string key)
        {
            return TryGet(key, out _);
        }

        /// <summary>
        /// 네트워크로 보낼 문구: 키와 인자를 한 문자열로 묶는다. 인자가 "@키"면 받는 쪽에서 그것도 번역한다.
        /// 받는 쪽은 <see cref="Unpack"/>(DamagePopup.SpawnText·지도 안내 등은 자동으로 Unpack한다).
        /// </summary>
        public static string Pack(string key, params object[] args)
        {
            var parts = new string[(args?.Length ?? 0) + 1];
            parts[0] = key;
            for (int i = 1; i < parts.Length; i++)
            {
                parts[i] = args[i - 1] != null ? args[i - 1].ToString() : "";
            }

            return string.Join(PackSeparator.ToString(), parts) + PackSeparator;
        }

        /// <summary>인자 하나를 "받는 쪽에서 번역할 키"로 표시한다(Pack 인자용).</summary>
        public static string KeyArg(string key)
        {
            return KeyArgPrefix + key;
        }

        /// <summary>Pack 문자열을 현재 언어 글자로 만든다. Pack이 아니면(일반 글자) 그대로 돌려준다.</summary>
        public static string Unpack(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.IndexOf(PackSeparator) < 0)
            {
                return payload;
            }

            string[] parts = payload.TrimEnd(PackSeparator).Split(PackSeparator);
            if (parts.Length == 1)
            {
                return Get(parts[0]);
            }

            var args = new object[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                string arg = parts[i];
                args[i - 1] = arg.StartsWith(KeyArgPrefix, StringComparison.Ordinal) ? Get(arg.Substring(KeyArgPrefix.Length)) : arg;
            }

            return Get(parts[0], args);
        }

        private static GameLanguage LoadSavedLanguage()
        {
            try
            {
                if (PlayerPrefs.HasKey(PrefKey))
                {
                    int saved = PlayerPrefs.GetInt(PrefKey);
                    if (Enum.IsDefined(typeof(GameLanguage), saved))
                    {
                        return (GameLanguage)saved;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Loc] language load failed: {e.Message}");
            }

            return Application.systemLanguage == SystemLanguage.Korean ? GameLanguage.Korean : GameLanguage.English;
        }

        private static void EnsureTable()
        {
            if (_table != null)
            {
                return;
            }

            _table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[Loc] Resources/{ResourcePath} not found — keys will be shown as-is");
                return;
            }

            Parse(asset.text, _table);
        }

        private static void Parse(string text, Dictionary<string, string[]> table)
        {
            int languageCount = Enum.GetValues(typeof(GameLanguage)).Length;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] cols = line.Split('\t');
                string key = cols[0].Trim();
                if (key.Length == 0 || key == "key")
                {
                    continue; // 빈 키·머리 줄
                }

                var row = new string[languageCount];
                for (int c = 0; c < languageCount; c++)
                {
                    row[c] = c + 1 < cols.Length ? Unescape(cols[c + 1]) : "";
                }

                if (table.ContainsKey(key) && Application.isEditor)
                {
                    Debug.LogWarning($"[Loc] duplicate key in strings.tsv: {key} (line {i + 1})");
                }

                table[key] = row;
            }
        }

        private static string Unescape(string value)
        {
            return value.IndexOf('\\') < 0 ? value : value.Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private static void WarnMissing(string key)
        {
            if (!Application.isEditor || key == null || !_warned.Add(key))
            {
                return;
            }

            Debug.LogWarning($"[Loc] missing key: {key}");
        }
    }
}
