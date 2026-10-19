using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools
{
    /// <summary>
    /// 빌드 메뉴(2026-10-16): 브라우저판(WebGL, 혼자 하기 전용)과 다운로드판(Windows)을 메뉴 한 번으로 만든다.
    /// - Tools/Absorb/빌드/브라우저판(WebGL): Gzip + Decompression Fallback(itch.io 등 서버 헤더 설정 불필요), 스레드 끔(SharedArrayBuffer 불필요)
    ///   → Builds/WebGL/
    /// - Tools/Absorb/빌드/다운로드판(Windows): StandaloneWindows64, Development 끔 → Builds/Release/&lt;제품명&gt;.exe
    /// 씬 목록은 EditorBuildSettings(켜진 것만). 활성 빌드 타깃은 직접 바꾸지 않는다(BuildPlayer가 내부적으로 처리). 결과는 로그로만 남긴다.
    /// Builds/ 는 git 제외, 릴리즈 zip은 GitHub Release. 자세한 내용: docs/04_구현위키/브라우저판.md
    /// </summary>
    public static class BuildMenu
    {
        private const string WebGLOutput = "Builds/WebGL";
        private const string WindowsOutputDir = "Builds/Release";

        [MenuItem("Tools/Absorb/빌드/브라우저판(WebGL)")]
        public static void BuildWebGL()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.threadsSupport = false;

            Build(new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = WebGLOutput,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });
        }

        [MenuItem("Tools/Absorb/빌드/다운로드판(Windows)")]
        public static void BuildWindows()
        {
            Build(new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = Path.Combine(WindowsOutputDir, PlayerSettings.productName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None, // Development 끔
            });
        }

        private static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        }

        private static void Build(BuildPlayerOptions options)
        {
            if (options.scenes.Length == 0)
            {
                Debug.LogError("[BuildMenu] Build Settings에 켜진 씬이 없다");
                return;
            }

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            string sizeMb = (summary.totalSize / (1024f * 1024f)).ToString("0.0");
            string message = $"[BuildMenu] {options.target} {summary.result} — {sizeMb} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings, "
                             + $"{summary.totalTime.TotalSeconds:0}s → {Path.GetFullPath(options.locationPathName)}";
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }
        }
    }
}
