using System.Collections;
using AbsorbRoguelike;
using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 플레이어 조율자: 입력 맵(PlayerControls, ADR-001) 소유·구독, facing(마우스 조준)과 그 동기화(syncedFacing·CmdSetFacing),
    /// 조작 잠금(사망·쓰러짐·메뉴), 흡수 스킬 값(_absorbSkill)·양털 SyncVar(sheepWool — 로직은 SheepWoolSkill, 단계 5b), 그리고 분리된 컴포넌트의 Tick을 기존 실행 순서대로 부른다
    /// (개 동료 생애주기는 단계 5c에서 DogCompanionSkill로 옮겼다 — 서버 Update 첫 줄 AbsorbSkillHost.TickServer·OnStopServer → AbsorbSkillHost.StopServer).
    /// 리팩터 B안 단계 4a에서 PlayerMotor(이동)·PlayerCameraRig(카메라)·PlayerWeaponView(무기 표시), 단계 4b에서
    /// PlayerGuard(가드·방향 막기)·PlayerDash(대시·대시 무적)·PlayerCombat(공격·판정 요청·서버 판정·연출 전파)를 떼어냈다.
    ///
    /// NOTE(ADR-003): 입력은 로컬 플레이어 소유 인스턴스에서만 처리하고(isLocalPlayer), 공격 판정은 항상 서버에서 한다
    /// (PlayerCombat.RequestHit → 호스트 ServerPerformHit / 원격 CmdPerformHit). 가드·대시 무적·facing도 PlayerGuard.CmdSetGuard·
    /// PlayerDash.CmdSetInvulnerable로 서버에 올려 서버의 PlayerHealth(IDamageFilter — PlayerDash 무시·PlayerGuard 방향 막기)와
    /// 돼지 반격·양털이 원격 플레이어에게도 적용된다. facing·가드는 syncedFacing(여기)·syncedGuarding(PlayerGuard) SyncVar로 관찰자에게 보여 준다(연출 전용).
    /// 지연 보상·예측은 없다(요청이 도착한 시점의 서버 상태로 판정).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : NetworkBehaviour, IPlayerActionState
    {
        // 이동 속도(moveSpeed)는 PlayerMotor로 옮겼다(리팩터 B안 단계 4a).
        // 가드 필드는 PlayerGuard, 대시 필드는 PlayerDash, 공격·흡수 스킬 판정 필드는 PlayerCombat으로 옮겼다(단계 4b).
        [Header("가드 (대검)")]
        [SerializeField] private float guardMoveSpeedMultiplier = 0.4f; // PlayerMotor가 GuardMoveSpeedMultiplier로 읽는다

        // sheepWoolMax는 단계 5b에서 Assets/Data/AbsorbSkills/SheepSkillConfig.asset, dogCompanionPrefab은 단계 5c에서 DogSkillConfig.asset(이름·값 그대로)으로 옮겼다.

        // ── 단계 4b 분리 컴포넌트(Player.prefab에 붙어 있다 — 런타임 AddComponent 안전망·RequireComponent는 두지 않음, 4a와 같은 방침) ──
        /// <summary>공격·판정 컴포넌트. DogCompanion(ServerApplySkillHit·LastHitTarget)·BananaProjectile(ServerOnBananaHit)·HitFx(NotifyNearbyHit)가 부른다.</summary>
        public PlayerCombat Combat => _combat;
        /// <summary>가드 컴포넌트.</summary>
        public PlayerGuard Guard => _guard;
        /// <summary>대시 컴포넌트.</summary>
        public PlayerDash Dash => _dash;

        // 읽기 전용 위임(단계 4b): PlayerMotor(IsDashing·IsGuarding)·HudController(DashCooldownRemaining·HorseDashesLeft)가 기존 이름 그대로 읽는다.
        public bool IsDashing => _dash.IsDashing;

        /// <summary>기능적 가드 상태(이동속도 감소 등). 소유권은 PlayerGuard(단계 4b).</summary>
        public bool IsGuarding => _guard.IsGuarding;

        public float DashCooldownRemaining => _dash.DashCooldownRemaining;

        /// <summary>현재 바라보는 방향(마우스 조준 방향). 가드 판정 등 외부 시스템이 참조한다.</summary>
        public Vector2 FacingDirection => _facingDirection;

        // ── 단계 4a: 분리한 컴포넌트(PlayerMotor·PlayerWeaponView)가 읽는 공용 상태(읽기 전용 — 소유권은 여기, 완전 분리는 단계 5) ──
        /// <summary>로컬 이동 입력(WASD). PlayerMotor.FixedTick이 읽는다.</summary>
        public Vector2 MoveInput => _moveInput;
        public float GuardMoveSpeedMultiplier => guardMoveSpeedMultiplier;
        /// <summary>소 돌진 중(코루틴이 속도를 직접 제어). PlayerMotor가 이동·피격 넉백을 건너뛴다. 쓰기는 OxKnockbackSkill 돌진 코루틴(단계 5a, 옛 PlayerCombat.OxChargeRoutine — 소유권은 여기 유지).</summary>
        public bool IsOxCharging { get; internal set; }
        /// <summary>스윙(공격 연출) 중. PlayerWeaponView가 무기 방향 갱신을 건너뛴다. 쓰기는 PlayerCombat(단계 4b — 소유권은 여기 유지).</summary>
        public bool IsAttacking { get; internal set; }
        /// <summary>현재 흡수 스킬(서버 = 특성, 원격 = 동기화 값). PlayerWeaponView·PlayerGuard·PlayerDash·PlayerCombat이 스킬 분기에 쓴다.</summary>
        public AbsorbRoguelike.Absorption.AbsorbSkill CurrentAbsorbSkill => _absorbSkill;
        // PlayerWeaponView(원격 스윙 연출)가 읽는 스윙 수치 — 필드는 PlayerCombat(단계 4b), 이름은 그대로 위임.
        public float AttackSwingDuration => _combat.AttackSwingDuration;
        public float AttackSwingArcDegrees => _combat.AttackSwingArcDegrees;
        public Color AttackFlashColor => _combat.AttackFlashColor;
        /// <summary>이동·피격 둔화/넉백 컴포넌트. MonsterBehaviorController가 Motor.ServerApplySlow/ServerApplyKnockback을 부른다.</summary>
        public PlayerMotor Motor => _motor;
        /// <summary>흡수 스킬 객체 보관(단계 5a). PlayerCombat·PlayerWeaponView가 현재 스킬의 훅을 묻는다. _absorbSkill과 같은 자리에서 Equip.</summary>
        public AbsorbSkillHost SkillHost => _skillHost;

        /// <summary>말 흡수 시 쿨다운 전까지 남은 대시 횟수(HUD 표시용, 로컬 플레이어 기준).</summary>
        public int HorseDashesLeft => _dash.HorseDashesLeft;

        /// <summary>양 흡수 시 모은 털 수(동기화 값, HUD 표시용).</summary>
        public int SheepWool => sheepWool;

        /// <summary>털 폭발까지 필요한 털 수 = Max(1, SheepSkillConfig.sheepWoolMax)(단계 5b). config가 비면 옛 기본값 3의 결과와 같다.</summary>
        public int SheepWoolMax
        {
            get
            {
                SheepSkillConfig cfg = _skillHost != null ? _skillHost.SheepConfig : null;
                return Mathf.Max(1, cfg != null ? cfg.sheepWoolMax : SheepSkillConfig.DefaultWoolMax);
            }
        }

        /// <summary>서버 전용: 털 수 쓰기(SheepWoolSkill — 털 +1·폭발 리셋·양 해제 리셋, 단계 5b). [SyncVar]라 NetworkBehaviour인 여기 남겼다.</summary>
        internal void ServerSetSheepWool(int value)
        {
            sheepWool = value;
        }

        /// <summary>입력 에셋 인스턴스(단계 4b — Guard·Dash·Combat이 버튼 눌림 상태를 읽는다. 구독·활성화는 여기서만).</summary>
        internal PlayerControls Controls => _controls;

        /// <summary>마우스가 UI 위에 있는가(Update에서 프레임마다 캐시 — PlayerCombat.IsAttackInputBlockedByUI가 읽는다).</summary>
        internal bool PointerOverUI => _pointerOverUI;

        [SyncVar] private int sheepWool; // 양털(단계 5b: 로직은 SheepWoolSkill — SyncVar는 일반 C# 객체에 둘 수 없어 여기 유지)

        // ── facing 시각 전파(2026-09-30 2인 검증 1차 후속, 연출 전용 — 판정은 여전히 서버의 _facingDirection·_isGuarding) ──
        // 서버가 쓰고 모든 관찰자가 읽는다. 소유 클라이언트는 자기 _facingDirection을 쓰고 이 값으로 덮어쓰지 않는다(VisualFacing 참고).
        // 가드 시각 전파(syncedGuarding)는 PlayerGuard로 옮겼다(단계 4b).
        [SyncVar] private Vector2 syncedFacing = Vector2.down;
        private const float FacingSyncInterval = 0.05f;  // facing 전송 간격(초) — 가드 재전송과 같은 주기(2026-10-06 0.1→0.05, 원격 무기 방향을 더 자주)
        private const float FacingSyncMinAngle = 2f;     // 이 각도(도) 미만 변화는 보내지 않는다
        private float _nextFacingSyncTime;
        private Vector2 _lastSentFacing = Vector2.down;
        // 원격 무기 방향(_remoteFacing)·원격 스윙 연출은 PlayerWeaponView로 옮겼다(단계 4a).

        /// <summary>화면에 그릴 방향: 로컬 플레이어는 자기 마우스 방향, 그 외(다른 플레이어 오브젝트)는 동기화된 facing. PlayerGuard 가드 부채꼴이 읽는다.</summary>
        internal Vector2 VisualFacing => isLocalPlayer ? _facingDirection : syncedFacing;

        private PlayerControls _controls;
        private PlayerHealth _health;
        private PlayerRevive _revive;
        private bool _inputBlockedByDowned; // 쓰러짐·부활 진행으로 Gameplay 맵을 껐는지(부활·진행 끝나면 다시 켠다)
        private AbsorbRoguelike.Abilities.MainAbilityController _mainAbility;
        private Rigidbody2D _rigidbody;
        private Vector2 _moveInput;
        private Vector2 _facingDirection = Vector2.down;
        // 피격 둔화·피격 넉백(잡몹 명중 효과)은 PlayerMotor로 옮겼다(단계 4a).
        private AbsorbRoguelike.Absorption.AbsorbSkill _absorbSkill;
        // 능력치 수정치(패시브 특성·메인 능력 속성·분해 버프)는 PlayerStats로 옮겼다(리팩터 B안 단계 3). 기본값 attackDamage와 합산 식은 PlayerCombat(단계 4b, moveSpeed 합산은 PlayerMotor, 단계 4a).
        private PlayerStats _stats;
        private AbsorptionController _absorption;
        // 단계 4a·4b 분리 컴포넌트(Player.prefab에 붙어 있다 — 런타임 AddComponent 안전망은 두지 않음, 단계 3 PlayerStats와 같은 방침).
        // 자체 Update류가 없고 이 클래스가 기존 순서대로 Tick을 부른다.
        private PlayerMotor _motor;
        private PlayerCameraRig _cameraRig;
        private PlayerWeaponView _weaponView;
        private PlayerGuard _guard;
        private PlayerDash _dash;
        private PlayerCombat _combat;
        private AbsorbSkillHost _skillHost;

        private void Awake()
        {
            AbsorbRoguelike.UI.GroundShadow.Attach(gameObject); // 발밑 타원 그림자(2026-10-08)
            _rigidbody = GetComponent<Rigidbody2D>();
            _health = GetComponent<PlayerHealth>();
            _revive = GetComponent<PlayerRevive>();
            _mainAbility = GetComponent<AbsorbRoguelike.Abilities.MainAbilityController>();
            _stats = GetComponent<PlayerStats>(); // Player.prefab에 붙어 있다(리팩터 B안 단계 3) — 런타임 AddComponent 안전망은 두지 않음
            _motor = GetComponent<PlayerMotor>();           // 단계 4a — 위와 같은 방침
            _cameraRig = GetComponent<PlayerCameraRig>();
            _weaponView = GetComponent<PlayerWeaponView>();
            _guard = GetComponent<PlayerGuard>();           // 단계 4b — 위와 같은 방침(RequireComponent·AddComponent 안전망 없음)
            _dash = GetComponent<PlayerDash>();
            _combat = GetComponent<PlayerCombat>();
            _skillHost = GetComponent<AbsorbSkillHost>();  // 단계 5a — 위와 같은 방침

            // 리팩터 B안 단계 3: 아래 컴포넌트들이 PlayerController를 직접 부르던 것을 이벤트로 바꿨다(PC → 각 컴포넌트 단방향).
            // Awake에서 구독 — AbsorptionController.OnStartClient(늦게 접속한 클라이언트의 1회 적용)보다 항상 먼저다.
            if (_mainAbility != null)
            {
                _mainAbility.ServerElementDamageBonusChanged += OnServerElementDamageBonusChanged;
            }

            _absorption = GetComponent<AbsorptionController>();
            if (_absorption != null)
            {
                _absorption.ServerTraitChanged += OnServerTraitChanged;
                _absorption.SyncedSkillChanged += OnSyncedSkillChanged;
            }

            // PlayerHealth.ServerKnockbackRequested 구독은 PlayerMotor.Awake로 옮겼다(단계 4a).

            _guard.CacheRenderer(); // 몸 스프라이트·기본색 캐시(옛 _defaultColor 캐시 자리, 단계 4b — 가드 시각은 PlayerGuard)
            _controls = new PlayerControls();
            AbsorbRoguelike.UI.InputBindingStore.Register(_controls); // 옵션 창에서 바꾼 키 적용(2026-09-30)
            // 옛 '_dash.InitHorseCharges()' 자리 — 단계 5b: 말 연속 대시 횟수 초기값(Max(1, horseDashCharges))은 HorseDoubleDashSkill 생성자(Host Awake)가 같은 값으로 채운다.

            _weaponView.CacheRenderers(); // 무기 렌더러·기본색 캐시(기존 자리 — 뷰의 Awake 순서와 무관)
        }

        /// <summary>
        /// 입력 구독은 이 오브젝트의 소유자(로컬 플레이어)에서만 한다 — 그래야 원격 플레이어의 오브젝트가
        /// 내 마우스/키보드 입력을 잘못 읽어 자기 몸을 움직이는 일이 없다.
        /// </summary>
        public override void OnStartLocalPlayer()
        {
            // 이동 끊김 방지(2026-10-06): 물리는 50Hz(Fixed Timestep 0.02)라 보간 없이는 화면 주사율(60Hz 이상)과 어긋나
            // 카메라는 부드럽게 따라가는데 캐릭터만 툭툭 끊겨 보인다. 내가 조작하는 캐릭터만 보간한다 — 원격 캐릭터는
            // NetworkTransform이 위치를 직접 써서 보간을 켜면 오히려 어긋난다.
            if (_rigidbody != null)
            {
                _rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

            // 마우스 커서를 크로스헤어로 바꿔 공격/뱉기 조준점을 항상 보여준다(커서 이미지만 바뀌어 UI 클릭은 그대로).
            Texture2D crosshair = CreateCrosshairTexture();
            Cursor.SetCursor(crosshair, new Vector2(crosshair.width / 2f, crosshair.height / 2f), CursorMode.Auto);

            // 첫 플레이 힌트(2026-10-16): 내 화면에만 한 번 붙는다(원격 플레이어 오브젝트에는 OnStartLocalPlayer가 안 불림). 플레이어가 사라지면 스스로 정리.
            AbsorbRoguelike.UI.FirstPlayHints.EnsureFor(gameObject);

            _controls.Gameplay.Enable();
            _controls.Gameplay.Move.performed += OnMove;
            _controls.Gameplay.Move.canceled += OnMove;
            _controls.Gameplay.Attack.performed += _combat.OnAttack;       // 단계 4b: 핸들러만 각 컴포넌트로(구독 순서 그대로)
            _controls.Gameplay.Guard.started += _guard.OnGuardStarted;
            _controls.Gameplay.Guard.canceled += _guard.OnGuardCanceled;
            _controls.Gameplay.Dash.performed += _dash.OnDash;
        }

        public override void OnStopLocalPlayer()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

            _controls.Gameplay.Move.performed -= OnMove;
            _controls.Gameplay.Move.canceled -= OnMove;
            _controls.Gameplay.Attack.performed -= _combat.OnAttack;
            _controls.Gameplay.Guard.started -= _guard.OnGuardStarted;
            _controls.Gameplay.Guard.canceled -= _guard.OnGuardCanceled;
            _controls.Gameplay.Dash.performed -= _dash.OnDash;
            _controls.Gameplay.Disable();
        }

        private void Update()
        {
            if (isServer)
            {
                _skillHost.TickServer(); // 옛 ServerUpdateDogCompanion 자리(단계 5c — DogCompanionSkill). 원격 플레이어 오브젝트도 서버에서는 여기까지 돈다

                // 2인 검증 1차 후속: 가드 시각 전파(값이 바뀔 때만 dirty). 호스트 본인 facing은 서버에서 직접 올린다(원격은 Command로 올라옴).
                _guard.ServerTick(); // syncedGuarding = _isGuarding (PlayerGuard, 단계 4b — 같은 자리)
                if (isLocalPlayer && Time.time >= _nextFacingSyncTime && Vector2.Angle(syncedFacing, _facingDirection) >= FacingSyncMinAngle)
                {
                    _nextFacingSyncTime = Time.time + FacingSyncInterval;
                    syncedFacing = _facingDirection;
                }
            }

            // 공격 부채꼴 연출 타이머는 모든 오브젝트에서 줄인다 — 다른 플레이어의 공격 연출(RPC 수신)도 제때 사라지게(2인 검증 1차 후속).
            _combat.TickDebug(Time.deltaTime); // PlayerCombat(단계 4b — 같은 자리)

            if (!isLocalPlayer)
            {
                return; // 대시 쿨타임은 로컬 입력을 막기 위한 로컬 전용 타이머
            }

            // 원격 클라이언트: 가드 중에는 facing을 일정 간격으로 서버에 다시 보낸다(방향성 가드 판정용). 조건·전송은 PlayerGuard(단계 4b) —
            // 보냈으면 true라 아래 else와 한 체인 그대로(조건이 거짓이면 부작용 없음).
            if (_guard.TryResendGuardFacing())
            {
                _lastSentFacing = _facingDirection;
            }
            // 원격 클라이언트: 가드 중이 아닐 때도 facing이 바뀌면 일정 간격으로 올린다 — 다른 화면의 무기 방향 표시용(판정 무관).
            else if (!isServer && !_guard.IsGuarding && Time.time >= _nextFacingSyncTime && Vector2.Angle(_lastSentFacing, _facingDirection) >= FacingSyncMinAngle)
            {
                _nextFacingSyncTime = Time.time + FacingSyncInterval;
                _lastSentFacing = _facingDirection;
                CmdSetFacing(_facingDirection);
            }

            // 입력 콜백 안에서 IsPointerOverGameObject를 부르면 Input System이 경고를 찍으므로, 프레임마다 여기서 캐시한다.
            _pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            // 사망·쓰러짐(멀티)·부활 진행 중(살리는 사람)이면 조작을 막는다 — 맵을 끄면 이동·가드는 canceled로 끝나고 공격·대시·스킬은 들어오지 않는다.
            // 흡수는 AbsorptionController가 IsActionLocked로 따로 막는다. 부활 키는 PlayerRevive의 별도 입력 인스턴스라 영향 없음(2026-10-07).
            if (IsActionLocked)
            {
                if (_controls.Gameplay.enabled)
                {
                    _controls.Gameplay.Disable(); // 진행 중이던 이동 입력도 canceled로 끝나 멈춘다
                }

                _inputBlockedByDowned = true;
                return;
            }

            if (_inputBlockedByDowned)
            {
                // 부활했거나 부활 진행을 멈췄다 — 메뉴가 열려 있지 않으면 다시 켠다(열려 있으면 아래 메뉴 분기가 닫힐 때 켠다).
                _inputBlockedByDowned = false;
                bool menuOpen = AbsorbRoguelike.UI.PauseMenuController.IsOpen || AbsorbRoguelike.UI.OptionsMenuController.IsOpen || AbsorbRoguelike.UI.RunEndScreenController.IsShown;
                if (menuOpen)
                {
                    _inputBlockedByMenu = true;
                }
                else if (!_controls.Gameplay.enabled)
                {
                    _controls.Gameplay.Enable();
                }
            }

            // ESC 메뉴·옵션 창이 열려 있으면 로컬 입력을 받지 않는다(2026-09-30): 맵을 끄면 이동은 canceled로 0, 가드는 기존
            // OnGuardCanceled 경로로 해제, 공격·대시·스킬 시작은 들어오지 않는다. 닫히면 다시 켠다. 판정·규칙 로직은 그대로.
            bool menuBlocksInput = AbsorbRoguelike.UI.PauseMenuController.IsOpen || AbsorbRoguelike.UI.OptionsMenuController.IsOpen || AbsorbRoguelike.UI.RunEndScreenController.IsShown;
            if (menuBlocksInput && _controls.Gameplay.enabled)
            {
                _controls.Gameplay.Disable();
                _inputBlockedByMenu = true;
            }
            else if (!menuBlocksInput && _inputBlockedByMenu)
            {
                _inputBlockedByMenu = false;
                _controls.Gameplay.Enable();
            }

            // 순서 그대로(단계 4b): 말 연속 대시 → 용 브레스 재개 → 대시 쿨타임 감소. (5b: 말 연속 대시 = AbsorbSkillHost.TickLocalDash → HorseDoubleDashSkill)
            _skillHost.TickLocalDash();
            _combat.TryResumeDragonBreath();
            _dash.TickCooldown(Time.deltaTime);
        }

        /// <summary>조작 잠금: 사망·쓰러짐(IsDead) 또는 부활 진행 중(살리는 사람). 이동·공격·가드·대시·흡수 입력을 받지 않는다.</summary>
        public bool IsActionLocked => (_health != null && _health.IsDead) || (_revive != null && _revive.IsReviving);

        /// <summary>
        /// 원격 오브젝트의 무기·가드·귀 채찍 연출, 로컬 오브젝트의 연출 후 카메라 추적. 카메라 추적·흔들림은 PlayerCameraRig.LateTick(단계 4a)이
        /// 로컬 분기 마지막에 처리한다 — 이동/애니메이션이 다 반영된 뒤에 카메라를 옮겨야 하므로 Update가 아니라 LateUpdate 시점.
        /// </summary>
        private void LateUpdate()
        {
            if (!isLocalPlayer)
            {
                // 다른 플레이어 오브젝트(2인 검증 1차 후속): 동기화된 facing으로 무기 방향, 동기화된 가드로 가드 부채꼴, RPC로 받은 귀 채찍 연출.
                if (!_weaponView.IsRemoteSwinging)
                {
                    // 무기 방향은 0.05초마다 오는 값을 그대로 쓰면 뚝뚝 꺾이므로 받은 방향 쪽으로 부드럽게 돌린다(2026-10-06).
                    _weaponView.TickRemoteFacing(syncedFacing, Time.deltaTime);
                }

                _combat.UpdateFollowingCone(_weaponView.RemoteFacing);
                _guard.LateTick();
                _skillHost.LateTickVisual(Time.deltaTime); // 옛 UpdateEarWhipVfx 자리(단계 5a — RabbitEarWhipSkill)
                return;
            }

            // 순서 그대로(단계 4b): 부채꼴 → 가드 디버그 → 귀 링 → 귀 이펙트 → 카메라. (5a: 귀 링·이펙트 = AbsorbSkillHost 훅)
            _combat.UpdateFollowingCone(_facingDirection);
            _guard.LateTick();
            _skillHost.LateTickLocal();                // 옛 UpdateEarWhipRing 자리(단계 5a — 장착 아니면 링 숨김)
            _skillHost.LateTickVisual(Time.deltaTime); // 옛 UpdateEarWhipVfx 자리
            _cameraRig.LateTick(); // 카메라 추적·흔들림(로컬 전용, 단계 4a)
        }

        private void FixedUpdate()
        {
            if (!isLocalPlayer)
            {
                return; // 원격 플레이어의 위치/회전은 NetworkTransform이 동기화해서 채워준다
            }

            UpdateFacingFromMouse();
            _weaponView.UpdateWeaponFacing(_facingDirection);
            _motor.FixedTick(); // 피격 둔화 감소 → 쓰러짐 → 대시/돌진 → 피격 넉백 → 입력 이동(단계 4a, 순서 그대로)
        }

        /// <summary>
        /// 흡수한 패시브 특성을 실제로 적용/해제한다 — AbsorptionController.ServerTraitChanged(서버) 구독 핸들러(옛 ApplyPassiveTrait).
        /// 슬롯이 1개뿐이라 새 특성이 기존 특성을 덮어쓴다(강화 특성은 2026-10-12 제거 — 수치는 특성 값 그대로).
        /// 순서 그대로: _absorbSkill → SkillHost.Equip(단계 5a, 5b부터 그 안에서 양 OnUnequip이 '(서버) 양 아니면 털 0') → 수치(PlayerStats).
        /// </summary>
        private void OnServerTraitChanged(AbsorbRoguelike.Absorption.PassiveTraitData trait)
        {
            _absorbSkill = trait != null ? trait.Skill : AbsorbRoguelike.Absorption.AbsorbSkill.None;
            _skillHost.Equip(_absorbSkill); // 단계 5a: 스킬 객체도 같은 자리에서 교체(서버·호스트). 5b: 양이 벗겨지면 털 0(SheepWoolSkill.OnUnequip, 서버만)

            _stats.SetPassiveTrait(trait);
        }

        /// <summary>
        /// 원격 클라이언트 전용(서버가 아닌 쪽): AbsorptionController.SyncedSkillChanged(SyncedSkill 훅·OnStartClient, 모든 PC) 구독 핸들러
        /// (옛 ApplySyncedAbsorbSkill). _absorbSkill만 채운다. 스탯 배율(이동 속도·공격력)은 OnServerTraitChanged가 서버에서만
        /// 적용하므로 여기서는 건드리지 않는다(이중 적용 금지). 이 값으로 원격 본인 화면의 귀 채찍 사거리 링·귀 직선 연출,
        /// 말 2단 대시 로컬 충전·통과가 동작한다. 판정은 여전히 서버. 호스트(isServer)는 OnServerTraitChanged가 이미 채웠으므로 무시한다.
        /// </summary>
        private void OnSyncedSkillChanged(AbsorbRoguelike.Absorption.AbsorbSkill skill)
        {
            if (isServer)
            {
                return;
            }

            _absorbSkill = skill;
            _skillHost.Equip(_absorbSkill); // 단계 5a: 원격 클라도 같은 자리에서 스킬 객체 교체(늦은 접속 = OnStartClient 1회 이벤트 경로)
        }

        /// <summary>
        /// 메인 능력(무기×속성)의 현재 속성이 주는 대미지 보너스 — MainAbilityController.ServerElementDamageBonusChanged(서버) 구독 핸들러
        /// (옛 ApplyMainAbilityDamageBonus). 패시브 특성과는 별개 축이라 서로 값을 덮어쓰지 않는다.
        /// </summary>
        private void OnServerElementDamageBonusChanged(int bonus)
        {
            _stats.SetMainAbilityDamageBonus(bonus);
        }

        /// <summary>트윈스틱 방식: 이동은 WASD, 바라보는 방향은 마우스 커서 위치를 따른다.</summary>
        private void UpdateFacingFromMouse()
        {
            if (Camera.main == null || UnityEngine.InputSystem.Mouse.current == null)
            {
                return;
            }

            Vector2 mouseScreenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(
                new Vector3(mouseScreenPos.x, mouseScreenPos.y, -Camera.main.transform.position.z));

            Vector2 dir = (Vector2)mouseWorld - (Vector2)transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                _facingDirection = dir.normalized;
            }
        }

        /// <summary>무기 그림 트랜스폼(WeaponElementAura가 속성 오라를 붙인다).</summary>
        /// 단계 4a부터 PlayerWeaponView 소유 — 실행 순서와 무관하게 null이 안 나도록 아직 캐시 전이면 직접 찾는다.
        public Transform WeaponTransform
        {
            get
            {
                PlayerWeaponView view = _weaponView != null ? _weaponView : GetComponent<PlayerWeaponView>();
                return view != null ? view.WeaponTransform : null;
            }
        }

        private void OnMove(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            _moveInput = context.ReadValue<Vector2>();

            // 용(결정 ㉘-1 가): 브레스 루프 중 이동 입력이 들어오면 가드 우선과 같은 방식으로 즉시 끊는다.
            if (_combat.IsDragonMoveLocked && _combat.HasAttackLoop && !_dash.IsDashing)
            {
                _combat.CancelAttackForGuard();
                _guard.RestoreFromInput(); // IsGuarding = Guard.IsPressed(); SetGuardVisual(IsGuarding);
            }
        }

        private bool _pointerOverUI;
        private bool _inputBlockedByMenu; // ESC 메뉴·옵션 때문에 Gameplay 맵을 꺼 둔 상태(닫히면 다시 켠다)

        /// <summary>소유 클라이언트 → 서버: 가드 중이 아닐 때의 facing(연출 전용, 2인 검증 1차 후속). 관찰자용 syncedFacing만 바꾼다 —
        /// 서버 판정용 _facingDirection은 기존처럼 PlayerGuard.CmdSetGuard만 바꾼다(판정 로직 변경 없음).</summary>
        [Command]
        private void CmdSetFacing(Vector2 facing)
        {
            if (facing.sqrMagnitude > 0.0001f)
            {
                syncedFacing = facing.normalized;
            }
        }

        /// <summary>서버 전용: PlayerGuard.CmdSetGuard가 받은 facing을 서버 판정용 _facingDirection과 관찰자용 syncedFacing에 쓴다(옛 CmdSetGuard 본문 그대로).
        /// 호출부가 sqrMagnitude &gt; 0.0001을 확인한 뒤 부른다.</summary>
        internal void ServerSetFacingFromGuard(Vector2 facing)
        {
            _facingDirection = facing.normalized;
            syncedFacing = _facingDirection; // 관찰자 화면 무기·가드 부채꼴 방향(연출 전용)
        }

        public override void OnStartClient()
        {
            // 늦게 접속했거나 이미 가드 중인 다른 플레이어도 처음부터 맞게 보이게(훅은 값이 바뀔 때만 온다). PlayerGuard(단계 4b) — 같은 자리에서 호출.
            _guard.ApplyInitialRemoteGuardVisual();

            // 다른 플레이어 끊김 방지(2026-10-06): 남의 캐릭터는 NetworkTransform(스냅샷 보간)이 위치를 정하는데, 내 화면에서
            // 동적 강체로 물리까지 돌면 벽·몬스터 충돌 보정이 보간 위치와 싸워 덜덜 떨린다. 내가 조작하지 않는 캐릭터는
            // 물리로 움직이지 않게(Kinematic) 한다 — 충돌체는 그대로라 서버 판정(겹침 검사)은 같다. 순간이동은 위치를 직접 쓰므로 무관.
            if (!isLocalPlayer && _rigidbody != null)
            {
                _rigidbody.bodyType = RigidbodyType2D.Kinematic;
                _rigidbody.linearVelocity = Vector2.zero;
                _rigidbody.interpolation = RigidbodyInterpolation2D.None;
            }

            _weaponView.RemoteFacing = syncedFacing;
        }

        // 가운데 점 + 틈을 둔 십자선. 흰색에 검은 테두리를 둘러 밝은/어두운 바닥 어디서든 보이게 한다.
        private static Texture2D CreateCrosshairTexture()
        {
            const int size = 32, c = 16, gap = 3, arm = 7;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[size * size];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);

            void Plot(int x, int y, Color32 color)
            {
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    pixels[y * size + x] = color;
                }
            }

            // 테두리(검정)를 먼저 한 칸 두껍게 칠하고, 그 위에 흰 선을 칠한다.
            for (int pass = 0; pass < 2; pass++)
            {
                int w = pass == 0 ? 1 : 0;
                Color32 col = pass == 0 ? black : white;
                for (int i = gap; i <= gap + arm; i++)
                {
                    for (int t = -w; t <= w; t++)
                    {
                        for (int e = -w; e <= w; e++)
                        {
                            Plot(c + i + e, c + t, col);
                            Plot(c - i + e, c + t, col);
                            Plot(c + t, c + i + e, col);
                            Plot(c + t, c - i + e, col);
                        }
                    }
                }

                for (int t = -w; t <= w; t++)
                {
                    for (int e = -w; e <= w; e++)
                    {
                        Plot(c + t, c + e, col);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // 입력 에셋 인스턴스 정리 — Play 종료처럼 OnStopLocalPlayer가 안 불리고 파괴될 때 Input System의 '맵이 켜진 채 GC됨' 경고를 막는다.
        private void OnDestroy()
        {
            AbsorbRoguelike.UI.InputBindingStore.Unregister(_controls);
            _controls?.Dispose();

            // 리팩터 B안 단계 3: Awake에서 구독한 이벤트 해제.
            if (_mainAbility != null)
            {
                _mainAbility.ServerElementDamageBonusChanged -= OnServerElementDamageBonusChanged;
            }

            if (_absorption != null)
            {
                _absorption.ServerTraitChanged -= OnServerTraitChanged;
                _absorption.SyncedSkillChanged -= OnSyncedSkillChanged;
            }
        }

        /// <summary>서버 정지: 스킬 객체 정리(옛 본문 — 개 동료 제거 — 은 단계 5c에서 DogCompanionSkill.OnStopServer로 이동).</summary>
        public override void OnStopServer()
        {
            _skillHost.StopServer();
        }
    }
}
