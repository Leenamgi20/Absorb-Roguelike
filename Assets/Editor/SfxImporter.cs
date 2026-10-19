using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AbsorbRoguelike.Audio;
using UnityEditor;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools
{
    /// <summary>
    /// 효과음 가져오기(2026-10-14). 메뉴 Tools/Absorb/사운드 가져오기, 레벨 디자인 시트 '사운드' 탭 버튼이 <see cref="ImportAll"/>을 부른다.
    /// 1) Art_Source/sound/ 아래 사용자 파일(.wav/.ogg/.mp3, 하위 폴더 포함 — synth/·ref/ 제외)과 Art_Source/sound/synth/ 합성본을 훑는다.
    ///    파일 이름 = SfxId 값 또는 값_1, 값_2…(변형). bgm_/jingle_ 등 목록 밖 이름은 무시.
    /// 2) id마다 사용자 파일이 하나라도 있으면 사용자 파일만, 없으면 합성본을 Assets/Audio/SFX/로 복사한다(원본은 그대로).
    ///    그 id의 예전 복사본 중 이번에 안 쓰는 것(예: 사용자 파일이 생겨 밀려난 합성본)은 지운다.
    /// 3) Assets/Resources/Audio/SfxLibrary.asset(없으면 생성)의 항목에 클립 배열·출처를 연결한다. 볼륨 등 조정값은 보존,
    ///    새 항목만 소리 성격별 기본값(<see cref="ApplyDefaults"/>)으로 시작한다.
    /// 임포트 설정(모노·로드 방식·압축)은 아래 <see cref="SfxAudioPostprocessor"/>가 Assets/Audio/SFX/ 파일에 자동 적용한다.
    /// </summary>
    public static class SfxImporter
    {
        public const string SourceRoot = "Art_Source/sound";
        public const string SynthDir = "Art_Source/sound/synth";
        public const string TargetDir = "Assets/Audio/SFX";
        public const string LibraryPath = "Assets/Resources/Audio/SfxLibrary.asset";

        private static readonly string[] Extensions = { ".wav", ".ogg", ".mp3" };
        private static readonly Regex VariantSuffix = new Regex(@"^(.+)_(\d+)$");

        [MenuItem("Tools/Absorb/사운드 가져오기")]
        public static void ImportFromMenu()
        {
            Debug.Log(ImportAll());
        }

        /// <summary>가져오기 실행 → 요약 문자열(사용자 n개 / 합성 n개 / 빠진 소리 n개(목록)).</summary>
        public static string ImportAll()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Dictionary<string, List<(string path, int variant)>> user = Scan(Path.Combine(projectRoot, SourceRoot), recursive: true);
            Dictionary<string, List<(string path, int variant)>> synth = Scan(Path.Combine(projectRoot, SynthDir), recursive: false);

            Directory.CreateDirectory(Path.Combine(projectRoot, TargetDir));
            var chosen = new Dictionary<string, (List<string> assetPaths, SfxLibrary.ClipSource source)>();
            var missing = new List<string>();
            int userCount = 0, synthCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string id in SfxId.All)
                {
                    SfxLibrary.ClipSource source;
                    List<(string path, int variant)> files;
                    if (user.TryGetValue(id, out files))
                    {
                        source = SfxLibrary.ClipSource.User;
                        userCount++;
                    }
                    else if (synth.TryGetValue(id, out files))
                    {
                        source = SfxLibrary.ClipSource.Synth;
                        synthCount++;
                    }
                    else
                    {
                        missing.Add(id);
                        chosen[id] = (new List<string>(), SfxLibrary.ClipSource.Missing);
                        RemoveStale(projectRoot, id, new HashSet<string>());
                        continue;
                    }

                    var assetPaths = new List<string>();
                    foreach ((string path, int _) in files.OrderBy(f => f.variant))
                    {
                        string assetPath = TargetDir + "/" + Path.GetFileName(path);
                        CopyIfChanged(path, Path.Combine(projectRoot, assetPath));
                        assetPaths.Add(assetPath);
                    }

                    RemoveStale(projectRoot, id, new HashSet<string>(assetPaths.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase));
                    chosen[id] = (assetPaths, source);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SfxLibrary library = LoadOrCreateLibrary();
            Undo.RecordObject(library, "사운드 가져오기");
            foreach (string id in SfxId.All)
            {
                SfxLibrary.Entry entry = library.entries.FirstOrDefault(e => e != null && e.id == id);
                if (entry == null)
                {
                    entry = new SfxLibrary.Entry { id = id };
                    ApplyDefaults(entry);
                    library.entries.Add(entry);
                }

                (List<string> assetPaths, SfxLibrary.ClipSource source) = chosen[id];
                entry.clips = assetPaths.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).ToArray();
                entry.source = entry.clips.Length > 0 ? source : SfxLibrary.ClipSource.Missing;
            }

            // 목록 문서 순서로 정렬(시트 줄 순서). 목록 밖 항목은 뒤에 그대로 둔다.
            library.entries = library.entries
                .OrderBy(e => { int i = Array.IndexOf(SfxId.All, e?.id); return i < 0 ? int.MaxValue : i; })
                .ToList();
            library.InvalidateCache();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);

            return $"[SfxImporter] 사용자 {userCount}개 / 합성 {synthCount}개 / 빠진 소리 {missing.Count}개"
                   + (missing.Count > 0 ? "(" + string.Join(", ", missing) + ")" : string.Empty);
        }

        /// <summary>폴더의 효과음 파일을 id별로 묶는다(id → (경로, 변형 번호 — 번호 없으면 0)).</summary>
        private static Dictionary<string, List<(string path, int variant)>> Scan(string dir, bool recursive)
        {
            var result = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
            if (!Directory.Exists(dir))
            {
                return result;
            }

            IEnumerable<string> files = Directory.EnumerateFiles(dir, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                string full = file.Replace('\\', '/');
                if (recursive && (full.Contains("/synth/") || full.Contains("/ref/")))
                {
                    continue;
                }

                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!Extensions.Contains(ext))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                string id = null;
                int variant = 0;
                if (Array.IndexOf(SfxId.All, name) >= 0)
                {
                    id = name;
                }
                else
                {
                    Match m = VariantSuffix.Match(name);
                    if (m.Success && Array.IndexOf(SfxId.All, m.Groups[1].Value) >= 0)
                    {
                        id = m.Groups[1].Value;
                        variant = int.Parse(m.Groups[2].Value);
                    }
                }

                if (id == null)
                {
                    continue; // bgm_/jingle_/목록 밖 이름
                }

                if (!result.TryGetValue(id, out List<(string, int)> list))
                {
                    result[id] = list = new List<(string, int)>();
                }

                list.Add((file, variant));
            }

            return result;
        }

        private static void CopyIfChanged(string from, string to)
        {
            if (File.Exists(to))
            {
                var a = new FileInfo(from);
                var b = new FileInfo(to);
                if (a.Length == b.Length && File.ReadAllBytes(from).SequenceEqual(File.ReadAllBytes(to)))
                {
                    return;
                }
            }

            File.Copy(from, to, overwrite: true);
        }

        // 이 id의 복사본 중 keep에 없는 파일 삭제(사용자 파일이 합성본을 교체했거나 변형 수가 줄었을 때).
        private static void RemoveStale(string projectRoot, string id, HashSet<string> keep)
        {
            string dir = Path.Combine(projectRoot, TargetDir);
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!Extensions.Contains(ext))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                Match m = VariantSuffix.Match(name);
                string fileId = Array.IndexOf(SfxId.All, name) >= 0 ? name : m.Success ? m.Groups[1].Value : null;
                if (fileId == id && !keep.Contains(Path.GetFileName(file)))
                {
                    AssetDatabase.DeleteAsset(TargetDir + "/" + Path.GetFileName(file));
                }
            }
        }

        private static SfxLibrary LoadOrCreateLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(LibraryPath);
            if (library != null)
            {
                return library;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            library = ScriptableObject.CreateInstance<SfxLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
            return library;
        }

        /// <summary>
        /// 새 항목 기본값(소리 성격별). UI·보스·방/층 소리는 2D(화면 전체), 전투 소리는 위치 소리.
        /// 자주 나는 소리는 간격·동시 수를 좁히고 음높이를 조금 흔든다. [PLAYTEST] 듣고 시트 '사운드' 탭에서 조정.
        /// </summary>
        public static void ApplyDefaults(SfxLibrary.Entry e)
        {
            string id = e.id;
            e.volume = 0.7f;
            e.pitchJitter = 0.04f;
            e.minInterval = 0.03f;
            e.maxConcurrent = 3;
            e.is2D = false;

            if (id.StartsWith("sfx_ui_", StringComparison.Ordinal))
            {
                e.is2D = true;
                e.volume = id == SfxId.UiHover ? 0.35f : 0.5f;
                e.pitchJitter = 0.02f;
                e.minInterval = id == SfxId.UiHover ? 0.05f : 0.03f;
                e.maxConcurrent = 2;
            }
            else if (id.StartsWith("sfx_boss_", StringComparison.Ordinal))
            {
                e.is2D = true;
                e.volume = 0.85f;
                e.pitchJitter = 0.02f;
                e.minInterval = 0.2f;
                e.maxConcurrent = 1;
            }
            else if (id == SfxId.DoorLock || id == SfxId.RoomClear || id == SfxId.PortalOpen || id == SfxId.FloorEnter)
            {
                e.is2D = true;
                e.volume = 0.7f;
                e.pitchJitter = 0f;
                e.minInterval = 0.3f;
                e.maxConcurrent = 1;
            }
            else if (id.StartsWith("sfx_elem_", StringComparison.Ordinal))
            {
                e.volume = 0.5f;
                e.minInterval = 0.08f; // 여러 대상에 동시에 걸릴 때 과다 재생 방지
                e.maxConcurrent = 2;
            }
            else if (id == SfxId.Hit || id == SfxId.MonsterHurt)
            {
                e.volume = 0.6f;
                e.pitchJitter = 0.06f;
                e.minInterval = 0.04f;
                e.maxConcurrent = 4;
            }
            else if (id == SfxId.SwordSwing)
            {
                e.volume = 0.55f;
                e.pitchJitter = 0.05f;
                e.maxConcurrent = 2;
            }
            else if (SfxId.IsLoop(id))
            {
                e.volume = 0.5f;
                e.pitchJitter = 0f;
                e.minInterval = 0f;
                e.maxConcurrent = 2; // 2인이 동시에
            }
            else if (id == SfxId.MonsterWarn)
            {
                e.volume = 0.45f;
                e.minInterval = 0.1f;
                e.maxConcurrent = 2;
            }
            else if (id == SfxId.DragonBreath)
            {
                e.volume = 0.6f;
                e.minInterval = 0.15f;
                e.maxConcurrent = 2;
            }
        }
    }

    /// <summary>Assets/Audio/SFX/ 효과음 임포트 설정: 모노, 짧은 소리 DecompressOnLoad+ADPCM, 반복 소리는 이음매 유지를 위해 PCM.</summary>
    public class SfxAudioPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(SfxImporter.TargetDir + "/", StringComparison.Ordinal))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool loop = SfxId.IsLoop(name) || SfxId.IsLoop(Regex.Replace(name, @"_\d+$", string.Empty));
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = loop ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }
    }
}
