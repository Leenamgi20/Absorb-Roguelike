using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 쓰러진 팀원 부활(2026-10-07 사용자 요청, 멀티 전용). 수치·흐름: docs/04_구현위키/쓰러짐_부활_관전.md.
    /// - 로컬 플레이어가 부활 키(기본 F, 옵션에서 변경)를 누르고 있으면 <see cref="CmdSetReviveHeld"/>로 서버에 알린다.
    /// - 서버가 매 프레임 조건(살리는 사람 생존·대상 쓰러짐 유지·거리 reviveRange 이내·살리는 사람 체력 minReviverHealth 이상)을 보고
    ///   시간을 쌓는다. 살리는 사람이 맞아 체력이 줄면 진행이 0으로 돌아간다. reviveDuration초가 차면
    ///   cost = 살리는 사람 체력/2(내림) → 살리는 사람 체력 -cost, 대상 체력 = cost로 부활(같이 리스크를 진다).
    /// - 진행도는 대상 쪽 SyncVar(reviveProgress 0~1)로 모든 화면에 몸 위 게이지로 그린다. 살리는 사람의 진행 여부(isReviving)도
    ///   동기화해 PlayerController가 이동·공격·대시·가드·흡수를 막는다(로컬은 지연 없이 바로 막도록 예측 값을 더한다).
    /// 부활 키는 PlayerController의 Gameplay 맵과 따로 이 컴포넌트의 입력 인스턴스에서 Revive 액션만 켜서 읽는다
    /// (부활 중 Gameplay 맵을 꺼도 키를 뗀 것을 놓치지 않게).
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerRevive : NetworkBehaviour
    {
        [SerializeField] private float reviveRange = 1.5f;     // 칸
        [SerializeField] private float reviveDuration = 3f;    // 초 (2026-10-07 5→3)
        [SerializeField] private int minReviverHealth = 2;     // 체력/2 내림이 1 이상이고 살리는 사람도 1 이상 남게

        [Header("몸 위 진행 게이지")]
        [SerializeField] private float barWidth = 1f;
        [SerializeField] private Color barFillColor = new Color(0.35f, 0.9f, 0.45f);
        [SerializeField] private Color barBackColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        [SerializeField] private int barSortingOrder = 15;

        private const float Pixel = 1f / 16f;

        /// <summary>살리는 사람 쪽: 서버가 부활을 진행 중인지.</summary>
        [SyncVar] private bool isReviving;
        /// <summary>쓰러진 사람 쪽: 부활 진행도 0~1(0이면 게이지 숨김).</summary>
        [SyncVar] private float reviveProgress;

        private PlayerHealth _health;
        private PlayerControls _controls;

        // 로컬 전용
        private bool _sentHeld;
        private bool _localLock;

        // 서버 전용
        private bool _serverHeld;
        private bool _serverWarnedThisHold;
        private PlayerRevive _serverTarget;
        private PlayerRevive _serverRevivedBy; // 대상 쪽: 지금 나를 살리는 사람(한 명만)
        private float _serverElapsed;
        private int _serverLastHealth;

        // 게이지(모든 화면)
        private GameObject _barRoot;
        private Transform _barFill;

        public float ReviveRange => reviveRange;
        public float ReviveProgress => reviveProgress;
        public bool IsDowned => _health != null && _health.IsDowned;

        /// <summary>부활 진행 중(살리는 사람). 로컬 플레이어는 서버 응답 전에도 조건이 맞으면 바로 true(예측).</summary>
        public bool IsReviving => isReviving || (isLocalPlayer && _localLock);

        /// <summary>현재 바인딩된 부활 키 표시 이름(옵션에서 바꾼 키 반영). 로컬 입력 인스턴스가 없으면 공유 인스턴스로.</summary>
        public static string ReviveKeyDisplay()
        {
            InputAction action = InputBindingStore.Shared.Gameplay.Revive;
            string display = action.bindings.Count > 0 ? action.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions) : "";
            return string.IsNullOrEmpty(display) ? "F" : display;
        }

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _controls = new PlayerControls();
            InputBindingStore.Register(_controls);
        }

        private void Start()
        {
            CreateBar();
        }

        public override void OnStartLocalPlayer()
        {
            _controls.Gameplay.Revive.Enable();
        }

        public override void OnStopLocalPlayer()
        {
            _controls.Gameplay.Revive.Disable();
        }

        public override void OnStopServer()
        {
            ServerStopReviving(); // 살리던 중 접속이 끊기면 대상 게이지를 0으로
        }

        private void OnDestroy()
        {
            InputBindingStore.Unregister(_controls);
            _controls?.Dispose();
        }

        private void Update()
        {
            if (isLocalPlayer)
            {
                LocalUpdate();
            }

            if (isServer)
            {
                ServerUpdate();
            }
        }

        // ── 로컬 입력 ──────────────────────────────────────────

        private void LocalUpdate()
        {
            bool menuOpen = PauseMenuController.IsOpen || OptionsMenuController.IsOpen || RunEndScreenController.IsShown;
            bool held = _controls.Gameplay.Revive.IsPressed() && !menuOpen && !_health.IsDead;
            if (held != _sentHeld)
            {
                _sentHeld = held;
                CmdSetReviveHeld(held);
            }

            // 예측 잠금: 누르고 있고, 근처에 쓰러진 팀원이 있고, 체력이 충분하면 서버 응답 전에도 조작을 멈춘다.
            _localLock = held && _health.CurrentHealth >= minReviverHealth && FindDownedTargetInRange(false) != null;
        }

        /// <summary>근처(reviveRange)에서 가장 가까운 쓰러진 팀원. serverExclusive면 다른 사람이 이미 살리는 중인 대상은 뺀다(서버 전용 정보).</summary>
        public PlayerRevive FindDownedTargetInRange(bool serverExclusive)
        {
            PlayerRevive best = null;
            float bestDistance = reviveRange;
            foreach (PlayerRevive other in FindObjectsByType<PlayerRevive>(FindObjectsSortMode.None))
            {
                if (other == this || !other.IsDowned)
                {
                    continue;
                }

                if (serverExclusive && other._serverRevivedBy != null && other._serverRevivedBy != this)
                {
                    continue;
                }

                float d = Vector2.Distance(transform.position, other.transform.position);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = other;
                }
            }

            return best;
        }

        [Command]
        private void CmdSetReviveHeld(bool held)
        {
            _serverHeld = held;
            _serverWarnedThisHold = false;
            if (!held)
            {
                ServerStopReviving();
            }
        }

        // ── 서버 진행 ──────────────────────────────────────────

        [Server]
        private void ServerUpdate()
        {
            if (!_serverHeld || _health.IsDead)
            {
                if (_serverTarget != null || isReviving)
                {
                    ServerStopReviving();
                }

                return;
            }

            // 대상 유지 조건: 아직 쓰러져 있고 거리 안.
            if (_serverTarget != null
                && (!_serverTarget.IsDowned || Vector2.Distance(transform.position, _serverTarget.transform.position) > reviveRange))
            {
                ServerStopReviving();
            }

            if (_serverTarget == null)
            {
                if (isReviving)
                {
                    ServerStopReviving(); // 대상이 접속을 끊어 파괴됨 — 살리는 사람 잠금 풀기
                }

                PlayerRevive candidate = FindDownedTargetInRange(true);
                if (candidate == null)
                {
                    return; // 누르고만 있고 근처에 쓰러진 팀원이 없다 — 걸어서 다가가면 그때 시작
                }

                if (_health.CurrentHealth < minReviverHealth)
                {
                    if (!_serverWarnedThisHold)
                    {
                        _serverWarnedThisHold = true;
                        TargetNeedHealth(connectionToClient);
                    }

                    return;
                }

                _serverTarget = candidate;
                candidate._serverRevivedBy = this;
                _serverElapsed = 0f;
                _serverLastHealth = _health.CurrentHealth;
                isReviving = true;
                Debug.Log($"[PlayerRevive] {name} starts reviving {candidate.name}");
            }

            // 살리는 사람이 맞으면(체력 감소) 진행 0으로.
            int hp = _health.CurrentHealth;
            if (hp < _serverLastHealth)
            {
                _serverElapsed = 0f;
                Debug.Log($"[PlayerRevive] {name} was hit — revive progress reset");
            }

            _serverLastHealth = hp;
            if (hp < minReviverHealth)
            {
                ServerStopReviving();
                return;
            }

            _serverElapsed += Time.deltaTime;
            _serverTarget.reviveProgress = Mathf.Clamp01(_serverElapsed / reviveDuration);
            if (_serverElapsed < reviveDuration)
            {
                return;
            }

            // 완료: 살리는 사람 체력을 반 나눠 준다(내림). 체력 2 이상이 보장돼 둘 다 1 이상.
            PlayerRevive target = _serverTarget;
            int cost = hp / 2;
            ServerStopReviving();
            _serverHeld = false; // 한 번 살리면 키를 다시 눌러야 다음 부활
            _health.ServerPayReviveCost(cost);
            target._health.ServerRevive(cost);
            Debug.Log($"[PlayerRevive] {name} revived {target.name} (shared {cost} HP)");
        }

        [Server]
        private void ServerStopReviving()
        {
            if (_serverTarget != null)
            {
                _serverTarget.reviveProgress = 0f;
                if (_serverTarget._serverRevivedBy == this)
                {
                    _serverTarget._serverRevivedBy = null;
                }
            }

            _serverTarget = null;
            _serverElapsed = 0f;
            isReviving = false;
        }

        [TargetRpc]
        private void TargetNeedHealth(NetworkConnectionToClient target)
        {
            DownedReviveHud.ShowMessage("revive.need_health");
        }

        // ── 몸 위 진행 게이지(모든 화면) ─────────────────────────

        private void CreateBar()
        {
            var body = GetComponent<SpriteRenderer>();
            float top = body != null && body.sprite != null ? body.sprite.bounds.max.y : 0.5f;
            _barRoot = new GameObject("ReviveBar");
            _barRoot.transform.SetParent(transform, false);
            _barRoot.transform.localPosition = new Vector3(0f, top + 6f * Pixel, 0f); // 번호표·체력 표시와 겹치지 않게 조금 위

            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
            MakeBar("Back", sprite, barBackColor, barSortingOrder, new Vector3(barWidth + 2f * Pixel, 4f * Pixel, 1f), -barWidth * 0.5f - Pixel);
            _barFill = MakeBar("Fill", sprite, barFillColor, barSortingOrder + 1, new Vector3(0f, 2f * Pixel, 1f), -barWidth * 0.5f).transform;
            _barRoot.SetActive(false);
        }

        private GameObject MakeBar(string barName, Sprite sprite, Color color, int order, Vector3 scale, float x)
        {
            var go = new GameObject(barName);
            go.transform.SetParent(_barRoot.transform, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        // 부활 진행 중 반복 효과음(2026-10-14): 쓰러진 사람(대상) 쪽 동기화 진행도가 0보다 크고 아직 쓰러져 있는 동안만.
        // 진행 시작·취소(0으로)·완료(쓰러짐 해제)·대상 이탈(0으로)이 모두 이 조건 하나로 정리되고, 오브젝트가 꺼지거나 파괴되면 아래 OnDisable/Sfx가 멈춘다.
        // 완료음(sfx_revive_done)은 PlayerHealth.ApplyDownedState(쓰러짐 해제).
        private int _reviveLoop;

        private void OnDisable() => AbsorbRoguelike.Audio.Sfx.StopLoop(ref _reviveLoop);

        private void UpdateReviveLoop(bool active)
        {
            if (active && _reviveLoop == 0)
            {
                _reviveLoop = AbsorbRoguelike.Audio.Sfx.StartLoop(AbsorbRoguelike.Audio.SfxId.ReviveLoop, transform);
            }
            else if (!active && _reviveLoop != 0)
            {
                AbsorbRoguelike.Audio.Sfx.StopLoop(ref _reviveLoop);
            }
        }

        private void LateUpdate()
        {
            UpdateReviveLoop(IsDowned && reviveProgress > 0f && isActiveAndEnabled);
            if (_barRoot == null)
            {
                return;
            }

            bool show = IsDowned && reviveProgress > 0f;
            if (_barRoot.activeSelf != show)
            {
                _barRoot.SetActive(show);
            }

            if (show)
            {
                _barFill.localScale = new Vector3(barWidth * reviveProgress, _barFill.localScale.y, 1f);
            }
        }
    }
}
