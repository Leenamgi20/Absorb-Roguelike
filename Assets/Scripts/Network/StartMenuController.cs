using System.Collections;
using System.Collections.Generic;
#if !UNITY_WEBGL
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
#endif
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
#if !UNITY_WEBGL
using Epic.OnlineServices.Lobby;
using EpicTransport;
#endif
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AbsorbRoguelike.Network
{
    /// <summary>
    /// 타이틀(2026-09-30): 메인 그룹 = '혼자 하기/같이 하기/옵션/게임 종료', 기존 방 코드·IP 버튼은 '같이 하기' 하위 그룹으로 옮김(기능 그대로).
    /// 시작 메뉴(2인 검증 준비, 2026-09-29 / EOS 방 코드 추가, 2026-09-30 eos-spike) — Mirror NetworkManagerHUD 대체.
    /// NetworkManager와 같은 오브젝트에 둔다. uGUI를 코드로 만든다(LegacyRuntime 폰트, 새 아트 없음).
    ///
    /// 전송은 두 개를 같은 오브젝트에 두고 버튼마다 고른다(<see cref="UseTransport"/>):
    ///   - 인터넷(방 코드): EOSTransport — '방 만들기'(6자리 코드로 EOS 로비 생성 → 내부에서 StartHost) / '방 코드로 참가'(JoinLobbyByID → 내부에서 StartClient).
    ///   - 같은 PC / LAN 테스트: KcpTransport — 기존 '호스트 시작' / 'IP로 참가'(마지막 IP PlayerPrefs 저장) 그대로.
    /// EOS 초기화(기기 ID 로그인)는 메뉴가 뜰 때 한 번 시작하고 상태를 폴링한다. EOS SDK 콜백 안에서 throw된 예외는
    /// 호출 쪽 try/catch에 잡히지 않으므로, 성공 여부는 제한 시간 + 상태 폴링으로 판정하고 실패 이유는
    /// Application.logMessageReceived로 받은 EOSSDKException의 "(Result.XXX)"에서 읽는다. 인증 값은 어디에도 로그로 쓰지 않는다.
    ///
    /// 끊김 감지는 상태 폴링으로 한다 — NetworkClient.OnDisconnectedEvent는 NetworkManager가 '='로 덮어써서 구독할 수 없다.
    /// 호스트 정보: Kcp 호스트면 오른쪽 아래에 LAN IPv4:포트, EOS 호스트면 대기 화면에 방 코드를 크게, 게임 중엔 오른쪽 아래에 작게 + '복사' 버튼.
    /// EOS 로비 정리 순서: 반드시 EOSTransport.LeaveLobby()를 StopHost보다 먼저 부른다(LeaveLobby는 NetworkServer.active일 때만 로비를 파괴,
    /// Transport.Shutdown은 로비를 정리하지 않음). NetworkShutdownGuard(종료 시 소켓·로비 정리)와 역할을 나눈다 — 이쪽은 시작·끊김 UI.
    ///
    /// 브라우저판(WebGL, 2026-10-16): 혼자 하기 전용. EOS 어셈블리가 WebGL에서 빠지므로 EOS 경로는 전부 #if !UNITY_WEBGL로 막고
    /// 같은 이름의 빈 스텁만 둔다. 혼자 하기는 listen=false 호스트(소켓 안 엶), 타이틀 '멀티플레이'·'게임 종료' 버튼은 만들지 않는다.
    /// 자세한 내용: docs/04_구현위키/브라우저판.md
    /// </summary>
    public class StartMenuController : MonoBehaviour
    {
        private const string LastJoinIpKey = "Absorb.LastJoinIp";
        private const string DefaultJoinIp = "127.0.0.1";
        private const int CanvasSortOrder = 40; // 런 종료 화면(20)·디버그 메뉴(30) 위 — 메뉴가 떠 있을 때는 맨 위

        // 방 코드: 헷갈리는 0/O/1/I를 뺀 대문자+숫자 32자에서 6자리(약 10억 가지). 전부 URL-safe 문자라 Helper.IsUrlSafe를 통과하고,
        // 길이 6은 EOS 로비 ID 오버라이드 길이 [LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH(4), MAX_LOBBYIDOVERRIDE_LENGTH(60)] 안이다.
        public const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int RoomCodeLength = 6;
        private const uint RoomMaxPlayers = 2;

        // 공개방 목록(2026-10-15): 로비는 모두 EOS 검색에 걸리는 Publicadvertised로 만들고, 꼬리표(로비 속성)로 공개/비공개를 나눈다.
        // 목록은 visibility=public인 로비만 검색한다 — 비공개방은 목록에 안 나오고 방 코드로만 들어간다.
        private const string VisibilityKey = "absorb_visibility";
        private const string VisibilityPublic = "public";
        private const string VisibilityPrivate = "private";
        private const string HostNameKey = "absorb_host";
        private const uint RoomSearchMaxResults = 50;
        private const int RoomListMaxRows = 6;        // 메뉴 높이 안에 들어가는 줄 수(스크롤 없음)
        private const float RoomSearchTimeout = 12f;  // SDK 콜백 예외 때는 콜백이 안 오므로 시간으로 실패 판정

        private const float OnlineInitTimeout = 15f;  // 이 안에 EOSManager.Initialized가 안 되면 실패
        private const float CreateRoomTimeout = 20f;  // 이 안에 로비+호스트가 안 서면 실패
        private const float JoinRoomTimeout = 35f;    // EOSTransport.timeout(25초) + 로비 참가 여유
        private const float QuitLobbyWait = 2f;       // '종료' 버튼: 로비 파괴 요청이 나갈 시간

        // 불투명 — 반투명이면 뒤의 HUD(체력바·Tab 지도 안내)가 타이틀에 비친다. 나중에 타이틀 이미지로 교체할 자리.
        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.3f, 0.95f);
        private static readonly Color InputColor = new Color(0.15f, 0.16f, 0.2f, 1f);
        private static readonly Color MessageColor = new Color(1f, 0.75f, 0.45f);
        private static readonly Color SectionColor = new Color(0.65f, 0.8f, 1f);
        private static readonly Color SubTextColor = new Color(0.8f, 0.8f, 0.85f);
        private static readonly Regex ResultRegex = new Regex(@"\(Result\.(\w+)\)", RegexOptions.Compiled);

        [Header("타이틀 배경 (2026-10-06)")]
        [Tooltip("메뉴 뒤 전체 화면 그림. 가운데(제목·버튼 열)는 비워 둔 일러스트. 비우면 단색 배경.")]
        [SerializeField] private Sprite titleBackground;
        [Tooltip("배경 위에 덮는 어둡기(0~1). 흰 글씨·버튼이 잘 읽히게.")]
        [SerializeField, Range(0f, 1f)] private float titleBackgroundDim = 0.25f;

        [Header("전송 — 비우면 같은 오브젝트/자식에서 찾는다")]
        [Tooltip("같은 PC/LAN(IP) 테스트용. 비우면 NetworkManager의 기본 transport(Kcp)")]
        [SerializeField] private Transport kcpTransport;
#if !UNITY_WEBGL
        [Tooltip("인터넷(방 코드)용")]
        [SerializeField] private EOSTransport eosTransport;

        [Header("EOS")]
        // EOS 접속 값은 git에 올리지 않는다(결정 ㉞A): 실제 설정은 git 제외 폴더 Assets/_Local/Resources/EOSConfig.asset에 두고
        // Resources.Load로 읽는다(빌드에 자동 포함). 없으면 인터넷 모드 버튼을 끈다. 견본: Assets/Data/EOSConfig.template.asset.
        private const string EOSConfigResourceName = "EOSConfig";
        private EOSConfig eosConfig;
#endif

        private enum NetMode { Kcp, Eos }
        private enum OnlineState { Disabled, Connecting, Ready, Failed }
        private enum Pending { None, Creating, Joining }

        private Font _font;
        private GameObject _menuPanel;
        private GameObject _mainGroup;   // 타이틀(혼자 하기/같이 하기/옵션/게임 종료)
        private GameObject _coopGroup;   // '같이 하기' 하위: 인터넷(방 코드) + 같은 네트워크(IP)
        private GameObject _connectingGroup;
        private GameObject _roomWaitGroup;
        private InputField _ipInput;
        private InputField _roomCodeInput;
        private Text _messageText;
        private Text _connectingText;
        private Text _onlineStatusText;
        private Text _roomCodeBigText;
        private Text _hostInfoText;
        private GameObject _roomCorner;
        private Text _roomCornerText;
        private Button _createRoomButton;
        private Button _createPrivateRoomButton;
        private Button _roomListButton;
        private Button _joinRoomButton;
        private Text _roomOpenedText;
        private GameObject _roomListGroup;
        private Transform _roomListRows;
        private Text _roomListStatusText;
        private string _roomListStatusPayload;
        private Button _roomListRefreshButton;

        // 방 목록 검색 상태. 검색 결과 핸들(LobbyDetails)은 목록을 비울 때 Release한다(참가에 쓴 것만 전송이 가져간다).
#if !UNITY_WEBGL
        private readonly List<LobbyDetails> _foundLobbies = new List<LobbyDetails>();
#endif
        private bool _searching;
        private float _searchStartTime;
        private int _searchToken;          // 뒤로·새로고침 뒤 늦게 온 검색 콜백을 버린다
        private bool _pendingPublic;       // 만들고 있는 방이 공개방인지
        private bool _roomIsPublic;        // 지금 호스트 중인 방이 공개방인지

        private bool _wasClientActive;
        private bool _wasConnected;
        private bool _wasHost;
        private float _hostInfoRefreshTimer;

        private NetMode _mode = NetMode.Kcp;
        private OnlineState _onlineState = OnlineState.Disabled;
        private string _onlineReason;
        private string _messagePayload;   // 언어 변경 때 다시 쓰기용(Loc.Pack 문자열)
        private string _connectingPayload;
        private float _onlineStartTime;

        private Pending _pending = Pending.None;
        private float _pendingStartTime;
        private string _pendingCode;
        private string _pendingErrorResult; // 진행 중 작업 동안 로그로 받은 EOSSDKException의 Result 이름
        private bool _pendingSawClient;
        private bool _discardLateLobby;    // 취소·실패 뒤 늦게 도착한 로비 콜백(StartHost/StartClient)을 되돌린다

        private string _roomCode;          // 지금 호스트 중인 EOS 방 코드
        private bool _showRoomWait;        // 방을 만든 직후 대기 화면(코드 크게)
        private bool _quitting;
        private bool _leftVoluntarily;     // 일시정지 메뉴 '타이틀로' — 끊김 안내 문구를 띄우지 않는다

        /// <summary>씬에 하나(NetworkManager와 같은 오브젝트, DontDestroyOnLoad). 씬 재로드 때 생기는 중복 NetworkManager의 것은 무시.</summary>
        public static StartMenuController Instance { get; private set; }

        /// <summary>시작 메뉴 패널(타이틀·같이 하기·접속 중·방 대기)이 보이는지 — 일시정지 메뉴는 이때 열리지 않는다.</summary>
        public bool IsMenuVisible => _menuPanel != null && _menuPanel.activeSelf;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            _font = AbsorbRoguelike.UI.DamagePopup.BuiltinFont; // 게임 공용 도트 폰트(Galmuri11)
            ResolveTransports();
            EnsureEventSystem();
            Build();
            Application.logMessageReceived += OnLogMessage;
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
            Loc.Changed -= OnLanguageChanged;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            StartOnlineInit(); // EOSManager.Awake(라이브러리 로드)가 끝난 뒤
            RefreshOnlineUi();
        }

        private void Update()
        {
            PollOnline();
            PollRoomSearch();

            if (_pending != Pending.None)
            {
                PollPending();
            }
            else
            {
                PollSession();
            }

            _wasClientActive = NetworkClient.active;
            _wasHost = NetworkServer.active;

            UpdateHostInfo(NetworkServer.active && _mode == NetMode.Kcp);
            UpdateRoomCorner();
        }

        // 진행 중인 방 작업이 없을 때: 기존 폴링(호스트/접속 시 메뉴 숨김, 끊기면 메뉴로 복귀 + 모드별 안내)
        private void PollSession()
        {
            bool host = NetworkServer.active;
            bool clientActive = NetworkClient.active;
            bool connected = NetworkClient.isConnected;

            // 취소·실패한 방 만들기/참가의 콜백이 늦게 와서 호스트·클라이언트가 켜졌으면 바로 되돌린다.
            if (_discardLateLobby && _mode == NetMode.Eos && (host || clientActive))
            {
                Debug.Log("[StartMenu] late lobby callback after cancel — stopping");
                StopEosSession();
                _wasClientActive = _wasHost = _wasConnected = false;
                return;
            }

            if (host || connected)
            {
                if (host && _mode == NetMode.Eos && _showRoomWait)
                {
                    // 친구가 들어오면(연결 2개 이상) 대기 화면을 자동으로 닫는다.
                    if (NetworkServer.connections.Count >= 2)
                    {
                        _showRoomWait = false;
                        SetMenuVisible(false);
                    }
                    else
                    {
                        ShowRoomWait();
                    }
                }
                else
                {
                    SetMenuVisible(false);
                }

                _wasConnected |= connected;
                _leftVoluntarily = false;
            }
            else if (clientActive)
            {
                ShowConnecting(); // 연결 시도 중(타임아웃 전)
            }
            else
            {
                // 세션이 끝났으면(나갔거나 끊겼거나) 씬을 다시 불러와 층·맵·결과 화면 등 게임 상태를 처음으로 되돌린다 — 안 그러면 다음 방이 이전 층부터 시작한다.
                bool sessionEnded = _leftVoluntarily || _wasClientActive || _wasHost;

                // 방금 전까지 클라이언트였거나 호스트였는데 둘 다 멈췄다 → 메뉴로 복귀 + 안내.
                if (_leftVoluntarily)
                {
                    // '타이틀로'로 스스로 나갔다 — 안내 없이 타이틀로(EOS 로비는 LeaveSessionToTitle에서 이미 나감).
                    _leftVoluntarily = false;
                    _roomCode = null;
                    _showRoomWait = false;
                    ShowMenu(null);
                }
                else if (_wasClientActive && !_wasHost)
                {
                    if (_mode == NetMode.Eos)
                    {
                        LeaveLobbySafe(); // 클라이언트도 로비에서 나간다
                        ShowMenu(_wasConnected
                            ? Loc.Pack("menu.msg_host_lost_online")
                            : Loc.Pack("menu.msg_cannot_join_online"));
                    }
                    else
                    {
                        ShowMenu(_wasConnected ? Loc.Pack("menu.msg_host_lost") : Loc.Pack("menu.msg_cannot_reach_host"));
                    }
                }
                else if (_wasHost)
                {
                    if (_mode == NetMode.Eos)
                    {
                        LeaveLobbySafe();
                    }

                    _roomCode = null;
                    _showRoomWait = false;
                    ShowMenu(null);
                }
                else if (!_menuPanel.activeSelf)
                {
                    ShowMenu(null);
                }

                _wasConnected = false;
                if (sessionEnded)
                {
                    ReloadGameScene();
                }
            }
        }

        /// <summary>현재 게임 씬을 다시 불러온다. NetworkManager(이 메뉴 포함)는 DontDestroyOnLoad라 남고, 씬의 중복 NetworkManager는 Mirror가 지운다.</summary>
        private static void ReloadGameScene()
        {
            Time.timeScale = 1f;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.name);
        }

        // ── 전송 선택 ─────────────────────────────────────────────

#if UNITY_WEBGL
        // 브라우저판: EOS 전송 없음 — NetworkManager의 기본 transport(Kcp)만 찾는다(혼자 하기 listen=false 호스트용, 소켓은 안 연다).
        private void ResolveTransports()
        {
            if (kcpTransport == null)
            {
                NetworkManager manager = GetComponent<NetworkManager>();
                kcpTransport = manager != null ? manager.transport : null;
            }

            if (kcpTransport == null)
            {
                kcpTransport = GetComponentInChildren<Transport>(true); // EOSTransport는 WebGL에서 missing script라 걸리지 않는다
            }
        }
#else
        private void ResolveTransports()
        {
            if (eosTransport == null)
            {
                eosTransport = GetComponentInChildren<EOSTransport>(true);
            }

            if (kcpTransport == null)
            {
                NetworkManager manager = GetComponent<NetworkManager>();
                if (manager != null && manager.transport != null && !(manager.transport is EOSTransport))
                {
                    kcpTransport = manager.transport;
                }
                else
                {
                    foreach (Transport t in GetComponentsInChildren<Transport>(true))
                    {
                        if (!(t is EOSTransport))
                        {
                            kcpTransport = t;
                            break;
                        }
                    }
                }
            }
        }
#endif

        /// <summary>다음 StartHost/StartClient가 쓸 전송을 고른다. 서버·클라이언트가 꺼져 있을 때만 부른다.</summary>
        public static void UseTransport(Transport transport)
        {
            if (transport == null || NetworkManager.singleton == null)
            {
                return;
            }

            NetworkManager.singleton.transport = transport;
            Transport.active = transport;
        }

        private bool UseKcp()
        {
            if (kcpTransport == null)
            {
                SetMessage(Loc.Pack("menu.msg_no_kcp"));
                return false;
            }

            UseTransport(kcpTransport);
            _mode = NetMode.Kcp;
            _discardLateLobby = false;
            return true;
        }

#if UNITY_WEBGL
        // ── 브라우저판 EOS 스텁 ─────────────────────────────────────
        // EOS 어셈블리가 WebGL 빌드에서 빠진다(EOSTransport/Epic.OnlineServices asmdef excludePlatforms). 공용 경로가 그대로 컴파일되게
        // 같은 이름의 빈 스텁만 둔다. 협동 화면 자체에 들어갈 수 없으므로(타이틀에 '멀티플레이' 버튼 없음) 실제로 불리는 것은 Start·Update의 것뿐.
        private bool UseEos()
        {
            return false;
        }

        private static bool LobbyConnected => false;

        private void StartOnlineInit()
        {
            _onlineState = OnlineState.Disabled;
            _onlineReason = null;
        }

        private void PollOnline()
        {
        }
#else
        private bool UseEos()
        {
            if (eosTransport == null)
            {
                SetMessage(Loc.Pack("menu.msg_no_eos"));
                return false;
            }

            UseTransport(eosTransport);
            _mode = NetMode.Eos;
            _discardLateLobby = false;
            return true;
        }

        // EOSTransport.ConnectedToLobby는 instance가 없으면 NRE — 감싸서 읽는다.
        private static bool LobbyConnected => EOSTransport.instance != null && EOSTransport.ConnectedToLobby;

        // ── EOS 초기화 ────────────────────────────────────────────

        private void StartOnlineInit()
        {
            if (eosConfig == null)
            {
                eosConfig = Resources.Load<EOSConfig>(EOSConfigResourceName);
            }

            if (eosConfig == null || !eosConfig.IsFilled)
            {
                _onlineState = OnlineState.Disabled;
                _onlineReason = "menu.reason_no_config"; // 키 — 표시할 때 ReasonText로 번역
                return;
            }

            if (EOSManager.instance == null || eosTransport == null)
            {
                _onlineState = OnlineState.Disabled;
                _onlineReason = "menu.reason_no_eos_components";
                return;
            }

            _onlineState = OnlineState.Connecting;
            _onlineReason = null;
            _onlineStartTime = Time.unscaledTime;
            try
            {
                // EOS 로비 표시 이름(게임 화면에는 안 나옴) — 접속 시점 언어로 한 번 정한다.
                string displayName = Loc.Get("menu.eos_display_name") + Random.Range(0, 10000).ToString("D4");
                EOSManager.Initialize(eosConfig.ToOptions(displayName));
            }
            catch (System.Exception e)
            {
                // 동기 단계 실패(플랫폼 생성 등). 메시지에는 인증 값이 들어가지 않는다(형식·결과 코드만).
                Debug.LogWarning($"[StartMenu] EOS initialize failed: {e.GetType().Name} {ExtractResult(e.Message)}");
                FailOnline(ExtractResult(e.Message) ?? e.GetType().Name);
            }
        }

        private void PollOnline()
        {
            if (_onlineState != OnlineState.Connecting)
            {
                return;
            }

            if (EOSManager.instance != null && EOSManager.Initialized)
            {
                _onlineState = OnlineState.Ready;
                _onlineReason = null;
                RefreshOnlineUi();
                return;
            }

            if (Time.unscaledTime - _onlineStartTime > OnlineInitTimeout)
            {
                FailOnline(_onlineReason ?? "menu.reason_timeout");
            }
        }
#endif

        private void FailOnline(string reason)
        {
            _onlineState = OnlineState.Failed;
            _onlineReason = reason;
            RefreshOnlineUi();
        }

        private void RefreshOnlineUi()
        {
            bool ready = _onlineState == OnlineState.Ready;
            if (_createRoomButton != null)
            {
                _createRoomButton.interactable = ready;
            }

            if (_createPrivateRoomButton != null)
            {
                _createPrivateRoomButton.interactable = ready;
            }

            if (_roomListButton != null)
            {
                _roomListButton.interactable = ready;
            }

            if (_joinRoomButton != null)
            {
                _joinRoomButton.interactable = ready;
            }

            if (_roomCodeInput != null)
            {
                _roomCodeInput.interactable = ready;
            }

            if (_onlineStatusText == null)
            {
                return;
            }

#if UNITY_WEBGL
            _onlineStatusText.text = ""; // 브라우저판: 온라인 상태 줄 없음(혼자 하기 전용)
#else

            switch (_onlineState)
            {
                case OnlineState.Connecting:
                    _onlineStatusText.text = Loc.Get("menu.online_connecting");
                    break;
                case OnlineState.Ready:
                    _onlineStatusText.text = Loc.Get("menu.online_ready");
                    break;
                case OnlineState.Failed:
                    _onlineStatusText.text = Loc.Get("menu.online_failed", ReasonText(_onlineReason));
                    break;
                default:
                    _onlineStatusText.text = Loc.Get("menu.online_disabled", ReasonText(_onlineReason));
                    break;
            }
#endif
        }

        // SDK 콜백 예외는 호출 쪽 try/catch를 지나가므로 로그로 받는다. Result 이름만 보관한다(인증 값 없음).
        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error)
            {
                return;
            }

            if (condition == null || !condition.Contains("EOSSDKException"))
            {
                return;
            }

            string result = ExtractResult(condition) ?? "Unknown";
            if (_pending != Pending.None && _pendingErrorResult == null)
            {
                _pendingErrorResult = result;
            }
            else if (_searching)
            {
                FailRoomSearch(result);
            }
            else if (_onlineState == OnlineState.Connecting)
            {
                _onlineReason = result;
                FailOnline(result);
            }
        }

        private static string ExtractResult(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            Match m = ResultRegex.Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }

        // ── 방 코드 ───────────────────────────────────────────────

#if !UNITY_WEBGL
        /// <summary>
        /// 6자리 방 코드. 문자 집합 <see cref="RoomCodeAlphabet"/>(0/O/1/I 제외). 암호학적 난수라 두 PC가 같은 시각에 만들어도 겹칠 확률이 낮다.
        /// 결과는 EOS 로비 ID 조건(길이 4~60, URL-safe)을 항상 만족한다 — 아래 검사로 확인.
        /// </summary>
        public static string GenerateRoomCode()
        {
            var sb = new StringBuilder(RoomCodeLength);
            for (int i = 0; i < RoomCodeLength; i++)
            {
                sb.Append(RoomCodeAlphabet[RandomNumberGenerator.GetInt32(RoomCodeAlphabet.Length)]);
            }

            string code = sb.ToString();
            if (code.Length < LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH || code.Length > LobbyInterface.MAX_LOBBYIDOVERRIDE_LENGTH || !Helper.IsUrlSafe(code))
            {
                throw new System.InvalidOperationException("room code violates EOS lobby id rules");
            }

            return code;
        }
#endif

        /// <summary>입력 문자열 → 대문자, 허용 문자만, 최대 6자.</summary>
        public static string SanitizeRoomCode(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }

            var sb = new StringBuilder(RoomCodeLength);
            foreach (char c in raw.ToUpperInvariant())
            {
                if (RoomCodeAlphabet.IndexOf(c) >= 0)
                {
                    sb.Append(c);
                    if (sb.Length >= RoomCodeLength)
                    {
                        break;
                    }
                }
            }

            return sb.ToString();
        }

        private void OnRoomCodeChanged(string value)
        {
            string clean = SanitizeRoomCode(value);
            if (clean != value)
            {
                _roomCodeInput.SetTextWithoutNotify(clean);
                _roomCodeInput.caretPosition = clean.Length;
            }
        }

        // ── 버튼 동작 ─────────────────────────────────────────────

        private bool CanStartSomething()
        {
            if (NetworkManager.singleton == null || NetworkServer.active || NetworkClient.active || _pending != Pending.None || _quitting)
            {
                return false;
            }

            if (LobbyConnected)
            {
                // 로비 정리 콜백이 아직 안 왔다 — 그 콜백이 StopHost/StopClient를 부르므로 새 세션을 열면 꺼진다.
                SetMessage(Loc.Pack("menu.msg_cleaning_up"));
                return false;
            }

            return true;
        }

        // 첫 플레이 인트로(2026-10-16): 처음 시작할 때만 컷 그림·자막을 먼저 보여 주고, 끝나면 원래 시작 동작을 그대로 한다.
        private void OnHostClicked()
        {
            if (StoryIntroController.IsPlaying)
            {
                return; // 인트로가 떠 있는 동안 중복 클릭 방지
            }

            if (!StoryIntroController.IsSeen)
            {
                if (CanStartSomething())
                {
                    StoryIntroController.Play(false, StartHostNow); // 인트로 뒤 StartHostNow가 검사를 다시 한다
                }

                return;
            }

            StartHostNow();
        }

        private void StartHostNow()
        {
            if (!CanStartSomething() || !UseKcp())
            {
                return;
            }

            SetMessage(null);
#if UNITY_WEBGL
            // 브라우저판 혼자 하기: 브라우저는 UDP 소켓을 열 수 없다 — listen=false면 StartHost가 Transport.ServerStart를 부르지 않고
            // 로컬 연결(호스트 자신)만 있는 호스트로 시작한다. 전송은 UseKcp로 지정만 해 둔다(Transport.active가 null이 아니게).
            NetworkServer.listen = false;
#endif
            try
            {
                NetworkManager.singleton.StartHost();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StartMenu] StartHost failed: {e.Message}");
                SetMessage(Loc.Pack("menu.msg_host_failed"));
            }
        }

        private void OnJoinClicked()
        {
            if (!CanStartSomething() || !UseKcp())
            {
                return;
            }

            string ip = _ipInput != null ? _ipInput.text.Trim() : "";
            if (string.IsNullOrEmpty(ip))
            {
                ip = DefaultJoinIp;
            }

            try
            {
                PlayerPrefs.SetString(LastJoinIpKey, ip);
                PlayerPrefs.Save();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StartMenu] PlayerPrefs save failed: {e.Message}");
            }

            SetMessage(null);
            NetworkManager.singleton.networkAddress = ip;
            _wasConnected = false;
            NetworkManager.singleton.StartClient();
            SetConnectingText(Loc.Pack("menu.connecting_ip", $"{ip}:{TransportPort()}"));
            ShowConnecting();
        }

        private void OnCreatePublicRoomClicked()
        {
            CreateRoom(true);
        }

        private void OnCreatePrivateRoomClicked()
        {
            CreateRoom(false);
        }

#if UNITY_WEBGL
        private void CreateRoom(bool isPublic)
        {
        }

        private void OnJoinRoomClicked()
        {
        }
#else
        private void CreateRoom(bool isPublic)
        {
            if (_onlineState != OnlineState.Ready || !CanStartSomething() || !UseEos())
            {
                return;
            }

            SetMessage(null);
            string code;
            try
            {
                code = GenerateRoomCode();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StartMenu] room code generation failed: {e.Message}");
                SetMessage(Loc.Pack("menu.msg_code_failed"));
                return;
            }

            BeginPending(Pending.Creating, code);
            _pendingPublic = isPublic;
            SetConnectingText(Loc.Pack("menu.creating_room"));
            ShowConnecting();
            try
            {
                var attributes = new List<AttributeData>
                {
                    new AttributeData { Key = VisibilityKey, Value = isPublic ? VisibilityPublic : VisibilityPrivate },
                    new AttributeData { Key = HostNameKey, Value = EOSManager.DisplayName },
                };
                EOSTransport.CreateLobby(code, RoomMaxPlayers, attributes); // 성공 콜백 안에서 StartHost
            }
            catch (System.Exception e)
            {
                FailCreate(ExtractResult(e.Message) ?? e.GetType().Name);
            }
        }

        private void OnJoinRoomClicked()
        {
            if (_onlineState != OnlineState.Ready || !CanStartSomething())
            {
                return;
            }

            string code = SanitizeRoomCode(_roomCodeInput != null ? _roomCodeInput.text : "");
            if (code.Length != RoomCodeLength)
            {
                SetMessage(Loc.Pack("menu.msg_enter_code", RoomCodeLength));
                return;
            }

            if (!UseEos())
            {
                return;
            }

            SetMessage(null);
            BeginPending(Pending.Joining, code);
            _wasConnected = false;
            SetConnectingText(Loc.Pack("menu.joining_room", code));
            ShowConnecting();
            try
            {
                EOSTransport.JoinLobbyByID(code); // 성공 콜백 안에서 StartClient
            }
            catch (System.Exception e)
            {
                FailJoin(ExtractResult(e.Message) ?? e.GetType().Name);
            }
        }
#endif

        private void OnCancelClicked()
        {
            if (_pending != Pending.None)
            {
                _pending = Pending.None;
                StopEosSession();
                _discardLateLobby = true;
                ResetWasFlags();
                ShowMenu(Loc.Pack("menu.msg_cancelled"));
                return;
            }

            if (_mode == NetMode.Eos)
            {
                StopEosSession();
            }
            else if (NetworkClient.active && !NetworkServer.active)
            {
                NetworkManager.singleton.StopClient();
            }

            ResetWasFlags(); // 사용자가 취소한 것 — 실패 문구를 띄우지 않는다
            ShowMenu(Loc.Pack("menu.msg_join_cancelled"));
        }

        /// <summary>
        /// 일시정지 메뉴 '타이틀로': 지금 세션을 끝내고 타이틀로 돌아간다(끊김 안내 없음). EOS면 LeaveLobby를 먼저(StopEosSession),
        /// 아니면 호스트는 StopHost·클라이언트는 StopClient. 메뉴 복귀는 기존 Update(PollSession) 흐름이 한다.
        /// </summary>
        public void LeaveSessionToTitle()
        {
            if (_pending != Pending.None)
            {
                OnCancelClicked();
                return;
            }

            _leftVoluntarily = true;
            if (_mode == NetMode.Eos)
            {
                StopEosSession();
            }
            else
            {
                try
                {
                    if (NetworkServer.active)
                    {
                        NetworkManager.singleton.StopHost();
                    }
                    else if (NetworkClient.active)
                    {
                        NetworkManager.singleton.StopClient();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[StartMenu] leave session failed: {e.Message}");
                }
            }

            SetMessage(null);
        }

        /// <summary>일시정지 메뉴 '게임 종료' — 타이틀 '게임 종료'와 같은 경로(EOS 로비 정리 후 종료).</summary>
        public void RequestQuit()
        {
            OnQuitClicked();
        }

        private void OnCoopClicked()
        {
            if (StoryIntroController.IsPlaying)
            {
                return;
            }

            if (!StoryIntroController.IsSeen)
            {
                StoryIntroController.Play(true, ShowCoopGroup); // 같이 하기는 '함께 온 친구들' 컷(12)까지
                return;
            }

            ShowCoopGroup();
        }

        private void ShowCoopGroup()
        {
            SetMessage(null);
            _mainGroup.SetActive(false);
            _coopGroup.SetActive(true);
            RefreshOnlineUi();
        }

        private void OnCoopBackClicked()
        {
            _coopGroup.SetActive(false);
            _mainGroup.SetActive(true);
        }

        // ── 공개방 목록 ───────────────────────────────────────────

        private void OnRoomListClicked()
        {
            if (_onlineState != OnlineState.Ready || !CanStartSomething())
            {
                return;
            }

            SetMessage(null);
            _coopGroup.SetActive(false);
            _roomListGroup.SetActive(true);
            StartRoomSearch();
        }

        private void OnRoomListBackClicked()
        {
            HideRoomList();
            _coopGroup.SetActive(true);
            RefreshOnlineUi();
        }

#if UNITY_WEBGL
        private void StartRoomSearch()
        {
        }
#else
        private void StartRoomSearch()
        {
            if (_searching || _onlineState != OnlineState.Ready)
            {
                return;
            }

            ClearRoomList();
            _searching = true;
            _searchStartTime = Time.unscaledTime;
            int token = ++_searchToken;
            _roomListRefreshButton.interactable = false;
            SetRoomListStatus(Loc.Pack("menu.room_list_searching"));
            try
            {
                EOSTransport.SearchForLobbiesByAttribute(
                    new AttributeData { Key = VisibilityKey, Value = VisibilityPublic },
                    RoomSearchMaxResults,
                    found => OnRoomsFound(token, found));
            }
            catch (System.Exception e)
            {
                FailRoomSearch(ExtractResult(e.Message) ?? e.GetType().Name);
            }
        }
#endif

        private void PollRoomSearch()
        {
            if (_searching && Time.unscaledTime - _searchStartTime > RoomSearchTimeout)
            {
                FailRoomSearch("TimedOut");
            }
        }

        private void FailRoomSearch(string result)
        {
            Debug.LogWarning($"[StartMenu] room search failed: {result}");
            _searching = false;
            _searchToken++; // 늦게 온 결과는 버린다
            _roomListRefreshButton.interactable = true;
            SetRoomListStatus(Loc.Pack("menu.room_list_failed"));
        }

#if UNITY_WEBGL
        private void OnJoinListedRoomClicked(int index)
        {
        }
#else
        // EOS 콜백(메인 스레드, EOSManager Tick 안). 자리가 남은 방만 보여 준다.
        private void OnRoomsFound(int token, List<LobbyDetails> found)
        {
            if (token != _searchToken || !_searching)
            {
                foreach (LobbyDetails stale in found)
                {
                    stale.Release();
                }

                return;
            }

            _searching = false;
            _roomListRefreshButton.interactable = true;
            var options = new LobbyDetailsCopyInfoOptions();
            foreach (LobbyDetails details in found)
            {
                if (_foundLobbies.Count < RoomListMaxRows
                    && details.CopyInfo(ref options, out LobbyDetailsInfo? info) == Epic.OnlineServices.Result.Success
                    && info.HasValue && info.Value.AvailableSlots > 0)
                {
                    AddRoomRow(_foundLobbies.Count, ReadHostName(details), info.Value.MaxMembers - info.Value.AvailableSlots, info.Value.MaxMembers);
                    _foundLobbies.Add(details);
                }
                else
                {
                    details.Release();
                }
            }

            SetRoomListStatus(_foundLobbies.Count == 0
                ? Loc.Pack("menu.room_list_empty")
                : Loc.Pack("menu.room_list_count", _foundLobbies.Count));
        }

        private static string ReadHostName(LobbyDetails details)
        {
            var options = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = HostNameKey };
            if (details.CopyAttributeByKey(ref options, out Epic.OnlineServices.Lobby.Attribute? attribute) == Epic.OnlineServices.Result.Success
                && attribute.HasValue && attribute.Value.Data.HasValue)
            {
                string name = attribute.Value.Data.Value.Value.AsUtf8;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return "?";
        }

        private void OnJoinListedRoomClicked(int index)
        {
            if (index < 0 || index >= _foundLobbies.Count || _onlineState != OnlineState.Ready || !CanStartSomething() || !UseEos())
            {
                return;
            }

            LobbyDetails details = _foundLobbies[index];
            _foundLobbies.RemoveAt(index); // 참가에 쓰는 핸들은 전송(CurrentLobbyDetails)이 가져간다 — Release하지 않는다
            var options = new LobbyDetailsCopyInfoOptions();
            string code = details.CopyInfo(ref options, out LobbyDetailsInfo? info) == Epic.OnlineServices.Result.Success && info.HasValue
                ? (string)info.Value.LobbyId
                : "?";
            HideRoomList();

            SetMessage(null);
            BeginPending(Pending.Joining, code);
            _wasConnected = false;
            SetConnectingText(Loc.Pack("menu.joining_room", code));
            ShowConnecting();
            try
            {
                EOSTransport.JoinLobby(details); // 성공 콜백 안에서 StartClient
            }
            catch (System.Exception e)
            {
                FailJoin(ExtractResult(e.Message) ?? e.GetType().Name);
            }
        }
#endif

        private void HideRoomList()
        {
            _searching = false;
            _searchToken++;
            ClearRoomList();
            _roomListGroup.SetActive(false);
        }

        private void ClearRoomList()
        {
#if !UNITY_WEBGL
            foreach (LobbyDetails details in _foundLobbies)
            {
                details.Release();
            }

            _foundLobbies.Clear();
#endif
            for (int i = _roomListRows.childCount - 1; i >= 0; i--)
            {
                Destroy(_roomListRows.GetChild(i).gameObject);
            }
        }

        private void SetRoomListStatus(string payload)
        {
            _roomListStatusPayload = payload;
            _roomListStatusText.text = Loc.Unpack(payload) ?? "";
        }

        // 한 줄: "플레이어1234의 방  1/2" + 참가 버튼
        private void AddRoomRow(int index, string hostName, uint members, uint maxMembers)
        {
            Transform row = AddRow(_roomListRows, "RoomRow", 40f);
            Text label = AddText(row, Loc.Get("menu.room_list_entry", hostName, members, maxMembers), 16, Color.white, 40f);
            label.alignment = TextAnchor.MiddleLeft;
            UIBuilder.FitButtonLabel(label);
            label.GetComponent<LayoutElement>().preferredWidth = 260f;
            Button join = AddButtonKey(row, "menu.room_list_join", () => OnJoinListedRoomClicked(index), 40f);
            join.GetComponent<LayoutElement>().preferredWidth = 110f;
        }

        private void OnOptionsClicked()
        {
            AbsorbRoguelike.UI.OptionsMenuController.Open(null); // 옵션 창(정렬 50)이 타이틀 위에 뜨고, 닫으면 타이틀이 그대로 보인다
        }

        private void OnCopyRoomCodeClicked()
        {
            if (string.IsNullOrEmpty(_roomCode))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = _roomCode;
            SetMessage(Loc.Pack("menu.msg_code_copied"));
        }

        private void OnRoomWaitContinueClicked()
        {
            _showRoomWait = false;
            SetMenuVisible(false);
        }

        private void OnCloseRoomClicked()
        {
            StopEosSession();
            _roomCode = null;
            _showRoomWait = false;
            ResetWasFlags();
            ShowMenu(Loc.Pack("menu.msg_room_closed"));
        }

        private void OnQuitClicked()
        {
            if (_quitting)
            {
                return;
            }

            _quitting = true;
            StartCoroutine(QuitRoutine());
        }

        private IEnumerator QuitRoutine()
        {
            if (LobbyConnected)
            {
                StopEosSession();
                // 로비 파괴 요청이 EOS로 나갈 시간(EOSManager.FixedUpdate가 계속 Tick). 콜백이 오면 바로 끝.
                float until = Time.unscaledTime + QuitLobbyWait;
                while (LobbyConnected && Time.unscaledTime < until)
                {
                    yield return null;
                }
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── 방 만들기/참가 진행 판정 ──────────────────────────────

        private void BeginPending(Pending kind, string code)
        {
            _pending = kind;
            _pendingCode = code;
            _pendingStartTime = Time.unscaledTime;
            _pendingErrorResult = null;
            _pendingSawClient = false;
        }

        private void PollPending()
        {
            float elapsed = Time.unscaledTime - _pendingStartTime;
            if (_pending == Pending.Creating)
            {
                if (_pendingErrorResult != null)
                {
                    FailCreate(_pendingErrorResult);
                }
                else if (LobbyConnected && NetworkServer.active)
                {
                    _pending = Pending.None;
                    _roomCode = _pendingCode;
                    _roomIsPublic = _pendingPublic;
                    _showRoomWait = true;
                    _wasConnected = true;
                    ShowRoomWait();
                }
                else if (elapsed > CreateRoomTimeout)
                {
                    FailCreate("TimedOut");
                }
                else
                {
                    ShowConnecting();
                }

                return;
            }

            // Joining
            _pendingSawClient |= NetworkClient.active;
            if (_pendingErrorResult != null)
            {
                FailJoin(_pendingErrorResult);
            }
            else if (NetworkClient.isConnected)
            {
                _pending = Pending.None;
                _wasConnected = true;
                SetMenuVisible(false);
            }
            else if (_pendingSawClient && !NetworkClient.active)
            {
                FailJoin("NoConnection"); // StartClient는 됐는데 호스트 P2P 연결이 끊김/타임아웃
            }
            else if (elapsed > JoinRoomTimeout)
            {
                FailJoin("TimedOut");
            }
            else
            {
                ShowConnecting();
            }
        }

        private void FailCreate(string result)
        {
            Debug.LogWarning($"[StartMenu] create room failed: {result}");
            _pending = Pending.None;
            StopEosSession(); // 반쯤 시작된 호스트·로비 정리(LeaveLobby → StopHost 순서)
            _discardLateLobby = true;
            ResetWasFlags();
            string reason = result == "LobbyLobbyAlreadyExists" ? Loc.KeyArg("menu.reason_code_exists")
                : result == "TimedOut" ? Loc.KeyArg("menu.reason_timeout_check")
                : result;
            ShowMenu(Loc.Pack("menu.msg_create_failed", reason));
        }

        private void FailJoin(string result)
        {
            Debug.LogWarning($"[StartMenu] join room failed: {result}");
            _pending = Pending.None;
            StopEosSession();
            _discardLateLobby = true;
            ResetWasFlags();
            string message;
            switch (result)
            {
                case "NotFound":
                case "InvalidParameters":
                    message = Loc.Pack("menu.msg_join_not_found");
                    break;
                case "LobbyTooManyPlayers":
                case "LobbyHostAtCapacity":
                    message = Loc.Pack("menu.msg_join_full");
                    break;
                case "TimedOut":
                    message = Loc.Pack("menu.msg_join_timeout");
                    break;
                case "NoConnection":
                    message = Loc.Pack("menu.msg_join_no_connection");
                    break;
                default:
                    message = Loc.Pack("menu.msg_join_failed", result);
                    break;
            }

            ShowMenu(message);
        }

        /// <summary>EOS 세션 정리: LeaveLobby를 먼저(호스트면 로비 파괴 — NetworkServer.active 동안만 가능), 그다음 StopHost/StopClient.</summary>
        private void StopEosSession()
        {
            LeaveLobbySafe();
            try
            {
                if (NetworkServer.active)
                {
                    NetworkManager.singleton.StopHost();
                }
                else if (NetworkClient.active)
                {
                    NetworkManager.singleton.StopClient();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StartMenu] stop after lobby leave failed: {e.Message}");
            }
        }

        /// <summary>로비에 들어가 있으면 나간다(호스트는 파괴). 예외는 삼킨다. NetworkShutdownGuard도 쓴다.</summary>
        public static void LeaveLobbySafe()
        {
#if !UNITY_WEBGL
            try
            {
                if (LobbyConnected)
                {
                    EOSTransport.LeaveLobby();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StartMenu] LeaveLobby failed: {e.GetType().Name} {ExtractResult(e.Message)}");
            }
#endif
        }

        private void ResetWasFlags()
        {
            _wasClientActive = false;
            _wasHost = false;
            _wasConnected = false;
        }

        // ── 화면 상태 ─────────────────────────────────────────────

        private void ShowMenu(string message)
        {
            SetMenuVisible(true);
            HideRoomList();
            _mainGroup.SetActive(true); // 항상 타이틀부터
            _coopGroup.SetActive(false);
            _connectingGroup.SetActive(false);
            _roomWaitGroup.SetActive(false);
            _onlineStatusText.gameObject.SetActive(true);
            RefreshOnlineUi();
            if (message != null)
            {
                SetMessage(message);
            }
        }

        private void ShowConnecting()
        {
            SetMenuVisible(true);
            _mainGroup.SetActive(false);
            _coopGroup.SetActive(false);
            _roomListGroup.SetActive(false);
            _connectingGroup.SetActive(true);
            _roomWaitGroup.SetActive(false);
            _onlineStatusText.gameObject.SetActive(false);
        }

        private void ShowRoomWait()
        {
            SetMenuVisible(true);
            _mainGroup.SetActive(false);
            _coopGroup.SetActive(false);
            _roomListGroup.SetActive(false);
            _connectingGroup.SetActive(false);
            _roomWaitGroup.SetActive(true);
            _onlineStatusText.gameObject.SetActive(false);
            _roomCodeBigText.text = Loc.Get("menu.room_code_big", _roomCode);
            _roomOpenedText.text = Loc.Get(_roomIsPublic ? "menu.room_opened_public" : "menu.room_opened_private");
        }

        private void SetMenuVisible(bool visible)
        {
            if (_menuPanel.activeSelf != visible)
            {
                _menuPanel.SetActive(visible);
            }
        }

        // 메시지·접속 중 글자는 Loc.Pack 문자열로 받아 두고 표시할 때 번역한다 — 언어를 바꾸면 OnLanguageChanged가 다시 쓴다.
        private void SetMessage(string message)
        {
            _messagePayload = message;
            _messageText.text = Loc.Unpack(message) ?? "";
            _messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private void SetConnectingText(string payload)
        {
            _connectingPayload = payload;
            _connectingText.text = Loc.Unpack(payload) ?? "";
        }

        // _onlineReason은 표 키(우리 쪽 사유) 또는 EOS 결과 코드(그대로 표시).
        private static string ReasonText(string reason)
        {
            return Loc.TryGet(reason, out string text) ? text : reason;
        }

        private void OnLanguageChanged()
        {
            if (_messageText != null && !string.IsNullOrEmpty(_messagePayload))
            {
                _messageText.text = Loc.Unpack(_messagePayload);
            }

            if (_connectingText != null && !string.IsNullOrEmpty(_connectingPayload))
            {
                _connectingText.text = Loc.Unpack(_connectingPayload);
            }

            if (_roomCodeBigText != null && !string.IsNullOrEmpty(_roomCode))
            {
                _roomCodeBigText.text = Loc.Get("menu.room_code_big", _roomCode);
                _roomOpenedText.text = Loc.Get(_roomIsPublic ? "menu.room_opened_public" : "menu.room_opened_private");
            }

            if (_roomListStatusText != null && !string.IsNullOrEmpty(_roomListStatusPayload))
            {
                _roomListStatusText.text = Loc.Unpack(_roomListStatusPayload);
            }

            _hostInfoRefreshTimer = 0f; // 호스트 IP 줄도 다음 프레임에 새 언어로
            RefreshOnlineUi();
        }

        // EOS 호스트 게임 중: 오른쪽 아래에 방 코드 작게 + 복사 버튼.
        private void UpdateRoomCorner()
        {
            bool show = NetworkServer.active && _mode == NetMode.Eos && !string.IsNullOrEmpty(_roomCode) && !_menuPanel.activeSelf;
            if (_roomCorner.activeSelf != show)
            {
                _roomCorner.SetActive(show);
            }

            if (show)
            {
                _roomCornerText.text = Loc.Get("menu.room_code_corner", _roomCode);
            }
        }

        // Kcp 호스트일 때만 오른쪽 아래에 LAN 주소 표시. 주소 목록은 1초마다만 다시 읽는다(네트워크 인터페이스 조회 비용).
        private void UpdateHostInfo(bool host)
        {
#if UNITY_WEBGL
            host = false; // 브라우저판: listen=false 호스트라 알려 줄 주소가 없다(네트워크 인터페이스 조회도 안 함)
#endif
            _hostInfoText.gameObject.SetActive(host);
            if (!host)
            {
                _hostInfoRefreshTimer = 0f;
                return;
            }

            _hostInfoRefreshTimer -= Time.unscaledDeltaTime;
            if (_hostInfoRefreshTimer > 0f)
            {
                return;
            }

            _hostInfoRefreshTimer = 1f;
            List<string> addresses = GetLanIPv4Addresses();
            string port = TransportPort();
            _hostInfoText.text = addresses.Count > 0
                ? Loc.Get("menu.host_ip", string.Join("  /  ", addresses.ConvertAll(a => $"{a}:{port}")))
                : Loc.Get("menu.host_ip_none", port);
        }

        private static string TransportPort()
        {
            return Transport.active is PortTransport portTransport ? portTransport.Port.ToString() : "?";
        }

        /// <summary>켜져 있고(Up) 루프백이 아닌 인터페이스의 IPv4 주소 전부. 이더넷을 먼저 둔다(시설 LAN 기준).</summary>
        public static List<string> GetLanIPv4Addresses()
        {
#if UNITY_WEBGL
            return new List<string>(); // 브라우저는 네트워크 인터페이스를 읽을 수 없다
#else
            var ethernet = new List<string>();
            var others = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    bool isEthernet = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                                      || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                                      || nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT
                                      || nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx;
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address))
                        {
                            continue;
                        }

                        string text = info.Address.ToString();
                        List<string> target = isEthernet ? ethernet : others;
                        if (!ethernet.Contains(text) && !others.Contains(text))
                        {
                            target.Add(text);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StartMenu] network interface query failed: {e.Message}");
            }

            ethernet.AddRange(others);
            return ethernet;
#endif
        }

        // ── UI 만들기 ─────────────────────────────────────────────

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem (StartMenu)");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private void Build()
        {
            var canvasObject = new GameObject("StartMenuCanvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortOrder;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            // 메뉴 패널(화면 전체 어둡게 + 가운데 세로 목록)
            _menuPanel = new GameObject("MenuPanel", typeof(RectTransform), typeof(Image));
            _menuPanel.transform.SetParent(canvasObject.transform, false);
            Stretch((RectTransform)_menuPanel.transform);
            _menuPanel.GetComponent<Image>().color = PanelColor;
            if (titleBackground != null)
            {
                // 화면 비율이 달라도 빈틈 없이 덮도록(넘치는 쪽은 잘림) 그림 비율을 유지한 채 패널을 감싼다.
                var bg = new GameObject("TitleBackground", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                bg.transform.SetParent(_menuPanel.transform, false);
                var bgImage = bg.GetComponent<Image>();
                bgImage.sprite = titleBackground;
                bgImage.raycastTarget = false;
                var fitter = bg.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = titleBackground.rect.width / titleBackground.rect.height;

                var dim = new GameObject("TitleDim", typeof(RectTransform), typeof(Image));
                dim.transform.SetParent(_menuPanel.transform, false);
                Stretch((RectTransform)dim.transform);
                var dimImage = dim.GetComponent<Image>();
                dimImage.color = new Color(0f, 0f, 0f, titleBackgroundDim);
                dimImage.raycastTarget = false;
            }

            // 2026-10-06 도트 UI 키트: 메뉴 목록 뒤에 패널 그림(키트가 없으면 투명 = 예전 그대로). 높이는 내용에 맞춘다.
            var column = new GameObject("Column", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            column.transform.SetParent(_menuPanel.transform, false);
            var columnRect = (RectTransform)column.transform;
            columnRect.anchorMin = columnRect.anchorMax = new Vector2(0.5f, 0.5f);
            columnRect.sizeDelta = new Vector2(440f, 600f);
            var columnImage = column.GetComponent<Image>();
            columnImage.color = Color.clear;
            columnImage.raycastTarget = false;
            AbsorbRoguelike.UI.UIBuilder.SkinPanel(columnImage);
            column.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.padding = AbsorbRoguelike.UI.UIBuilder.PanelPadding;
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            AddText(column.transform, "Absorb", 44, Color.white, 64f);
            _messageText = AddText(column.transform, "", 16, MessageColor, 40f);
            SetMessage(null);
            // EOS 초기화 상태는 타이틀·같이 하기 양쪽에서 보이도록 그룹 밖(column)에 둔다
            _onlineStatusText = AddText(column.transform, Loc.Get("menu.online_connecting"), 13, SubTextColor, 18f);

            // 타이틀(2026-09-30): 혼자 하기 = 로컬 호스트 1인 세션(기존 '호스트 시작'과 같은 호출), 같이 하기 = 하위 그룹
            _mainGroup = AddGroup(column.transform, "Main");
            AddButtonKey(_mainGroup.transform, "menu.solo", OnHostClicked, 48f);
#if UNITY_WEBGL
            // 브라우저판: 멀티플레이 대신 눌리지 않는 안내 버튼(협동 그룹으로 못 들어감), 브라우저 탭은 닫을 수 없으니 '게임 종료' 없음.
            AddButtonKey(_mainGroup.transform, "menu.coop_download_only", () => { }, 48f).interactable = false;
            AddButtonKey(_mainGroup.transform, "menu.options", OnOptionsClicked, 48f);
#else
            AddButtonKey(_mainGroup.transform, "menu.coop", OnCoopClicked, 48f);
            AddButtonKey(_mainGroup.transform, "menu.options", OnOptionsClicked, 48f);
            AddButtonKey(_mainGroup.transform, "menu.quit", OnQuitClicked, 48f);
#endif

            // 같이 하기: 위 = 인터넷(방 코드), 아래 = 같은 네트워크(기존 Kcp 동작 그대로) + 뒤로
            // 브라우저판: 이 그룹은 만들기만 하고 들어갈 길이 없다('멀티플레이' 버튼 없음) — 아래 '호스트 시작'·'IP로 참가'도 WebGL에서는 도달 불가.
            _coopGroup = AddGroup(column.transform, "Coop");
            AddTextKey(_coopGroup.transform, "menu.section_online", 16, SectionColor, 22f);
            Transform createRow = AddRow(_coopGroup.transform, "CreateRoomRow", 42f);
            _createRoomButton = AddButtonKey(createRow, "menu.create_room_public", OnCreatePublicRoomClicked, 42f);
            _createPrivateRoomButton = AddButtonKey(createRow, "menu.create_room_private", OnCreatePrivateRoomClicked, 42f);
            _roomListButton = AddButtonKey(_coopGroup.transform, "menu.room_list", OnRoomListClicked, 42f);
            Transform codeRow = AddRow(_coopGroup.transform, "RoomCodeRow", 40f);
            _roomCodeInput = AddInput(codeRow, "", "menu.room_code_placeholder", 160f);
            _roomCodeInput.characterLimit = 16; // 붙여넣기 여유 — 실제 6자 제한·문자 걸러내기는 OnRoomCodeChanged
            _roomCodeInput.onValueChanged.AddListener(OnRoomCodeChanged);
            _joinRoomButton = AddButtonKey(codeRow, "menu.join_room", OnJoinRoomClicked, 40f);

            AddTextKey(_coopGroup.transform, "menu.section_lan", 16, SectionColor, 26f);
            AddButtonKey(_coopGroup.transform, "menu.start_host", OnHostClicked, 42f);
            Transform ipRow = AddRow(_coopGroup.transform, "IpRow", 40f);
            _ipInput = AddInput(ipRow, LoadLastJoinIp(), "menu.ip_placeholder", 220f);
            _ipInput.characterLimit = 64;
            AddButtonKey(ipRow, "menu.join_ip", OnJoinClicked, 40f);
            AddButtonKey(_coopGroup.transform, "menu.back", OnCoopBackClicked, 42f);
            _coopGroup.SetActive(false);

            // 공개방 목록: 상태 줄 + 방 줄(최대 RoomListMaxRows) + 새로고침/뒤로
            _roomListGroup = AddGroup(column.transform, "RoomList");
            AddTextKey(_roomListGroup.transform, "menu.room_list_title", 18, SectionColor, 26f);
            _roomListStatusText = AddText(_roomListGroup.transform, "", 14, SubTextColor, 20f);
            _roomListRows = AddGroup(_roomListGroup.transform, "Rows").transform;
            Transform listButtons = AddRow(_roomListGroup.transform, "RoomListButtons", 42f);
            _roomListRefreshButton = AddButtonKey(listButtons, "menu.room_list_refresh", StartRoomSearch, 42f);
            AddButtonKey(listButtons, "menu.back", OnRoomListBackClicked, 42f);
            _roomListGroup.SetActive(false);

            _connectingGroup = AddGroup(column.transform, "Connecting");
            _connectingText = AddText(_connectingGroup.transform, Loc.Get("menu.connecting"), 18, Color.white, 40f);
            AddButtonKey(_connectingGroup.transform, "menu.cancel", OnCancelClicked, 44f);
            _connectingGroup.SetActive(false);

            // EOS 방을 만든 직후 대기 화면: 코드 크게 + 복사 / 게임으로 / 방 닫기
            _roomWaitGroup = AddGroup(column.transform, "RoomWait");
            _roomOpenedText = AddText(_roomWaitGroup.transform, Loc.Get("menu.room_opened_private"), 18, Color.white, 28f);
            UIBuilder.FitButtonLabel(_roomOpenedText);
            _roomCodeBigText = AddText(_roomWaitGroup.transform, Loc.Get("menu.room_code_big", "------"), 38, Color.white, 60f);
            UIBuilder.FitButtonLabel(_roomCodeBigText); // 영어 'Room Code: XXXXXX'가 두 줄로 접혀 코드가 잘리던 문제(2026-10-16) — 한 줄에서 줄인다
            Text shareHint = AddTextKey(_roomWaitGroup.transform, "menu.room_share_hint", 13, SubTextColor, 20f);
            UIBuilder.FitButtonLabel(shareHint); // 영어가 길면 한 줄 안에서 글자를 줄인다
            Transform waitRow = AddRow(_roomWaitGroup.transform, "RoomWaitRow", 42f);
            AddButtonKey(waitRow, "menu.copy", OnCopyRoomCodeClicked, 42f);
            AddButtonKey(waitRow, "menu.to_game", OnRoomWaitContinueClicked, 42f);
            AddButtonKey(_roomWaitGroup.transform, "menu.close_room", OnCloseRoomClicked, 42f);
            _roomWaitGroup.SetActive(false);

            // Kcp 호스트 IP 표시(오른쪽 아래 작게 — 왼쪽 위 HUD·파티 UI, 오른쪽 디버그 패널과 겹치지 않게)
            var info = new GameObject("HostInfo", typeof(RectTransform));
            info.transform.SetParent(canvasObject.transform, false);
            var infoRect = (RectTransform)info.transform;
            infoRect.anchorMin = infoRect.anchorMax = infoRect.pivot = new Vector2(1f, 0f);
            infoRect.anchoredPosition = new Vector2(-8f, 6f);
            infoRect.sizeDelta = new Vector2(620f, 22f);
            _hostInfoText = info.AddComponent<Text>();
            _hostInfoText.font = _font;
            _hostInfoText.fontSize = 13;
            _hostInfoText.alignment = TextAnchor.LowerRight;
            _hostInfoText.color = new Color(1f, 1f, 1f, 0.7f);
            _hostInfoText.raycastTarget = false;
            info.SetActive(false);

            // EOS 호스트 게임 중 방 코드(같은 자리, 작게) + 복사 버튼
            _roomCorner = new GameObject("RoomCodeCorner", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _roomCorner.transform.SetParent(canvasObject.transform, false);
            var cornerRect = (RectTransform)_roomCorner.transform;
            cornerRect.anchorMin = cornerRect.anchorMax = cornerRect.pivot = new Vector2(1f, 0f);
            cornerRect.anchoredPosition = new Vector2(-8f, 6f);
            cornerRect.sizeDelta = new Vector2(240f, 36f);
            var cornerLayout = _roomCorner.GetComponent<HorizontalLayoutGroup>();
            cornerLayout.spacing = 6f;
            cornerLayout.childAlignment = TextAnchor.LowerRight;
            cornerLayout.childControlWidth = true;
            cornerLayout.childControlHeight = true;
            cornerLayout.childForceExpandWidth = false;
            cornerLayout.childForceExpandHeight = true;
            _roomCornerText = AddText(_roomCorner.transform, "", 14, new Color(1f, 1f, 1f, 0.8f), 36f);
            _roomCornerText.alignment = TextAnchor.MiddleRight;
            _roomCornerText.GetComponent<LayoutElement>().preferredWidth = 150f;
            Button copy = AddButtonKey(_roomCorner.transform, "menu.copy", OnCopyRoomCodeClicked, 36f);
            copy.GetComponent<LayoutElement>().preferredWidth = 64f;
            Text copyText = copy.GetComponentInChildren<Text>();
            copyText.fontSize = 13;
            UIBuilder.FitButtonLabel(copyText);
            _roomCorner.SetActive(false);
        }

        private static string LoadLastJoinIp()
        {
            try
            {
                return PlayerPrefs.GetString(LastJoinIpKey, DefaultJoinIp);
            }
            catch (System.Exception)
            {
                return DefaultJoinIp;
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Inset(RectTransform rect, float x)
        {
            Stretch(rect);
            rect.offsetMin = new Vector2(x, 0f);
            rect.offsetMax = new Vector2(-x, 0f);
        }

        private static GameObject AddGroup(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go;
        }

        // 가로 한 줄(입력칸 + 버튼 등). 자식은 preferredWidth 비율대로 나눠 가진다.
        private static Transform AddRow(Transform parent, string name, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return go.transform;
        }

        private Text AddText(Transform parent, string content, int size, Color color, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float height)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            go.GetComponent<Image>().color = ButtonColor;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            AbsorbRoguelike.UI.UIBuilder.SkinButton(button); // 도트 버튼(키트 없으면 무시)

            Text text = AddText(go.transform, label, 18, Color.white, height);
            Stretch((RectTransform)text.transform);
            // 도트 버튼 테두리 안쪽으로(좌우 12·위아래 7). 높이를 줄여 두 줄로 접히기보다 한 줄에서 글자가 줄어들게 한다(2026-10-16 영어 'Create Private Room' 두 줄·테두리 겹침).
            var textRect = (RectTransform)text.transform;
            textRect.offsetMin = new Vector2(12f, 7f);
            textRect.offsetMax = new Vector2(-12f, -7f);
            UIBuilder.FitButtonLabel(text); // 영어로 길어지면 버튼 안에서 글자 축소
            return button;
        }

        // 언어 설정(2026-10-06): 고정 글자는 키로 만들고 LocalizedText를 붙인다(언어 변경 때 자동 갱신).
        private Text AddTextKey(Transform parent, string key, int size, Color color, float height)
        {
            return UIBuilder.Localize(AddText(parent, Loc.Get(key), size, color, height), key);
        }

        private Button AddButtonKey(Transform parent, string key, UnityEngine.Events.UnityAction onClick, float height)
        {
            Button button = AddButton(parent, Loc.Get(key), onClick, height);
            button.gameObject.name = key;
            return UIBuilder.Localize(button, key);
        }

        private InputField AddInput(Transform parent, string initial, string placeholderKey, float preferredWidth)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var element = go.GetComponent<LayoutElement>();
            element.preferredHeight = 40f;
            element.preferredWidth = preferredWidth;
            go.GetComponent<Image>().color = InputColor;
            AbsorbRoguelike.UI.UIBuilder.SkinBox(go.GetComponent<Image>()); // 파인 칸(키트 없으면 무시)

            // 글자는 칸 테두리(4px × 2단위) 안쪽에
            Text text = AddText(go.transform, "", 18, Color.white, 40f);
            text.supportRichText = false;
            text.raycastTarget = false;
            Inset((RectTransform)text.transform, 10f);

            Text hint = AddTextKey(go.transform, placeholderKey, 15, new Color(1f, 1f, 1f, 0.35f), 40f);
            Inset((RectTransform)hint.transform, 10f);

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.contentType = InputField.ContentType.Standard;
            input.text = initial;
            return input;
        }
    }
}
