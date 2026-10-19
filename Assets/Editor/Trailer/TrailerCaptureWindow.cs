using System.IO;
using UnityEditor;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools.Trailer
{
    /// <summary>
    /// 트레일러 촬영 창(2026-10-19, Tools > Absorb > 트레일러 촬영). 샷 번호(파일 접두어) 선택, 수동 녹화 시작/정지(F9와 같음),
    /// 녹화 상태·마지막 파일, 자동 샷 5개(5-1·6-1·6-3·6-4·7-2) 실행/취소, 몽타주 자동 조작 샷 9개(2-1~2-9, 테이크 수 지정) 실행/취소,
    /// 2차 자동 조작 샷(촬영 준비 · 1-1·1-2/1-3·3-1·3-2·5-2·6-2·6-5·7-1 · 나머지 전부) 실행/취소. 샷 번호는 docs/06_작업방식/발표/트레일러_촬영_목록.md 기준.
    /// 자세한 내용: docs/04_구현위키/트레일러_촬영_도구.md
    /// </summary>
    public class TrailerCaptureWindow : EditorWindow
    {
        private const string PrefixKey = "Absorb.Trailer.ShotPrefix";
        private const string TakesKey = "Absorb.Trailer.MontageTakes";
        private const double RepaintInterval = 0.25;

        private static readonly string[] ShotPrefixes =
        {
            "manual",
            "1-1", "1-2", "1-3",
            "2-1", "2-2", "2-3", "2-4", "2-5", "2-6", "2-7", "2-8", "2-9",
            "3-1", "3-2",
            "4-1", "4-2", "4-3", "4-4", "4-5",
            "5-1", "5-2",
            "6-1", "6-2", "6-3", "6-4", "6-5",
            "7-1", "7-2",
        };

        private static readonly TrailerShotRunner.Shot[] AutoShots =
        {
            TrailerShotRunner.Shot.FloorTiles,
            TrailerShotRunner.Shot.BossEntrance,
            TrailerShotRunner.Shot.Quake,
            TrailerShotRunner.Shot.EnrageRoar,
            TrailerShotRunner.Shot.Title,
        };

        private double _lastRepaint;

        /// <summary>수동 녹화(F9·창 버튼) 파일 접두어. 에디터 세션 동안 유지.</summary>
        public static string SelectedPrefix
        {
            get => SessionState.GetString(PrefixKey, "manual");
            private set => SessionState.SetString(PrefixKey, value);
        }

        /// <summary>몽타주 테이크 수(기본 3). 에디터 세션 동안 유지.</summary>
        private static int MontageTakes
        {
            get => SessionState.GetInt(TakesKey, TrailerShotRunner.DefaultTakes);
            set => SessionState.SetInt(TakesKey, value);
        }

        [MenuItem("Tools/Absorb/트레일러 촬영")]
        public static void Open()
        {
            GetWindow<TrailerCaptureWindow>("트레일러 촬영");
        }

        private void OnEnable()
        {
            EditorApplication.update += RepaintWhileBusy;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            wantsMouseEnterLeaveWindow = true; // 창에 마우스를 올리면 버튼 활성 상태가 다시 그려진다(평소엔 주기 Repaint 없음)
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhileBusy;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        // 녹화·시퀀스 중에만 주기적으로 다시 그린다(경과 시간·단계 표시). 그 외에는 Play 상태 변화 때만.
        private void RepaintWhileBusy()
        {
            bool busy = TrailerRecorder.IsRecording || TrailerShotRunner.IsRunning;
            if (!busy && !_wasBusy)
            {
                return;
            }

            _wasBusy = busy;
            if (EditorApplication.timeSinceStartup - _lastRepaint >= RepaintInterval || !busy)
            {
                _lastRepaint = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private bool _wasBusy;

        private void OnPlayModeChanged(PlayModeStateChange change) => Repaint();

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseEnterWindow)
            {
                Repaint();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Play 중 Game View에 포커스를 두고 F9 = 수동 녹화 시작/정지. 1920x1080 · 60fps · MP4(H.264) · 오디오 포함.\n" +
                "녹화 전 F1 디버그 패널을 닫는다(자동 샷은 알아서 닫음). 2인·전투 액션 샷은 수동 녹화로 찍는다.",
                MessageType.Info);

            DrawManualSection();
            EditorGUILayout.Space(8);
            DrawAutoSection();
            EditorGUILayout.Space(8);
            DrawMontageSection();
            EditorGUILayout.Space(8);
            DrawExtraSection();
        }

        private void DrawManualSection()
        {
            EditorGUILayout.LabelField("수동 녹화", EditorStyles.boldLabel);

            int index = System.Array.IndexOf(ShotPrefixes, SelectedPrefix);
            if (index < 0)
            {
                index = 0;
            }

            using (new EditorGUI.DisabledScope(TrailerRecorder.IsRecording))
            {
                int next = EditorGUILayout.Popup("샷 번호(파일 접두어)", index, ShotPrefixes);
                if (next != index)
                {
                    SelectedPrefix = ShotPrefixes[next];
                }
            }

            bool recording = TrailerRecorder.IsRecording;
            if (recording)
            {
                var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.25f, 0.25f) } };
                EditorGUILayout.LabelField($"● 녹화 중  {TrailerRecorder.Elapsed:0.0}초", style);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || recording || TrailerShotRunner.IsRunning))
                {
                    if (GUILayout.Button("녹화 시작 (F9)"))
                    {
                        TrailerRecorder.StartRecording(SelectedPrefix);
                    }
                }

                using (new EditorGUI.DisabledScope(!recording || TrailerShotRunner.IsRunning))
                {
                    if (GUILayout.Button("녹화 정지 (F9)"))
                    {
                        TrailerRecorder.StopRecording();
                    }
                }
            }

            string last = TrailerRecorder.LastFilePath;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("마지막 파일", string.IsNullOrEmpty(last) ? "-" : Path.GetFileName(last));
                if (GUILayout.Button("폴더 열기", GUILayout.Width(80)))
                {
                    Directory.CreateDirectory(TrailerRecorder.OutputDirectory);
                    if (!string.IsNullOrEmpty(last) && File.Exists(last))
                    {
                        EditorUtility.RevealInFinder(last);
                    }
                    else
                    {
                        EditorUtility.RevealInFinder(TrailerRecorder.OutputDirectory + Path.DirectorySeparatorChar);
                    }
                }
            }
        }

        private void DrawAutoSection()
        {
            EditorGUILayout.LabelField("자동 샷", EditorStyles.boldLabel);

            if (TrailerShotRunner.IsRunning)
            {
                EditorGUILayout.HelpBox($"실행 중: {TrailerShotRunner.RunningName} — {TrailerShotRunner.Status}", MessageType.None);
                if (GUILayout.Button("취소 (녹화 정지 + 무적 복구)"))
                {
                    TrailerShotRunner.Cancel();
                }

                return;
            }

            string warning = TrailerShotRunner.IsHostReady ? TrailerShotRunner.FloorTilesWarning() : null;
            if (warning != null)
            {
                EditorGUILayout.HelpBox($"5-1: {warning}", MessageType.Warning);
            }

            foreach (TrailerShotRunner.Shot shot in AutoShots)
            {
                string reason = TrailerShotRunner.CannotRunReason(shot);
                using (new EditorGUI.DisabledScope(reason != null))
                {
                    var content = new GUIContent($"{TrailerShotRunner.ShotCode(shot)} {TrailerShotRunner.ShotTitle(shot)}", reason ?? "");
                    if (GUILayout.Button(content))
                    {
                        TrailerShotRunner.Run(shot);
                    }
                }

                if (reason != null && EditorApplication.isPlaying)
                {
                    EditorGUILayout.LabelField($"   └ {reason}", EditorStyles.miniLabel);
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("자동 샷은 Play 중에만 쓸 수 있다. 5-1·6-x는 호스트 시작 후, 7-2는 타이틀 화면에서.", MessageType.None);
            }
        }

        private void DrawMontageSection()
        {
            EditorGUILayout.LabelField("몽타주 (자동 조작)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "넓은 빈 방 가운데에 서서 실행. 특성 부여 → 몹 소환 → 녹화 → 가상 키보드·마우스로 정해진 입력 → 정지를 테이크 수만큼.\n" +
                "촬영 중 실제 마우스는 잠시 꺼지고(끝나면 복구) 키보드는 만지지 않는다. 파일: <샷>_t<테이크>_<시각>.mp4",
                MessageType.None);

            if (TrailerShotRunner.IsRunning)
            {
                EditorGUILayout.HelpBox($"실행 중: {TrailerShotRunner.RunningName} — {TrailerShotRunner.Status}", MessageType.None);
                if (GUILayout.Button("취소 (녹화 정지 + 입력·몹·무적 정리)"))
                {
                    TrailerShotRunner.Cancel();
                }

                return;
            }

            int takes = EditorGUILayout.IntSlider("테이크 수", MontageTakes, 1, 10);
            if (takes != MontageTakes)
            {
                MontageTakes = takes;
            }

            string allReason = null;
            foreach (TrailerShotRunner.Shot shot in TrailerShotRunner.MontageShots)
            {
                allReason = TrailerShotRunner.CannotRunReason(shot);
                if (allReason != null)
                {
                    break;
                }
            }

            using (new EditorGUI.DisabledScope(allReason != null))
            {
                var content = new GUIContent($"몽타주 전부 촬영 (9샷 × {takes})", allReason ?? "");
                if (GUILayout.Button(content, GUILayout.Height(26)))
                {
                    TrailerShotRunner.RunMontageAll(takes);
                }
            }

            if (allReason != null && EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField($"   └ {allReason}", EditorStyles.miniLabel);
            }

            const int columns = 3;
            for (int i = 0; i < TrailerShotRunner.MontageShots.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + columns && j < TrailerShotRunner.MontageShots.Length; j++)
                    {
                        TrailerShotRunner.Shot shot = TrailerShotRunner.MontageShots[j];
                        string reason = TrailerShotRunner.CannotRunReason(shot);
                        using (new EditorGUI.DisabledScope(reason != null))
                        {
                            var content = new GUIContent($"{TrailerShotRunner.ShotCode(shot)} {TrailerShotRunner.ShotTitle(shot)}", reason ?? "");
                            if (GUILayout.Button(content))
                            {
                                TrailerShotRunner.RunMontage(shot, takes);
                            }
                        }
                    }
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("몽타주는 Play 중 호스트 시작 후에만 쓸 수 있다.", MessageType.None);
            }
        }

        private void DrawExtraSection()
        {
            EditorGUILayout.LabelField("2차 샷 (자동 조작)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "촬영 준비 = 보스 방이 아닌 가장 넓은 방 가운데로 이동 → 방 몹 처치 → 주변 20칸 몬스터·시체 숨김(몽타주 전에도 쓸 수 있음).\n" +
                "1-1~5-2는 시작 전에 촬영 준비를 자동으로 한다. 3-2는 무적을 잠깐 끈다. 6-5·7-1은 1테이크, 7-1은 판이 끝나므로 마지막에.",
                MessageType.None);

            if (TrailerShotRunner.IsRunning)
            {
                EditorGUILayout.HelpBox($"실행 중: {TrailerShotRunner.RunningName} — {TrailerShotRunner.Status}", MessageType.None);
                if (GUILayout.Button("취소 (녹화 정지 + 입력·몹·속성·무적·숨김 정리)"))
                {
                    TrailerShotRunner.Cancel();
                }

                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string prepareReason = TrailerShotRunner.CannotPrepareReason();
                using (new EditorGUI.DisabledScope(prepareReason != null))
                {
                    if (GUILayout.Button(new GUIContent("촬영 준비", prepareReason ?? "")))
                    {
                        TrailerShotRunner.PrepareCleanRoom();
                    }
                }

                int hidden = EditorApplication.isPlaying ? TrailerShotRunner.HiddenCount : 0;
                using (new EditorGUI.DisabledScope(hidden == 0))
                {
                    if (GUILayout.Button($"숨김 되돌리기 ({hidden})", GUILayout.Width(130)))
                    {
                        TrailerShotRunner.RestoreHidden();
                    }
                }
            }

            int takes = MontageTakes; // 테이크 수는 몽타주 섹션 슬라이더와 같이 쓴다
            const int columns = 3;
            TrailerShotRunner.Shot[] shots = TrailerShotRunner.ExtraShots;
            for (int i = 0; i < shots.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + columns && j < shots.Length; j++)
                    {
                        TrailerShotRunner.Shot shot = shots[j];
                        string reason = TrailerShotRunner.CannotRunReason(shot);
                        using (new EditorGUI.DisabledScope(reason != null))
                        {
                            var content = new GUIContent($"{TrailerShotRunner.ShotCode(shot)} {TrailerShotRunner.ShotTitle(shot)}", reason ?? "");
                            if (GUILayout.Button(content))
                            {
                                TrailerShotRunner.RunExtra(shot, takes);
                            }
                        }
                    }
                }
            }

            // 일반 방 샷이 하나라도 안 되면 막고, 보스·승리 샷은 못 찍으면 건너뛴다(RunExtraAll과 같은 기준).
            string allReason = null;
            foreach (TrailerShotRunner.Shot shot in shots)
            {
                if (TrailerShotRunner.IsRoomShot(shot))
                {
                    allReason = TrailerShotRunner.CannotRunReason(shot);
                    if (allReason != null)
                    {
                        break;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(allReason != null))
            {
                var content = new GUIContent($"나머지 전부 촬영 (촬영 준비 → 1-1 … 7-1, ×{takes})", allReason ?? "");
                if (GUILayout.Button(content, GUILayout.Height(26)))
                {
                    TrailerShotRunner.RunExtraAll(takes);
                }
            }

            if (allReason != null && EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField($"   └ {allReason}", EditorStyles.miniLabel);
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("2차 샷은 Play 중 호스트 시작 후에만 쓸 수 있다.", MessageType.None);
            }
        }
    }
}
