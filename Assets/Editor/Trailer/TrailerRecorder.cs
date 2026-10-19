using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools.Trailer
{
    /// <summary>
    /// 트레일러 촬영 도구(2026-10-19) — 녹화 시작/정지 API. Unity Recorder(com.unity.recorder)로 Game View를
    /// 1920x1080, 60fps 고정(FrameRatePlayback.Constant + CapFrameRate), H.264 MP4, 오디오 포함으로 녹화한다.
    /// 출력: &lt;프로젝트 루트&gt;/Recordings/Trailer/&lt;샷접두어&gt;_&lt;yyyyMMdd_HHmmss&gt;.mp4 (Recordings/ 는 git 제외).
    /// 테이크 번호를 주면(몽타주 자동 조작 샷) &lt;샷접두어&gt;_t&lt;테이크&gt;_&lt;yyyyMMdd_HHmmss&gt;.mp4 (예: 2-2_t1_20261019_153000.mp4).
    /// 에디터 전용 — 런타임 코드(Assets/Scripts)는 이 클래스를 참조하지 않는다. 자세한 내용: docs/04_구현위키/트레일러_촬영_도구.md
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerRecorder
    {
        public const int Width = 1920;
        public const int Height = 1080;
        public const float FrameRate = 60f;

        private static RecorderController _controller;
        private static RecorderControllerSettings _settings;
        private static double _startedAt;

        /// <summary>지금 녹화 중인지.</summary>
        public static bool IsRecording => _controller != null && _controller.IsRecording();

        /// <summary>녹화 시작 후 경과 시간(초, 에디터 시간 기준). 녹화 중이 아니면 0.</summary>
        public static double Elapsed => IsRecording ? EditorApplication.timeSinceStartup - _startedAt : 0d;

        /// <summary>마지막으로 시작한 녹화의 파일 경로(.mp4). 아직 없으면 null.</summary>
        public static string LastFilePath { get; private set; }

        /// <summary>출력 폴더(프로젝트 루트/Recordings/Trailer).</summary>
        public static string OutputDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Trailer"));

        static TrailerRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>녹화를 시작한다(Play 중일 때만). take가 1 이상이면 파일 이름에 _t&lt;take&gt;를 붙인다. 성공하면 true.</summary>
        public static bool StartRecording(string shotPrefix, int take = 0)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Trailer] Play 중이 아니라 녹화를 시작하지 않는다.");
                return false;
            }

            if (IsRecording)
            {
                Debug.LogWarning("[Trailer] 이미 녹화 중이다.");
                return false;
            }

            if (AbsorbRoguelike.UI.DebugMenuController.IsOpen)
            {
                Debug.LogWarning("[Trailer] F1 패널이 열려 있음 — 디버그 메뉴가 영상에 찍힌다.");
            }

            string prefix = string.IsNullOrWhiteSpace(shotPrefix) ? "manual" : SanitizeFileName(shotPrefix);
            Directory.CreateDirectory(OutputDirectory);
            string takePart = take > 0 ? $"_t{take}" : "";
            string fileNoExt = Path.Combine(OutputDirectory, $"{prefix}{takePart}_{DateTime.Now:yyyyMMdd_HHmmss}");

            Cleanup();
            _settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            _settings.hideFlags = HideFlags.HideAndDontSave;

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.hideFlags = HideFlags.HideAndDontSave;
            movie.name = "Trailer Movie";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.CaptureAudio = true;
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = Width,
                OutputHeight = Height,
            };
            movie.OutputFile = fileNoExt; // 확장자(.mp4)는 Recorder가 붙인다

            _settings.AddRecorderSettings(movie);
            _settings.SetRecordModeToManual();
            _settings.FrameRatePlayback = FrameRatePlayback.Constant;
            _settings.FrameRate = FrameRate;
            _settings.CapFrameRate = true;
            _settings.ExitPlayMode = false;

            _controller = new RecorderController(_settings);
            _controller.PrepareRecording();
            if (!_controller.StartRecording())
            {
                Debug.LogError("[Trailer] 녹화 시작 실패(Recorder 설정 오류 — 콘솔의 Recorder 로그 확인).");
                Cleanup();
                return false;
            }

            _startedAt = EditorApplication.timeSinceStartup;
            LastFilePath = fileNoExt + ".mp4";
            Debug.Log($"[Trailer] 녹화 시작: {LastFilePath}");
            return true;
        }

        /// <summary>녹화를 멈추고 파일을 마무리한다. 녹화 중이 아니면 아무것도 안 한다.</summary>
        public static void StopRecording()
        {
            if (_controller == null)
            {
                return;
            }

            bool wasRecording = _controller.IsRecording();
            if (wasRecording)
            {
                _controller.StopRecording();
                Debug.Log($"[Trailer] 녹화 정지: {LastFilePath} ({EditorApplication.timeSinceStartup - _startedAt:0.0}초)");
            }

            Cleanup();
        }

        private static void Cleanup()
        {
            _controller = null;
            if (_settings != null)
            {
                foreach (RecorderSettings recorder in _settings.RecorderSettings)
                {
                    UnityEngine.Object.DestroyImmediate(recorder);
                }

                UnityEngine.Object.DestroyImmediate(_settings);
                _settings = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && IsRecording)
            {
                Debug.Log("[Trailer] Play 종료 — 녹화를 자동 정지한다.");
                StopRecording();
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value.Replace(' ', '_');
        }
    }
}
