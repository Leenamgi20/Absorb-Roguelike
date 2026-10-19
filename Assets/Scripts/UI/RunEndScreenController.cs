using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Network;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 런 종료(승리/패배) 전체 화면. 로컬 플레이어가 죽으면 '패배', 보스 처치 등은 <see cref="ShowResult"/>로 '승리'.
    /// 레이아웃은 UIBuilder로 코드 생성(2026-10-02): 검은 배경, 위 제목, 가운데 빈 자리(대표 이미지 예정),
    /// 기록 줄(<see cref="AddStatRow"/>, 아직 호출 안 함), 아래 '다시 하기'/'타이틀로' 버튼.
    /// '다시 하기'는 호스트만 — 같은 씬을 네트워크로 다시 불러온다(ServerChangeScene).
    /// </summary>
    public class RunEndScreenController : MonoBehaviour
    {
        private static RunEndScreenController _instance;
        private bool _shown;

        private GameObject _root;
        private Text _titleText;
        private Text _messageText;
        private Transform _statsRoot;
        private Button _restartButton;
        private Text _waitingText;
        private Button _titleButton;

        /// <summary>런 종료 화면이 떠 있는지(ESC 메뉴 차단·플레이어 입력 차단에 사용).</summary>
        public static bool IsShown => _instance != null && _instance._shown;
        /// <summary>떠 있는 결과 화면이 승리인가(배경음악 선택용).</summary>
        public static bool IsVictory => _instance != null && _instance._victory;

        private void Awake()
        {
            _instance = this;
            _runStartTime = Time.time; // 재시작(씬 재로드)이면 접속 중이라 LateUpdate가 갱신하지 않으므로 여기서 시작
            Build();
            UIBuilder.ApplyFont(transform);
            _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Build()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "RunEndCanvas", 44);
            // 프리팹 루트 캔버스 안에 들어가므로 자체 정렬을 켜서 HUD·파티 칸 등 다른 화면 UI보다 확실히 위에 그린다(2026-10-07).
            canvas.overrideSorting = true;
            canvas.sortingOrder = 90;
            _root = canvas.gameObject;
            GameObject bg = UIBuilder.AddFullPanel(canvas.transform, "Background", new Color(0f, 0f, 0f, 1f));

            // 2026-10-06 도트 UI 키트: 결과 목록을 패널 위에(배경색을 주면 AddColumn이 패널 그림·여백을 입힌다). 검은 전체 배경은 그대로.
            Transform column = UIBuilder.AddColumn(bg.transform, "Column", new Vector2(720f, 640f), UIBuilder.BoxColor);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;

            _titleText = UIBuilder.AddText(column, "", 64, Color.white, 96f);
            _titleText.fontStyle = FontStyle.Bold;
            _messageText = UIBuilder.AddText(column, "", 20, UIBuilder.SubTextColor, 30f);

            // 가운데 빈 영역(대표 이미지/일러스트 자리 — 비워 둠).
            var spacer = new GameObject("CenterSlot", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(column, false);
            spacer.GetComponent<LayoutElement>().preferredHeight = 240f;

            // 기록 줄 컨테이너(생존 시간·도달 층 등 — 유저와 논의 후 채움).
            var stats = new GameObject("Stats", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            stats.transform.SetParent(column, false);
            var statsImage = stats.GetComponent<Image>();
            statsImage.color = Color.clear; // 키트가 없으면 예전처럼 투명
            statsImage.raycastTarget = false;
            UIBuilder.SkinBox(statsImage); // 기록 줄 = 파인 칸
            var statsLayout = stats.GetComponent<VerticalLayoutGroup>();
            statsLayout.padding = new RectOffset(12, 12, 10, 10); // 칸 테두리(4px × 2단위) 안쪽
            statsLayout.spacing = 4f;
            statsLayout.childControlWidth = true;
            statsLayout.childControlHeight = true;
            statsLayout.childForceExpandHeight = false;
            stats.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _statsRoot = stats.transform;

            Transform buttons = UIBuilder.AddRow(column, "Buttons", 52f, 24f);
            buttons.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            _restartButton = UIBuilder.AddButtonKey(buttons, "result.restart", OnRestartClicked, 52f, 220f);
            _waitingText = UIBuilder.AddTextKey(buttons, "result.waiting_host", 15, UIBuilder.SubTextColor, 52f, 220f);
            _titleButton = UIBuilder.AddButtonKey(buttons, "result.to_title", OnTitleClicked, 52f, 220f);
        }

        /// <summary>보스 처치 등 승리 종료(기존 시그니처 유지 — title은 무시하고 '승리'로 표시).
        /// message는 일반 글자 또는 Loc.Pack 문자열(언어를 바꾸면 결과 화면을 다시 그릴 때 새 언어로).</summary>
        public static void ShowResult(string title, string message)
        {
            if (_instance != null)
            {
                _instance.Show(true, message);
            }
        }

        private void Update()
        {
            if (_shown || NetworkClient.localPlayer == null)
            {
                return;
            }

            // 패배 판정(2026-10-07 쓰러짐·부활): 살아 있는 플레이어가 한 명도 없을 때. 솔로는 내가 죽으면 곧바로(기존과 같음),
            // 멀티는 쓰러진 사람(IsDead = 체력 0)은 팀원이 부활시킬 수 있으므로 전원 쓰러졌을 때만. 접속이 끊겨 플레이어 오브젝트가
            // 사라지면 남은 인원으로 매 프레임 다시 센다(동기화 값만 읽어 모든 화면이 같은 결과).
            var localHealth = NetworkClient.localPlayer.GetComponent<PlayerHealth>();
            if (localHealth == null || !localHealth.IsDead)
            {
                return; // 내가 살아 있으면 전원 쓰러짐일 수 없다 — 전체 스캔 생략
            }

            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (!health.IsDead)
                {
                    return;
                }
            }

            Show(false, Loc.Pack("result.defeated_message"));
        }

        public void Show(bool victory, string message = null)
        {
            _shown = true;
            _victory = victory;
            _message = message;
            _survivedSeconds = Time.time - _runStartTime;
            var dgm = AbsorbRoguelike.Dungeon.FloorProgression.Instance;
            _floor = dgm != null ? dgm.Floor : -1;
            _maxFloors = dgm != null ? dgm.MaxFloors : -1;
            DrawTexts();

            bool isHost = NetworkServer.active;
            _restartButton.gameObject.SetActive(isHost);
            _waitingText.gameObject.SetActive(!isHost);
            _root.SetActive(true);
            SetWorldHidden(true);
            (isHost ? _restartButton : _titleButton).Select();
        }

        // 결과 값은 Show 때 고정하고, 글자는 언어가 바뀌면 다시 그린다(Loc.Changed).
        private bool _victory;
        private string _message;
        private float _survivedSeconds;
        private int _floor;
        private int _maxFloors;

        private void OnEnable()
        {
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDisable()
        {
            Loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_shown)
            {
                DrawTexts();
            }
        }

        private void DrawTexts()
        {
            ClearStats();
            AddStatRow(Loc.Get("result.stat_time"), FormatTime(_survivedSeconds));
            if (_maxFloors > 0)
            {
                AddStatRow(Loc.Get("result.stat_floor"), _victory
                    ? Loc.Get("result.floor_cleared", _maxFloors)
                    : Loc.Get("result.floor_reached", _floor, _maxFloors));
            }

            string message = Loc.Unpack(_message);
            _titleText.text = Loc.Get(_victory ? "result.victory" : "result.defeat");
            _titleText.color = _victory ? UIBuilder.SectionColor : UIBuilder.WarningColor;
            _messageText.text = message ?? "";
            _messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        // 생존 시간 기준점: 접속 전(타이틀)에는 계속 갱신하고, 세션이 시작되면 멈춘다. Time.time이라 혼자 일시정지한 시간은 빠진다.
        private float _runStartTime;

        private void LateUpdate()
        {
            if (!NetworkClient.isConnected)
            {
                _runStartTime = Time.time;
                // 타이틀로 나가도 씬은 다시 불러오지 않으므로(offlineScene 없음) 결과 화면을 직접 닫는다 — 안 그러면 새 게임에 전판 결과가 남는다
                if (_shown) { Hide(); }
            }
        }

        private void Hide()
        {
            _shown = false;
            ClearStats();
            _root.SetActive(false);
            SetWorldHidden(false);
        }

        // 결과 화면 동안 게임 카메라가 아무것도 그리지 않게 하고 검정으로만 지운다(2026-10-07 사용자 요청 — 승리·패배 창 뒤로
        // 맵·움직이는 몬스터·무기 방향이 비쳐 보이던 문제). 다시 시작은 씬을 새로 불러 원래대로 돌아가지만, 숨김을 풀 때도 되돌린다.
        private int _savedCullingMask;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackground;
        private Camera _hiddenCamera;

        private void SetWorldHidden(bool hidden)
        {
            if (hidden)
            {
                Camera cam = Camera.main;
                if (cam == null || _hiddenCamera != null)
                {
                    return;
                }

                _hiddenCamera = cam;
                _savedCullingMask = cam.cullingMask;
                _savedClearFlags = cam.clearFlags;
                _savedBackground = cam.backgroundColor;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
            else if (_hiddenCamera != null)
            {
                _hiddenCamera.cullingMask = _savedCullingMask;
                _hiddenCamera.clearFlags = _savedClearFlags;
                _hiddenCamera.backgroundColor = _savedBackground;
                _hiddenCamera = null;
            }
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 3600).ToString("00") + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        /// <summary>기록 한 줄(라벨 - 값) 추가.</summary>
        public void AddStatRow(string label, string value)
        {
            Transform row = UIBuilder.AddRow(_statsRoot, "Stat_" + label, 28f, 16f);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Text l = UIBuilder.AddText(row, label, 18, UIBuilder.SubTextColor, 28f, 200f);
            l.alignment = TextAnchor.MiddleRight;
            Text v = UIBuilder.AddText(row, value, 18, Color.white, 28f, 200f);
            v.alignment = TextAnchor.MiddleLeft;
        }

        public void ClearStats()
        {
            for (int i = _statsRoot.childCount - 1; i >= 0; i--)
            {
                GameObject row = _statsRoot.GetChild(i).gameObject;
                row.SetActive(false); // 같은 프레임에 다시 그릴 때(언어 변경) 지워질 줄이 레이아웃에 남지 않게
                Destroy(row);
            }
        }

        private void OnRestartClicked()
        {
            if (NetworkServer.active)
            {
                Time.timeScale = 1f;
                NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
            }
        }

        private void OnTitleClicked()
        {
            Time.timeScale = 1f;
            StartMenuController startMenu = StartMenuController.Instance;
            if (startMenu != null)
            {
                startMenu.LeaveSessionToTitle();
            }
        }
    }
}
