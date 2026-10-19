using AbsorbRoguelike.Network;
using Mirror;
using AbsorbRoguelike.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// ESC 일시정지/메뉴(2026-09-30, 타이틀·일시정지·옵션) — 코드로 만드는 임시 uGUI. NetworkManager 오브젝트(DontDestroyOnLoad)에 붙는다.
    ///
    /// ESC 우선순위(매 프레임 한 번만 처리):
    ///   ① 옵션 창이 열려 있거나 이번 프레임에 닫혔으면 무시(옵션이 자체 처리) ② 이 메뉴가 열려 있으면 닫기(재개)
    ///   ③ 디버그 메뉴(F1)가 열려 있으면 그것만 닫기 ④ 보스 속성 선택 창이 떠 있으면 열지 않음(선택 창 동작 그대로)
    ///   ⑤ 런 종료 화면·타이틀(시작 메뉴 패널)이 보이거나 접속 중이 아니면 열지 않음 ⑥ 그 외 열기.
    ///   (①' 조작법 표가 열려 있거나 이번 프레임에 닫혔으면 무시 — 표가 자체 처리, 2026-10-16)
    ///
    /// 일시정지 규칙: 1인 세션(호스트이고 연결이 호스트 자신 하나뿐)일 때만 Time.timeScale = 0. 협동(원격 플레이어 있음·클라이언트)은
    /// 게임을 멈추지 않고 메뉴만 띄운다. 열려 있는 동안 매 프레임 다시 판정 — 원격 플레이어가 들어오면 즉시 timeScale 1로 돌린다.
    /// 닫기·비활성·파괴·씬 로드·연결 끊김 때 timeScale 1 보장.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        private const int CanvasSortOrder = 45; // 시작 메뉴(40) 위, 옵션(50) 아래

        private static PauseMenuController _instance;

        /// <summary>일시정지/메뉴 창이 열려 있는지(열려 있는 동안 로컬 입력 차단).</summary>
        public static bool IsOpen { get; private set; }

        private GameObject _root;
        private Text _titleText;
        private Text _coopNoteText;
        private Button _restartButton;
        private bool _pausedTime;       // 이 메뉴가 timeScale을 0으로 만든 상태
        private bool _optionsFromPause; // 이 메뉴에서 연 옵션 창

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                enabled = false; // 씬 재로드 때 생기는 중복 NetworkManager 쪽 — 곧 파괴된다
                return;
            }

            _instance = this;
            Build();
            _root.SetActive(false);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                ForceClose();
            }
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            ForceClose();
            _instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ForceClose(); // 재시작(ServerChangeScene) 등으로 씬이 바뀌면 메뉴를 닫고 시간을 돌린다
        }

        private void Update()
        {
            if (IsOpen)
            {
                if (!NetworkClient.isConnected)
                {
                    ForceClose(); // 연결 끊김 — 시작 메뉴가 타이틀로 돌아간다
                    return;
                }

                RefreshMode();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            // ① 옵션 창이 처리
            if (OptionsMenuController.IsOpen || OptionsMenuController.LastClosedFrame == Time.frameCount)
            {
                return;
            }

            // ①' 조작법 표가 처리(표만 닫고 메뉴는 유지, 2026-10-16)
            if (ControlsTableOverlay.IsOpen || ControlsTableOverlay.LastClosedFrame == Time.frameCount)
            {
                return;
            }

            // ② 열려 있으면 재개
            if (IsOpen)
            {
                ForceClose();
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // ③ 디버그 메뉴만 닫기 — DebugMenuController는 개발 빌드에만 있다(배포 빌드 컴파일 에러 방지, QA #49)
            if (DebugMenuController.IsOpen)
            {
                DebugMenuController.CloseIfOpen();
                return;
            }
#endif

            // ④ 보스 속성 선택 중에는 열지 않음 ⑤ 런 종료 화면·타이틀·미접속
            if (BossChoiceUIController.IsOpen || RunEndScreenController.IsShown)
            {
                return;
            }

            StartMenuController startMenu = StartMenuController.Instance;
            if ((startMenu != null && startMenu.IsMenuVisible) || !NetworkClient.isConnected)
            {
                return;
            }

            // ⑥ 열기
            Open();
        }

        private void Open()
        {
            IsOpen = true;
            _root.SetActive(true);
            RefreshMode();
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiOpen); // 창 열기(2026-10-14)
        }

        /// <summary>메뉴를 닫고(옵션 창이 이 메뉴에서 열렸으면 그것도) timeScale을 1로 돌린다.</summary>
        private void ForceClose()
        {
            ControlsTableOverlay.Close(); // 조작법 표도 함께 닫는다(씬 로드·연결 끊김·타이틀 이동)
            if (_optionsFromPause)
            {
                _optionsFromPause = false;
                if (OptionsMenuController.IsOpen)
                {
                    OptionsMenuController.Close();
                }
            }

            if (_pausedTime)
            {
                _pausedTime = false;
                Time.timeScale = 1f;
            }

            if (IsOpen)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiOpen); // 창 닫기(열려 있을 때만)
            }

            IsOpen = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        // 1인 세션 판정 = 서버이고 연결이 호스트 로컬 하나뿐. 클라이언트는 항상 협동.
        private static bool IsSoloSession => NetworkServer.active && NetworkServer.connections.Count <= 1;

        private void RefreshMode()
        {
            bool solo = IsSoloSession;
            if (solo && !_pausedTime)
            {
                _pausedTime = true;
                Time.timeScale = 0f;
            }
            else if (!solo && _pausedTime)
            {
                _pausedTime = false;
                Time.timeScale = 1f; // 열려 있는 동안 원격 플레이어가 들어왔다
            }

            _titleText.text = Loc.Get(solo ? "pause.title_paused" : "pause.title_menu"); // 열려 있는 동안 매 프레임 갱신 — 언어 변경도 바로 반영
            if (_coopNoteText.gameObject.activeSelf == solo)
            {
                _coopNoteText.gameObject.SetActive(!solo);
            }

            bool host = NetworkServer.active;
            if (_restartButton.gameObject.activeSelf != host)
            {
                _restartButton.gameObject.SetActive(host); // 재시작은 호스트만(클라이언트에겐 숨김)
            }

            bool canRestart = host && CanRestartNow();
            if (_restartButton.interactable != canRestart)
            {
                _restartButton.interactable = canRestart;
            }
        }

        // 호스트 시작 직후 플레이어 스폰(AddPlayer)이 끝나기 전에 ServerChangeScene을 부르면 새 씬에서 AddPlayer가 한 번 더 와
        // Mirror "There is already a player for this connection." 에러가 난다 — 씬 로드 중이거나 아직 플레이어가 없는 연결이 있으면 막는다.
        private static bool CanRestartNow()
        {
            if (NetworkServer.isLoadingScene || NetworkClient.localPlayer == null)
            {
                return false;
            }

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn != null && conn.identity == null)
                {
                    return false;
                }
            }

            return true;
        }

        // ── 버튼 ──────────────────────────────────────────────────

        private void OnResumeClicked()
        {
            ForceClose();
        }

        private void OnOptionsClicked()
        {
            _optionsFromPause = true;
            OptionsMenuController.Open(() => _optionsFromPause = false); // 닫히면 이 메뉴가 그대로 보인다
        }

        private void OnControlsClicked()
        {
            ControlsTableOverlay.Open(null); // 닫히면 이 메뉴가 그대로 보인다
        }

        private void OnRestartClicked()
        {
            if (!NetworkServer.active || !CanRestartNow())
            {
                return;
            }

            ForceClose();
            // RunEndScreenController.OnRestartClicked와 같은 경로: 같은 씬을 네트워크로 다시 불러온다.
            NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
        }

        private void OnTitleClicked()
        {
            ForceClose();
            StartMenuController startMenu = StartMenuController.Instance;
            if (startMenu != null)
            {
                startMenu.LeaveSessionToTitle();
            }
        }

        private void OnQuitClicked()
        {
            ForceClose();
            StartMenuController startMenu = StartMenuController.Instance;
            if (startMenu != null)
            {
                startMenu.RequestQuit(); // EOS 로비 정리 후 종료(타이틀 '게임 종료'와 같은 경로)
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── UI 만들기 ─────────────────────────────────────────────

        private void Build()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "PauseMenuCanvas", CanvasSortOrder);
            _root = UIBuilder.AddFullPanel(canvas.transform, "PausePanel", new Color(0f, 0f, 0f, 0.6f));
            Transform column = UIBuilder.AddColumn(_root.transform, "PauseColumn", new Vector2(380f, 476f), UIBuilder.BoxColor);
            column.GetComponent<VerticalLayoutGroup>().spacing = 8f;

            _titleText = UIBuilder.AddText(column, Loc.Get("pause.title_paused"), 28, Color.white, 44f);
            _coopNoteText = UIBuilder.AddTextKey(column, "pause.coop_note", 14, UIBuilder.SubTextColor, 20f);
            UIBuilder.FitButtonLabel(_coopNoteText); // 영어가 길면 한 줄 안에서 축소
            UIBuilder.AddButtonKey(column, "pause.resume", OnResumeClicked, 44f);
            UIBuilder.AddButtonKey(column, "pause.options", OnOptionsClicked, 44f);
            UIBuilder.AddButtonKey(column, "pause.controls", OnControlsClicked, 44f); // 조작법 표(2026-10-16)
            _restartButton = UIBuilder.AddButtonKey(column, "pause.restart", OnRestartClicked, 44f);
            UIBuilder.AddButtonKey(column, "pause.to_title", OnTitleClicked, 44f);
#if !UNITY_WEBGL // 브라우저판: 탭을 닫을 수 없으니 '게임 종료' 버튼을 만들지 않는다
            UIBuilder.AddButtonKey(column, "pause.quit", OnQuitClicked, 44f);
#endif
        }
    }
}
