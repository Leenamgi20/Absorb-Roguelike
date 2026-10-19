using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbsorbRoguelike.EditorTools.Trailer
{
    /// <summary>
    /// 트레일러 촬영 도구(2026-10-19) — Play 중 F9로 수동 녹화 시작/정지 토글. 에디터 전용이고 PlayerControls 입력 에셋에는
    /// 등록하지 않는다(EditorApplication.update에서 Keyboard.current를 직접 읽음). 파일 접두어는 촬영 창에서 고른 샷 번호(기본 manual).
    /// 자동 샷 시퀀스가 진행 중이면 F9를 무시한다. Game View에 포커스가 있어야 키 입력이 들어온다.
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerHotkey
    {
        static TrailerHotkey()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f9Key.wasPressedThisFrame)
            {
                return;
            }

            // 같은 프레임에 update가 여러 번 불려도 한 번만 처리한다.
            int frame = Time.frameCount;
            if (frame == _lastHandledFrame)
            {
                return;
            }

            _lastHandledFrame = frame;

            if (TrailerShotRunner.IsRunning)
            {
                Debug.Log("[Trailer] 자동 샷 진행 중 — F9 무시.");
                return;
            }

            if (TrailerRecorder.IsRecording)
            {
                TrailerRecorder.StopRecording();
            }
            else
            {
                TrailerRecorder.StartRecording(TrailerCaptureWindow.SelectedPrefix);
            }
        }

        private static int _lastHandledFrame = -1;
    }
}
