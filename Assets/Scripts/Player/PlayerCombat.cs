using System.Collections;
using AbsorbRoguelike.Audio;
using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 공격(리팩터 B안 단계 4b, PlayerController에서 분리): 공격 입력·스윙 루프·용 브레스·히트스톱, 판정 요청(RequestHit →
    /// 호스트 ServerPerformHit / 원격 CmdPerformHit → ValidateHitRequest → ServerPerformHit), 서버 판정(DoConeHit·HitCircleOnce·
    /// ServerApplySkillHit — HitResolver 경로 그대로), 연출 전파(RpcAttackFx includeOwner=false), 부채꼴 연출, 바나나(CmdThrowBanana)·털 폭발 판정·연출(수치는 SheepWoolSkill이 넘김, 단계 5b).
    /// NOTE(ADR-003): 입력은 로컬 플레이어 소유 인스턴스에서만, 판정은 항상 서버. 대상별 1회·적중 회복 1회는 서버의 HitSession(step 묶음)이 지킨다.
    /// 지연 보상·예측은 없다(요청이 도착한 시점의 서버 상태로 판정).
    /// 자체 Update류는 두지 않는다 — PlayerController가 Update(TickDebug, TryResumeDragonBreath)·LateUpdate(UpdateFollowingCone)를
    /// 기존 순서대로 부른다. 공용 상태 IsAttacking·IsOxCharging은 PlayerController 소유(internal setter).
    /// 흡수 스킬(단계 5a): 호랑이·뱀·소·토끼는 스킬 객체(Assets/Scripts/Player/Skills, AbsorbSkillHost.Current)의 훅으로 묻는다 —
    /// 스윙 형태 BuildAttackSpec(ModifyAttack), 공격 대체 TryReplaceAttack(소 돌진), 검증 AllowsHit, 판정 ServerPerformSkillHit
    /// (TigerFollowup·EarWhip·OxCharge), 뱀 둔화 ServerOnConeHitMonster, 귀 직선 ShowAttackFx. 5b의 말·닭은 검증 AllowsHit(Swing·DashPath)와
    /// 대시 경로 배율(PlayerDash.GetDashParams → ModifyDash). 5c의 쥐(명중 플래그 AttackSpec.HitFlags·취약 스택 OnHitEnemy)·용(브레스 TryReplaceAttack·
    /// DragonTick 서버 판정·이동 잠금/재개/재사용 대기 생략 플래그)·원숭이(TryReplaceAttack·CmdThrowBanana 본문 ServerOnAttackCommand)도 훅으로 묻는다 —
    /// 이 클래스에 동물 enum 비교는 hasSkill(None 비교, R02 보스 맨손 1)만 남는다. [Command] CmdThrowBanana·RpcAttackFx BreathCone 수신부는 여기 그대로.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        [Header("공격 (대검)")]
        [Tooltip("부채꼴 판정 반지름(칸). [PLAYTEST]")]
        [SerializeField] private float attackRadius = 1.6f;
        [Tooltip("바라보는 방향 기준 좌우 각도(도). 60이면 총 120° 부채꼴. [PLAYTEST]")]
        [SerializeField] private float attackHalfAngle = 60f;
        [Tooltip("공격할 때마다 판정 범위를 잠깐 그려준다(튜닝용 디버그).")]
        [SerializeField] private bool showAttackDebug = true;
        [SerializeField] private float attackDebugDuration = 0.15f;
        [SerializeField] private Color attackDebugColor = new Color(1f, 1f, 0.3f, 0.3f);
        [SerializeField] private float attackSwingDuration = 0.25f;
        [SerializeField] private float attackSwingArcDegrees = 100f;
        [SerializeField] private float attackRecoveryDelay = 0.2f; // 스윙 후 다음 공격까지의 대기시간 — 이 값과 attackSwingDuration의 합이 곧 '공격속도'
        [SerializeField] private Color attackFlashColor = Color.white;
        [SerializeField] private LayerMask attackHitLayers = ~0;
        [SerializeField] private int attackDamage = 10;

        [Header("흡수 스킬 (GDD 메인 슬롯 표, 수치 전부 [PLAYTEST])")]
        [Tooltip("스킬 없는 기본 공격의 넉백 배율(몬스터 knockbackDistance 기준). 소 흡수와 차이가 나도록 약하게.")]
        [SerializeField] private float baseKnockbackScale = 0.5f;
        // 호랑이·뱀·소·토끼 수치 22개는 단계 5a에서 Assets/Data/AbsorbSkills/*SkillConfig.asset(이름·값 그대로)으로 옮겼다 — AbsorbSkillHost가 참조.

        // 양 털 폭발 수치 3개(sheepBurstRadius·sheepBurstDamageScale·sheepBurstColor)는 단계 5b에서 Assets/Data/AbsorbSkills/SheepSkillConfig.asset(이름·값 그대로)으로 옮겼다.
        // 용 브레스 수치 5개(dragonBreath*)·원숭이 bananaProjectilePrefab은 단계 5c에서 DragonSkillConfig·MonkeySkillConfig.asset(이름·값 그대로)으로 옮겼다.

        [Header("공격 판정 요청 검증 (2인 검증 준비 — 최소 검증, 규칙 수치 아님)")]
        [Tooltip("원격 요청 위치와 서버가 아는 위치의 허용 차이(칸). 넘으면 요청을 무시한다.")]
        [SerializeField] private float hitRequestMaxPositionError = 1.5f;
        [Tooltip("빠르게 움직이는 판정(소 돌진·대시 경로)의 허용 차이(칸) — 지연 동안 움직인 거리만큼 여유.")]
        [SerializeField] private float hitRequestMaxPositionErrorMoving = 3f;
        [Tooltip("최소 간격 검증 배율 — 스윙 주기·틱 간격·재사용 대기의 이 비율보다 빨리 온 새 요청은 무시한다(지연 흔들림 여유).")]
        [SerializeField] private float hitRequestMinIntervalRatio = 0.7f;

        [Header("히트스톱 (2026-10-07 타격감, [PLAYTEST])")]
        [SerializeField] private float hitStopDuration = 0.06f;
        [SerializeField] private float hitStopShakeDuration = 0.08f;
        [SerializeField] private float hitStopShakeAmplitude = 0.04f;
        private float _hitStopRemaining;
        private bool _hitStopUsedThisSwing;

        private PlayerController _controller;
        private PlayerDash _dash;
        private PlayerGuard _guard;
        private PlayerHealth _health;
        private PlayerStats _stats;
        private PlayerWeaponView _weaponView;
        private AbsorbRoguelike.Abilities.MainAbilityController _mainAbility;
        private Rigidbody2D _rigidbody;

        private Coroutine _attackCoroutine;
        private readonly System.Collections.Generic.Dictionary<int, Sprite> _coneSprites = new System.Collections.Generic.Dictionary<int, Sprite>();
        private SpriteRenderer _attackDebugRenderer;
        private float _attackDebugRemaining;
        private UnityEngine.Rendering.Universal.Light2D _breathLight; // 2단계 브레스 빛(부채꼴 이펙트 자식, 각 화면 로컬)
        private bool _coneFollowsFacing; // 용 브레스 부채꼴: 틱 사이에도 매 프레임 지금 방향으로 돌린다(로컬 = 마우스 방향, 원격 = 부드럽게 보간한 동기화 방향)

        /// <summary>서버 전용: 이 플레이어가 마지막으로 때린 살아있는 적과 그 시각(Time.time). 개 동료(DogCompanion)가 파티 전원 것을 조회한다.</summary>
        public GameObject LastHitTarget { get; private set; }
        public float LastHitTime { get; private set; } = float.NegativeInfinity;

        public float AttackSwingDuration => attackSwingDuration;
        /// <summary>스윙 후 다음 공격까지의 대기(용 속성 빈도·원숭이 바나나 연타 방지가 같이 쓴다, 단계 5c).</summary>
        internal float AttackRecoveryDelay => attackRecoveryDelay;
        public float AttackSwingArcDegrees => attackSwingArcDegrees;
        public Color AttackFlashColor => attackFlashColor;
        /// <summary>공격 판정 레이어(흡수 스킬 판정 — 소 돌진·귀 채찍 — 이 같이 쓴다, 단계 5a).</summary>
        internal LayerMask AttackHitLayers => attackHitLayers;
        /// <summary>스킬 없는 기본 공격의 넉백 배율(귀 채찍 판정도 이 값, 단계 5a — 공용 필드라 PlayerCombat에 남김).</summary>
        internal float BaseKnockbackScale => baseKnockbackScale;

        /// <summary>공격 루프(AttackRoutine)가 살아 있는가 — 스윙 사이 재사용 대기 구간도 포함(옛 '_attackCoroutine != null').</summary>
        internal bool HasAttackLoop => _attackCoroutine != null;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _dash = GetComponent<PlayerDash>();
            _guard = GetComponent<PlayerGuard>();
            _health = GetComponent<PlayerHealth>();
            _stats = GetComponent<PlayerStats>();
            _weaponView = GetComponent<PlayerWeaponView>();
            _mainAbility = GetComponent<AbsorbRoguelike.Abilities.MainAbilityController>();
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        private AbsorbRoguelike.Absorption.AbsorbSkill Skill => _controller.CurrentAbsorbSkill;

        /// <summary>현재 흡수 스킬 객체(단계 5a·5b·5c — 12종). None·config 누락이면 null → '스킬 없음'(대검 기본 공격) 경로.</summary>
        private AbsorbSkillBase CurrentSkill => _controller.SkillHost != null ? _controller.SkillHost.Current : null;

        /// <summary>지금 스킬의 스윙 형태 — '스킬 없음' 값(attackRadius·attackHalfAngle·배율 1·추가 타 0·baseKnockbackScale)에 현재 스킬의 ModifyAttack을 덮는다.</summary>
        internal AttackSpec BuildAttackSpec()
        {
            AttackSpec spec = AttackSpec.Default(attackRadius, attackHalfAngle, baseKnockbackScale);
            CurrentSkill?.ModifyAttack(ref spec);
            return spec;
        }

        /// <summary>판정 종류(TigerFollowup·EarWhip·OxCharge·DragonTick)를 맡는 스킬 객체 — 현재 스킬과 무관(AbsorbSkillHost.FindServerHitOwner).</summary>
        private AbsorbSkillBase HitKindOwner(HitKind kind) => _controller.SkillHost != null ? _controller.SkillHost.FindServerHitOwner(kind) : null;

        /// <summary>
        /// 공격 부채꼴 연출 타이머(모든 오브젝트) — PlayerController.Update의 기존 자리(isLocalPlayer return 앞)에서 부른다.
        /// 다른 플레이어의 공격 연출(RPC 수신)도 제때 사라지게(2인 검증 1차 후속).
        /// </summary>
        internal void TickDebug(float deltaTime)
        {
            if (_attackDebugRemaining > 0f)
            {
                _attackDebugRemaining -= deltaTime;
                if (_attackDebugRemaining <= 0f && _attackDebugRenderer != null)
                {
                    _attackDebugRenderer.enabled = false;
                    AbsorbRoguelike.UI.VisualFx.SetLight(_breathLight, false); // 브레스 빛도 이펙트와 함께 사라짐
                }
            }
        }

        /// <summary>
        /// 누군가 직접 명중당했을 때 각 화면에서 불린다(HitFx). 로컬 플레이어가 휘두르는 중이고 맞은 대상이 내 공격 범위 근처면
        /// 내 공격이 맞힌 것으로 보고, 스윙당 한 번 베기를 0.06초 멈칫 + 아주 작은 카메라 흔들림.
        /// </summary>
        public void NotifyNearbyHit(Vector3 targetPosition)
        {
            if (!isLocalPlayer || !_controller.IsAttacking || _hitStopUsedThisSwing)
            {
                return;
            }

            if (Vector2.Distance(transform.position, targetPosition) > CurrentAttackRadius + 0.8f)
            {
                return;
            }

            _hitStopUsedThisSwing = true;
            _hitStopRemaining = hitStopDuration;
            PlayerCameraRig.RequestCameraShake(hitStopShakeDuration, hitStopShakeAmplitude);
        }

        /// <summary>용 흡수 중이고 이동 입력이 있으면 true — 이때는 공격(브레스·대검 휘두르기 모두)이 나가지 않는다. 가드·대시는 무관.
        /// 단계 5c: '용' 조건 = 현재 스킬의 LocksAttackWhileMoving(DragonBreathSkill). 이름은 호출부(OnAttack·AttackRoutine·PlayerController.OnMove) 그대로.</summary>
        internal bool IsDragonMoveLocked
        {
            get
            {
                AbsorbSkillBase skill = CurrentSkill;
                return skill != null && skill.LocksAttackWhileMoving && _controller.MoveInput.sqrMagnitude > 0.01f;
            }
        }

        /// <summary>용: 공격 버튼을 누른 채 이동하다 멈추면 그 자리에서 브레스를 이어간다(누르고 있는 동안 자동 연속 공격과 같은 체감).
        /// PlayerController.Update 로컬 분기 끝(SkillHost.TickLocalDash — 옛 UpdateHorseDashCharges — 다음, 대시 쿨타임 감소 앞)에서 부른다.
        /// 단계 5c: '용' 조건 = 현재 스킬의 ResumesAttackWhenStopped(DragonBreathSkill). 나머지 조건·순서 그대로.</summary>
        internal void TryResumeDragonBreath()
        {
            PlayerControls controls = _controller.Controls;
            AbsorbSkillBase skill = CurrentSkill;
            if (skill == null || !skill.ResumesAttackWhenStopped || IsDragonMoveLocked || _attackCoroutine != null
                || _dash.IsDashing || _controller.IsOxCharging || !controls.Gameplay.Attack.IsPressed() || controls.Gameplay.Guard.IsPressed()
                || IsAttackInputBlockedByUI())
            {
                return;
            }

            var absorption = GetComponent<AbsorptionController>();
            if (absorption != null && absorption.HasDebris)
            {
                return; // 뱉기는 클릭 한 번에 한 번만(OnAttack)
            }

            _attackCoroutine = StartCoroutine(AttackRoutine());
        }

        internal void OnAttack(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            // _isAttacking만 보면 스윙과 스윙 사이(attackRecoveryDelay 대기 구간)에는 이미 false라서,
            // 그 사이에 좌클릭을 놓았다 빠르게 다시 누르면 기존 AttackRoutine 코루틴이 끝나지도 않았는데
            // 새 코루틴이 하나 더 시작되어 두 개가 겹쳐 돌며 공격이 두 번 나가는 버그가 있었다.
            // AttackRoutine 전체 생존 기간을 추적하는 _attackCoroutine으로 체크해야 정확하다.
            if (IsAttackInputBlockedByUI())
            {
                return; // UI(디버그 메뉴 등) 클릭은 공격으로 내보내지 않는다
            }

            if (_attackCoroutine != null || _dash.IsDashing)
            {
                return; // 이미 공격 루프가 돌고 있거나 대시 중에는 새로 시작하지 않는다
            }

            if (_controller.Controls.Gameplay.Guard.IsPressed())
            {
                return; // 가드 우선(2026-09-28 결정 ⑲A): 가드를 누르고 있으면 공격은 나가지 않는다
            }

            if (IsDragonMoveLocked)
            {
                return; // 용(결정 ㉘-1 가): 이동 중에는 공격 입력을 무시한다(뱉기 포함 — 멈춰서 쏜다)
            }

            // 파편을 머금고 있으면 휘두르기 대신 마우스 방향으로 뱉는다(보스 대미지 규칙).
            var absorption = GetComponent<AbsorptionController>();
            if (absorption != null && absorption.HasDebris)
            {
                absorption.CmdSpit(_controller.FacingDirection);
                return;
            }

            _attackCoroutine = StartCoroutine(AttackRoutine());
        }

        /// <summary>대시 종료 직후 공격 버튼이 눌려 있으면 공격 루프를 이어 시작한다(PlayerDash.DashRoutine — 조건 판단은 호출부 그대로).</summary>
        internal void StartAttackLoop()
        {
            _attackCoroutine = StartCoroutine(AttackRoutine());
        }

        // 마우스가 UI(디버그 메뉴 패널 등) 위에 있을 때만 좌클릭을 공격으로 쓰지 않는다.
        // 메뉴가 열려 있어도 패널 밖 게임 화면 좌클릭은 평소처럼 공격/뱉기로 나간다. 가드·대시·이동 입력은 그대로.
        internal bool IsAttackInputBlockedByUI()
        {
            return _controller.PointerOverUI; // PlayerController.Update에서 캐시한 값(직전 프레임 기준 — 콜백 안에서 직접 조회해도 어차피 직전 프레임 값)
        }

        /// <summary>
        /// 가드가 공격보다 우선한다(2026-09-28 결정 ⑲A) — 스윙·재사용 대기 중에 가드를 누르면 공격을 즉시 끊고 가드가
        /// 들린다(CancelAttackForGuard). 이미 들어간 대미지는 유지되고, 아직 안 나간 타격(호랑이 2·3타)만 취소된다.
        /// 누르고 있는 동안은 (스윙 + 재사용 대기시간)이 끝날 때마다 버튼이 여전히 눌려있으면 자동으로 다음 스윙을 이어간다.
        /// </summary>
        private IEnumerator AttackRoutine()
        {
            _guard.ForceOff(); // 대시와 달리 공격은 방패를 든 채로 휘두르는 모션이 어색하므로, 버튼을 안 떼도 시각까지 즉시 끈다

            PlayerControls controls = _controller.Controls;
            do
            {
                yield return PerformSingleSwing();

                // 용 브레스는 속사형이라 재사용 대기 없이 틱 간격(PerformSingleSwing 안)만으로 이어 뿜는다(단계 5c: SkipsAttackRecovery).
                AbsorbSkillBase loopSkill = CurrentSkill;
                if (attackRecoveryDelay > 0f && (loopSkill == null || !loopSkill.SkipsAttackRecovery))
                {
                    yield return new WaitForSeconds(attackRecoveryDelay);
                }
            }
            while (controls.Gameplay.Attack.IsPressed() && !controls.Gameplay.Guard.IsPressed() && !_dash.IsDashing && !IsAttackInputBlockedByUI() && !IsDragonMoveLocked);

            // 공격이 끝난 시점에 가드 버튼이 여전히 눌려있다면 가드 상태(기능+시각)로 복귀
            _guard.RestoreFromInput();
            _attackCoroutine = null;
        }

        private IEnumerator PerformSingleSwing()
        {
            _controller.IsAttacking = true;

            Vector2 facing = _controller.FacingDirection.normalized;
            float baseAngle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;

            // 기본 휘두르기를 통째로 바꾸는 흡수 스킬(TryReplaceAttack — 5a 소 돌진, 5c 원숭이 바나나·용 브레스 틱). 끝나면 공격 상태만 풀고 이번 스윙을 마친다.
            AbsorbSkillBase skill = CurrentSkill;
            if (skill != null && skill.TryReplaceAttack(facing, out IEnumerator replacedAttack))
            {
                yield return replacedAttack;
                _controller.IsAttacking = false;
                yield break;
            }

            // 스윙 형태(단계 5a — 현재 스킬의 ModifyAttack). 토끼 귀 채찍(결정 ㉑A)은 판정 종류가 EarWhip(긴 직선 + 거리별 배율)이고
            // 연출이 부채꼴·베기 프레임 대신 귀 직선(UseSkillSwingFx)이다. 스윙 시간·재사용 대기·자동 연속 스윙·스윙 1회당 적중 회복 1회는
            // 기본 공격과 같다(아래 공통 흐름을 그대로 탄다).
            AttackSpec spec = BuildAttackSpec();
            bool skillSwingFx = spec.UseSkillSwingFx;
            if (skillSwingFx)
            {
                skill?.ShowAttackFx(facing);
            }
            else if (!_weaponView.HasSlashFx || spec.DrawConeOverSlash)
            {
                // 베기 이펙트가 있으면 노란 판정 부채꼴 대신 그것을 보여 준다. 단 범위가 기본과 다른 흡수 스킬(호랑이 좁고 긴 부채꼴·뱀 원 전체)은
                // 차이가 보이도록 부채꼴도 같이 그린다(2026-10-07 사용자 피드백: 뱀이 기본 공격과 달라 보이지 않음).
                ShowAttackDebug(facing, spec.Radius, spec.HalfAngle);
            }

            // 판정은 부채꼴(반지름 attackRadius, 좌우 attackHalfAngle) 안의 적 전부에게 한 번에 들어간다. 휘두르는
            // 무기는 판정과 무관한 연출이다. 추가 타(spec.ExtraHits — 호랑이 2)가 있으면 한 번 휘두를 때 나눠서(아래 스윙 루프) 요청한다.
            // 결과 반영(대미지)은 서버만 한다 (ADR-003) — 판정 시점에 RequestHit로 요청한다(호스트는 직접, 원격은 CmdPerformHit).
            // 배율(spec.DamageScale·KnockbackScale — 호랑이 tigerHitDamageScale·넉백 0, 기본 1)과 스윙 1회당 적중 회복 1회는 서버가 스윙 step 묶음으로 처리한다.
            int extraHits = spec.ExtraHits;
            int totalHits = 1 + extraHits;
            int swingStep = ++_hitStep;
            RequestHit(spec.SwingHitKind, facing, swingStep);
            Sfx.Play(SfxId.SwordSwing, transform.position); // 소유 화면(다른 화면은 RpcAttackFx SwingCone/EarWhip에서)
            int hitsDone = 1;

            if (_weaponView.WeaponTransform != null)
            {
                _weaponView.WeaponTransform.localPosition = _weaponView.WeaponAnchor(facing);
                _weaponView.UpdateWeaponLayer(facing);
                float startAngle = baseAngle - attackSwingArcDegrees * 0.5f;
                float endAngle = baseAngle + attackSwingArcDegrees * 0.5f;

                if (_weaponView.WeaponSpriteRenderer != null)
                {
                    _weaponView.WeaponSpriteRenderer.color = attackFlashColor;
                }

                float elapsed = 0f;
                _hitStopUsedThisSwing = false;
                _hitStopRemaining = 0f;
                while (elapsed < attackSwingDuration)
                {
                    // 히트스톱: 이번 스윙이 처음 맞힌 순간 베기·무기가 잠깐 멈칫한다(NotifyNearbyHit, 로컬 표시만 — 판정은 이미 서버에 요청됨)
                    if (_hitStopRemaining > 0f)
                    {
                        _hitStopRemaining -= Time.deltaTime;
                        yield return null;
                        continue;
                    }

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / attackSwingDuration);
                    _weaponView.WeaponTransform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startAngle, endAngle, t));
                    if (!skillSwingFx)
                    {
                        _weaponView.ShowSlashFrame(facing, t);
                    }

                    // 추가 타(호랑이 연속 할퀴기): 스윙 1/3·2/3 지점에서 추가 타격, 마지막 타에만 넉백(GDD 메인 슬롯 표 — 서버 TigerComboSkill)
                    if (extraHits > 0 && hitsDone < totalHits && t >= hitsDone / (float)totalHits)
                    {
                        ShowAttackDebug(facing, CurrentAttackRadius, CurrentAttackHalfAngle);
                        RequestHit(HitKind.TigerFollowup, facing, swingStep); // 마지막 타 넉백 여부는 서버가 묶음 안 타 수로 판단
                        hitsDone++;
                    }

                    yield return null;
                }

                _weaponView.HideSlash();
                if (_weaponView.WeaponSpriteRenderer != null)
                {
                    _weaponView.WeaponSpriteRenderer.color = _weaponView.WeaponDefaultColor;
                }
            }

            // 회복(ICD)은 대미지량과 무관하게 한 번 휘두를 때 최대 1회(GDD 회복 시스템 설계 원칙) — 서버 HitSession이 스윙 step 묶음에서
            // 처음 실제로 깎은 타격 때 1회 시도한다(가드로 스윙을 끊어도 이미 맞힌 스윙은 회복이 들어간다 — 기존 동작과 같음).
            _controller.IsAttacking = false;
        }

        /// <summary>가드 입력으로 공격 루프를 끊는다(PlayerGuard.OnGuardStarted·PlayerController.OnMove 용 브레스·PlayerDash.TryStartDash).
        /// 이미 맞힌 스윙의 적중 회복(ICD)은 서버 HitSession이 첫 적중 때 이미 시도했으므로 여기서 할 일은 없다.</summary>
        internal void CancelAttackForGuard()
        {
            StopCoroutine(_attackCoroutine);
            _attackCoroutine = null;
            _controller.IsAttacking = false;
            if (_weaponView.WeaponSpriteRenderer != null)
            {
                _weaponView.WeaponSpriteRenderer.color = _weaponView.WeaponDefaultColor;
            }
        }

        /// <summary>부채꼴 안의 적에게 한 번 대미지를 준다. 흡수 스킬에 따라 둔화(뱀 — ServerOnConeHitMonster)·강한 넉백(소 — AttackSpec.KnockbackBaseScale)이 붙는다.
        /// 살아있는 적을 하나라도 맞혔으면 true. radiusOverride/halfAngleOverride가 0보다 크면 현재 스킬
        /// 반경/반각 대신 쓴다(돼지 반격 전용 — 기본공격·호랑이·뱀 호출은 생략해서 동작 그대로).</summary>
        /// elementApplications: 명중 1회당 속성 적용 횟수(기본 1, 용 브레스는 기본 공격 한 주기에 1·나머지 틱 0).
        internal bool DoConeHit(Vector2 facing, float damageScale, float knockbackScale, float radiusOverride = -1f, float halfAngleOverride = -1f, int elementApplications = 1)
        {
            if (!isServer)
            {
                return false; // 서버 전용(2인 검증 준비: 호출부가 전부 서버 — ServerPerformHit·돼지 반격·털 폭발)
            }

            Vector2 origin = HitOrigin;
            var alreadyHit = new System.Collections.Generic.HashSet<GameObject>();
            bool dealtHit = false;
            int baseDamage = attackDamage + _stats.AttackDamageBonusSum;
            int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * damageScale));
            bool hasSkill = Skill != AbsorbRoguelike.Absorption.AbsorbSkill.None;
            float knockback = knockbackScale * BuildAttackSpec().KnockbackBaseScale; // 기본 baseKnockbackScale, 소 oxKnockbackScale(ModifyAttack)
            AbsorbRoguelike.Combat.HitFlags hitFlags = BuildAttackSpec().HitFlags; // 기본 None, 쥐 RatGnaw(RatGnawSkill.ModifyAttack, 단계 5c)

            float halfAngle = halfAngleOverride > 0f ? halfAngleOverride : CurrentAttackHalfAngle;
            float radius = radiusOverride > 0f ? radiusOverride : CurrentAttackRadius;
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(origin, radius, attackHitLayers))
            {
                if (hit.gameObject == gameObject || !alreadyHit.Add(hit.gameObject) || !IsInAttackCone(hit, facing, halfAngle))
                {
                    continue; // 자기 자신, 콜라이더 여러 개로 중복된 대상, 부채꼴 밖 제외
                }

                // 잡몹/보스 공통 처리(리팩터 B안 단계 1) — HitResolver가 기존 순서 그대로 실행한다:
                // 잡몹 = 대미지(정면 방어) → 살아 있으면 속성 elementApplications회·기록 → 넉백(knockback > 0일 때 배율, 죽었어도).
                // 보스 = 대미지(흡수 스킬이면 정상, 맨손 무기면 BossStub이 1 고정) → hasSkill일 때만 속성 → 처치 안 됐으면 기록.
                var info = new AbsorbRoguelike.Combat.HitInfo(damage, origin, netIdentity,
                    hasSkill ? DamageSource.AbsorbSkill : DamageSource.Weapon,
                    flags: hitFlags);
                var options = new AbsorbRoguelike.Combat.HitOptions
                {
                    ElementApplications = elementApplications,
                    ApplyElementToBoss = hasSkill,
                    RecordHit = true,
                    Knockback = AbsorbRoguelike.Combat.KnockbackMode.Scale,
                    KnockbackValue = knockback,
                };
                var result = AbsorbRoguelike.Combat.HitResolver.Resolve(hit.gameObject, info, options,
                    (t, isBoss) => ApplyElementOnHit(t, damage, isBoss, hasSkill), OnEnemyHitServer);

                if (result.TargetKind == AbsorbRoguelike.Combat.HitTargetKind.Monster)
                {
                    dealtHit |= result.Damaged; // 적중 회복은 체력이 실제로 깎였을 때만(2026-09-28)
                    // 스킬 후처리(뱀 둔화): 생존 무관, 넉백 뒤(기존 순서) — 단계 5a부터 SnakeBindSkill.ServerOnConeHitMonster.
                    if (result.Behavior != null)
                    {
                        CurrentSkill?.ServerOnConeHitMonster(result.Behavior);
                    }
                }
                else if (result.TargetKind == AbsorbRoguelike.Combat.HitTargetKind.Boss)
                {
                    // 적중 회복은 체력이 실제로 깎였을 때만 — 개인 할당량 초과분·맨몸 무기 튕김(1 고정)은 제외(2026-09-28)
                    dealtHit |= hasSkill && result.Damaged;
                }
            }

            return dealtHit;
        }
        /// <summary>판정 요청 종류. Command로 보내므로 정수값을 바꾸지 않는다.</summary>
        public enum HitKind : byte
        {
            Swing = 0,          // 대검 기본 휘두르기(뱀 원형·쥐 갉기·돼지·양 등 스킬이 부채꼴을 바꾸는 경우 포함), 호랑이 1타
            TigerFollowup = 1,  // 호랑이 2·3타(같은 스윙 step)
            EarWhip = 2,        // 토끼 귀 채찍
            DragonTick = 3,     // 용 브레스 틱
            OxCharge = 4,       // 소 돌진 중 판정(같은 돌진 step 동안 여러 번)
            DashPath = 5,       // 말·닭 대시 경로 판정(같은 대시 step 동안 여러 번)
        }

        /// <summary>서버 전용: 스윙·돌진·대시 1회분 판정 묶음. step이 같은 요청은 같은 묶음 — 대상별 1회(hitSet)와 적중 회복 1회를 여기서 지킨다.</summary>
        internal class HitSession
        {
            public bool Active;
            public HitKind Kind;
            public int Step;
            public float StartTime;
            public bool Healed;
            public int Followups;
            public readonly System.Collections.Generic.HashSet<GameObject> HitSet = new System.Collections.Generic.HashSet<GameObject>();
        }

        private readonly HitSession _attackSession = new HitSession(); // 서버 전용 — 스윙·호랑이·귀 채찍·용 틱·소 돌진
        private readonly HitSession _dashSession = new HitSession();   // 서버 전용 — 말·닭 대시 경로
        private int _hitStep;                // 소유 클라이언트 — 스윙·돌진·대시·틱마다 1씩 올리는 묶음 id
        private bool _hasHitOriginOverride;  // 서버 — 원격 요청 판정 중에는 요청 위치를 판정 원점으로 쓴다
        private Vector2 _hitOriginOverride;

        /// <summary>판정 원점. 원격 요청을 처리하는 동안은 요청 위치(검증 통과한 값), 그 외(호스트·서버 자체 판정)는 서버 transform 위치.</summary>
        internal Vector2 HitOrigin => _hasHitOriginOverride ? _hitOriginOverride : (Vector2)transform.position;

        /// <summary>
        /// 속성 부가 효과를 명중한 대상 1마리당 1회 건다(서버 전용, 효과 계산은 StatusEffectReceiver). 그래서 기본 부채꼴은 1회,
        /// 호랑이 3연타는 3스택, 뱀 360°는 주변 전원 1회씩, 소 돌진은 돌진 1회당 대상별 1회가 된다. 뱉은 파편은 무속성.
        /// 보스에게는 흡수 스킬 명중일 때만 건다 — 맨몸 무기 명중(보스 대미지 1 고정)은 효과 없음(유저 결정 대기, 기본값).
        /// </summary>
        internal void ApplyElementOnHit(GameObject target, int damage, bool isBoss, bool hasSkill)
        {
            if (!isServer || _mainAbility == null)
            {
                return;
            }

            AbsorbRoguelike.Abilities.ElementType element = _mainAbility.CurrentElement;
            if (element == AbsorbRoguelike.Abilities.ElementType.None || (isBoss && !hasSkill))
            {
                return;
            }

            var receiver = target.GetComponent<StatusEffectReceiver>();
            if (receiver != null)
            {
                receiver.ApplyOnHit(element, damage, netIdentity);
            }
        }

        // 대상 콜라이더에서 플레이어에 가장 가까운 점이나 중심 중 하나라도 부채꼴 각도 안이면 맞는다
        // (보스처럼 큰 대상의 가장자리를 쳐도 맞도록). 이미 겹쳐 있으면 각도와 무관하게 맞는다.
        private bool IsInAttackCone(Collider2D target, Vector2 facing, float halfAngle)
        {
            if (halfAngle >= 180f)
            {
                return true; // 뱀: 원 전체
            }

            Vector2 origin = HitOrigin;
            Vector2 toClosest = target.ClosestPoint(origin) - origin;
            if (toClosest.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            Vector2 toCenter = (Vector2)target.bounds.center - origin;
            return Vector2.Angle(facing, toClosest) <= halfAngle || Vector2.Angle(facing, toCenter) <= halfAngle;
        }

        /// <summary>현재 스윙 부채꼴 반지름 — 객체화된 스킬이 있으면 그 ModifyAttack 결과(호랑이·뱀), 없으면 attackRadius(단계 5a).</summary>
        internal float CurrentAttackRadius => BuildAttackSpec().Radius;

        /// <summary>현재 스윙 부채꼴 반각 — 객체화된 스킬이 있으면 그 ModifyAttack 결과(호랑이·뱀 180), 없으면 attackHalfAngle(단계 5a).</summary>
        internal float CurrentAttackHalfAngle => BuildAttackSpec().HalfAngle;

        internal void UpdateFollowingCone(Vector2 facing)
        {
            if (!_coneFollowsFacing || _attackDebugRenderer == null || !_attackDebugRenderer.enabled || facing.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _attackDebugRenderer.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);
        }

        internal void ShowAttackDebug(Vector2 facing, float radius, float halfAngle, float duration = -1f, Color? color = null, bool followFacing = false)
        {
            if (!showAttackDebug)
            {
                return;
            }

            _coneFollowsFacing = followFacing;
            AbsorbRoguelike.UI.VisualFx.SetLight(_breathLight, false); // 브레스가 아닌 부채꼴이면 꺼 둔다(브레스는 바로 뒤 ShowBreathLight)

            if (_attackDebugRenderer == null)
            {
                var go = new GameObject("AttackDebugCone");
                go.transform.SetParent(transform, false);
                _attackDebugRenderer = go.AddComponent<SpriteRenderer>();
                _attackDebugRenderer.color = attackDebugColor;
                _attackDebugRenderer.sortingOrder = 9;
            }

            _attackDebugRenderer.sprite = GetConeSprite(halfAngle);
            _attackDebugRenderer.color = color ?? attackDebugColor; // 용 브레스(속성색)·털 폭발만 색을 바꾼다
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            _attackDebugRenderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            _attackDebugRenderer.transform.localScale = Vector3.one * radius;
            _attackDebugRenderer.enabled = true;
            _attackDebugRemaining = duration > 0f ? duration : attackDebugDuration;
        }

        /// <summary>
        /// 용 브레스 빛(2026-10-15, 비주얼 2단계): 방금 띄운 부채꼴 이펙트 자식으로 주황 점광원을 켠다(부채꼴 반경 중간, 방향을 따라 돈다).
        /// 부채꼴이 사라질 때(TickDebug) 같이 끈다. 판정·대미지와 무관한 표시 전용.
        /// </summary>
        internal void ShowBreathLight()
        {
            if (_attackDebugRenderer == null || !_attackDebugRenderer.enabled)
            {
                return;
            }

            AbsorbRoguelike.UI.VisualFxConfig fx = AbsorbRoguelike.UI.VisualFx.Settings;
            if (_breathLight == null)
            {
                _breathLight = AbsorbRoguelike.UI.VisualFx.AttachPointLight(_attackDebugRenderer.transform, fx.breathLightColor, fx.breathLightRadius, fx.breathLightIntensity);
                if (_breathLight == null)
                {
                    return;
                }

                _breathLight.transform.localPosition = new Vector3(0.5f, 0f, 0f); // 부채꼴 로컬 스케일 = 반경 → 반경의 절반 지점
            }

            _breathLight.color = fx.breathLightColor;
            _breathLight.intensity = fx.breathLightIntensity;
            _breathLight.pointLightOuterRadius = fx.breathLightRadius; // 점광원 반경은 트랜스폼 스케일 영향 없음
            AbsorbRoguelike.UI.VisualFx.SetLight(_breathLight, true);
        }

        // ── 공격 판정 요청 / 서버 검증 (2인 검증 준비, 2026-09-29) ─────────────────────────────────────────────

        /// <summary>
        /// 소유 클라이언트의 판정 요청 진입점. 호스트(isServer && isLocalPlayer)는 Command를 거치지 않고 바로 서버 판정을 한다 —
        /// 그래서 호스트는 한 번만 판정된다. 원격 클라이언트는 facing·현재 위치·묶음 id(step)를 CmdPerformHit로 보낸다.
        /// </summary>
        internal void RequestHit(HitKind kind, Vector2 facing, int step)
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (isServer)
            {
                ServerPerformHit(kind, facing, step);
            }
            else
            {
                CmdPerformHit(kind, facing, transform.position, step);
            }
        }

        [Command]
        private void CmdPerformHit(HitKind kind, Vector2 facing, Vector2 position, int step)
        {
            if (!ValidateHitRequest(kind, position, step, out string reason))
            {
                Debug.LogWarning($"[PlayerCombat] {name} hit request {kind} (step {step}) rejected: {reason}");
                return;
            }

            // 판정 원점 = 요청 위치(검증 통과 — 서버 위치와 허용 차이 이내). 원격 본인 화면에서 휘두른 자리와 판정 자리를 맞춘다.
            _hasHitOriginOverride = true;
            _hitOriginOverride = position;
            try
            {
                ServerPerformHit(kind, facing, step);
            }
            finally
            {
                _hasHitOriginOverride = false;
            }
        }

        /// <summary>
        /// 원격 판정 요청 최소 검증(서버). 지연 보상·치트 방지 수준이 아니라 "명백히 이상한 요청만 거른다" — 사망, 종류에 맞는 흡수 스킬,
        /// 새 묶음의 최소 간격(주기 × hitRequestMinIntervalRatio), 같은 묶음 요청의 지속 시간, 위치 차이.
        /// </summary>
        private bool ValidateHitRequest(HitKind kind, Vector2 position, int step, out string reason)
        {
            reason = null;
            if (_health != null && _health.IsDead)
            {
                reason = "dead";
                return false;
            }

            AbsorbRoguelike.Absorption.AbsorbSkill skill = Skill; // 거부 로그용
            // 스킬이 허용하는 판정 종류만(단계 5c: 12종 모두 AllowsHit). None(·config 누락)은 대검 스윙만.
            AbsorbSkillBase current = CurrentSkill;
            bool skillOk = current != null ? current.AllowsHit(kind) : kind == HitKind.Swing;

            if (!skillOk)
            {
                reason = $"skill mismatch ({skill})";
                return false;
            }

            bool moving = kind == HitKind.OxCharge || kind == HitKind.DashPath;
            float maxError = moving ? hitRequestMaxPositionErrorMoving : hitRequestMaxPositionError;
            if (Vector2.Distance(position, transform.position) > maxError)
            {
                reason = $"position error {Vector2.Distance(position, transform.position):0.00} > {maxError}";
                return false;
            }

            HitSession session = kind == HitKind.DashPath ? _dashSession : _attackSession;
            float now = Time.time;
            if (kind == HitKind.TigerFollowup)
            {
                if (!session.Active || session.Kind != HitKind.Swing || session.Step != step || session.Followups >= 2)
                {
                    reason = "tiger followup without matching swing";
                    return false;
                }

                return true;
            }

            bool continuing = session.Active && session.Kind == kind && session.Step == step;
            if (continuing)
            {
                // 같은 묶음은 소 돌진·대시 경로만 여러 번 온다. 돌진/대시 시간 + 여유(지연 흔들림) 안에서만 받는다.
                float window = (kind == HitKind.OxCharge ? SkillSessionDuration(kind) : _dash.DashDuration) + 0.5f;
                if (!moving || now - session.StartTime > window)
                {
                    reason = moving ? "session expired" : "duplicate request";
                    return false;
                }

                return true;
            }

            if (session.Active && session.Kind == kind)
            {
                float minInterval = MinSessionInterval(kind) * hitRequestMinIntervalRatio;
                if (now - session.StartTime < minInterval)
                {
                    reason = $"too fast ({now - session.StartTime:0.000}s < {minInterval:0.000}s)";
                    return false;
                }
            }

            return true;
        }

        // 스킬이 맡는 판정 종류의 묶음 지속 시간(소 돌진 oxChargeDuration — OxKnockbackSkill). 맡는 스킬이 없으면 0.
        private float SkillSessionDuration(HitKind kind)
        {
            AbsorbSkillBase owner = HitKindOwner(kind);
            return owner != null ? owner.GetHitSessionDuration(kind) : 0f;
        }

        // 스킬이 맡는 판정 종류의 새 묶음 최소 주기(용 브레스 dragonBreathTickInterval — DragonBreathSkill). 맡는 스킬이 없으면 0.
        private float SkillMinInterval(HitKind kind)
        {
            AbsorbSkillBase owner = HitKindOwner(kind);
            return owner != null && owner.TryGetMinHitInterval(kind, out float interval) ? interval : 0f;
        }

        // 종류별 새 묶음 사이의 정상 주기(초). 검증은 이 값 × hitRequestMinIntervalRatio.
        private float MinSessionInterval(HitKind kind)
        {
            switch (kind)
            {
                case HitKind.DragonTick: return SkillMinInterval(kind); // DragonBreathSkill — dragonBreathTickInterval(단계 5c)
                case HitKind.OxCharge: return SkillSessionDuration(kind) + attackRecoveryDelay;
                case HitKind.DashPath: return _dash.DashDuration; // 말 연속 대시는 쿨다운 없이 이어진다 — 대시 이동 시간이 최소 간격
                default: return attackSwingDuration + attackRecoveryDelay; // 스윙·귀 채찍
            }
        }

        /// <summary>
        /// 판정 실행(서버 전용). 호스트 본인은 RequestHit에서 바로, 원격은 CmdPerformHit 검증 뒤에 온다. 판정 함수(DoConeHit·HitCircleOnce,
        /// 스킬 객체의 귀 채찍·소 돌진 판정 — 단계 5a에서 RabbitEarWhipSkill·OxKnockbackSkill로 이동)는 기존 그대로라 대상별 1회·속성·보스 대미지 규칙·취약·갑옷 규칙이 같다.
        /// 적중 회복(ICD)은 묶음(스윙·틱·돌진·대시 1회)마다 처음 실제로 깎은 타격 때 1회만 시도한다.
        /// </summary>
        private void ServerPerformHit(HitKind kind, Vector2 facing, int step)
        {
            if (!isServer)
            {
                return;
            }

            if (facing.sqrMagnitude < 0.0001f)
            {
                facing = Vector2.down;
            }

            facing = facing.normalized;
            HitSession session = kind == HitKind.DashPath ? _dashSession : _attackSession;
            bool newSession = kind != HitKind.TigerFollowup && !(session.Active && session.Kind == kind && session.Step == step);
            // 연출 전파(2인 검증 1차 후속): 새 묶음(스윙·틱·돌진·대시 1회)마다 한 번, 호랑이 추가 타는 유효할 때마다. 판정 결과와 무관.
            if (newSession || (kind == HitKind.TigerFollowup && session.Active && session.Kind == HitKind.Swing && session.Step == step && session.Followups < 2))
            {
                ServerBroadcastHitFx(kind, facing);
            }

            if (newSession)
            {
                session.Active = true;
                session.Kind = kind;
                session.Step = step;
                session.StartTime = Time.time;
                session.Healed = false;
                session.Followups = 0;
                session.HitSet.Clear();
            }

            bool dealtHit = false;
            switch (kind)
            {
                case HitKind.Swing:
                {
                    // 스윙 1타 배율은 현재 스킬의 AttackSpec(호랑이 tigerHitDamageScale·넉백 0, 기본 1·1).
                    AttackSpec spec = BuildAttackSpec();
                    dealtHit = DoConeHit(facing, spec.DamageScale, spec.KnockbackScale);
                    break;
                }
                case HitKind.TigerFollowup:
                {
                    if (!session.Active || session.Kind != HitKind.Swing || session.Step != step || session.Followups >= 2)
                    {
                        return; // 호스트 경로도 같은 규칙(스윙당 추가 타 2회)
                    }

                    session.Followups++;
                    dealtHit = ServerSkillHit(kind, facing, session); // TigerComboSkill — 마지막 타에만 넉백
                    break;
                }
                case HitKind.EarWhip:
                    dealtHit = ServerSkillHit(kind, facing, session); // RabbitEarWhipSkill
                    break;
                case HitKind.DragonTick:
                    dealtHit = ServerSkillHit(kind, facing, session); // DragonBreathSkill — 속성은 기본 공격 한 주기에 1회(단계 5c)
                    break;
                case HitKind.OxCharge:
                    dealtHit = ServerSkillHit(kind, facing, session); // OxKnockbackSkill — session.HitSet으로 돌진 1회당 대상별 1회
                    break;
                case HitKind.DashPath:
                {
                    // 경로 배율·넉백은 서버 값으로 다시 계산한다(클라이언트 값을 믿지 않음 — 판정 시점 현재 스킬의 ModifyDash). 말/닭이 아니면 PathDamageScale = 0 → 판정 없음.
                    PlayerDash.DashParams p = _dash.GetDashParams();
                    if (p.PathDamageScale > 0f)
                    {
                        dealtHit = HitCircleOnce(_dash.DashPathHitRadius, session.HitSet, ComputeSkillDamage(p.PathDamageScale), p.PathKnockbackDistance);
                    }

                    break;
                }
            }

            if (dealtHit && !session.Healed && _health != null)
            {
                session.Healed = true;
                _health.OnDealtHit();
            }
        }

        /// <summary>스킬이 맡는 판정 종류(TigerFollowup·EarWhip·OxCharge·DragonTick)의 서버 판정 — 판정 시점 스킬과 무관하게 그 종류를 맡는 스킬 객체가
        /// 처리한다(단계 5a 이전 동작과 같음). 맡는 객체가 없으면(config 누락) 판정 없음.</summary>
        private bool ServerSkillHit(HitKind kind, Vector2 facing, HitSession session)
        {
            AbsorbSkillBase owner = HitKindOwner(kind);
            return owner != null && owner.ServerPerformSkillHit(kind, facing, session);
        }

        // ── 공격 연출 전파(2026-09-30 2인 검증 1차 후속, 연출 전용 — 판정·대미지 무관) ─────────────────────────────
        // 공격자 본인 화면은 기존처럼 로컬에서 바로 그린다. 서버는 판정 요청(ServerPerformHit — 호스트는 직접, 원격은 CmdPerformHit 검증 통과 후)의
        // 새 묶음마다 같은 연출을 includeOwner=false ClientRpc로 다른 클라이언트(호스트 포함)에 보낸다. 반경·각도·색은 서버가 가진 값으로 계산한다.

        private static class AttackFx
        {
            public const byte Cone = 0;       // 부채꼴만(호랑이 추가 타·용 브레스·소 돌진·대시 경로·돼지 반격·털 폭발)
            public const byte SwingCone = 1;  // 부채꼴 + 대검 휘두르기(기본 공격)
            public const byte EarWhip = 2;    // 귀 채찍 막대 + 대검 휘두르기
            public const byte BreathCone = 3; // 용 브레스 부채꼴 — 다음 틱까지 동기화 방향을 부드럽게 따라 돈다
        }

        // 서버: ServerPerformHit에서 새 묶음(또는 호랑이 추가 타)일 때 부른다.
        private void ServerBroadcastHitFx(HitKind kind, Vector2 facing)
        {
            switch (kind)
            {
                case HitKind.Swing:
                    RpcAttackFx(AttackFx.SwingCone, facing, CurrentAttackRadius, CurrentAttackHalfAngle, attackDebugDuration, attackDebugColor);
                    break;
                case HitKind.TigerFollowup:
                    RpcAttackFx(AttackFx.Cone, facing, CurrentAttackRadius, CurrentAttackHalfAngle, attackDebugDuration, attackDebugColor);
                    break;
                case HitKind.EarWhip:
                    RpcAttackFx(AttackFx.EarWhip, facing, 0f, 0f, 0f, attackDebugColor);
                    break;
                case HitKind.DragonTick:
                {
                    AbsorbSkillBase owner = HitKindOwner(kind); // 반경·반각·틱 간격·브레스 색 = DragonBreathSkill(단계 5c)
                    if (owner != null && owner.TryGetFollowingHitFx(kind, out float fxRadius, out float fxHalfAngle, out float fxDuration, out Color fxColor))
                    {
                        RpcAttackFx(AttackFx.BreathCone, facing, fxRadius, fxHalfAngle, fxDuration, fxColor);
                    }

                    break;
                }
                case HitKind.OxCharge:
                {
                    AbsorbSkillBase owner = HitKindOwner(kind); // 반경·시간 = OxSkillConfig(oxChargeHitRadius·oxChargeDuration)
                    if (owner != null && owner.TryGetHitFx(kind, out float fxRadius, out float fxHalfAngle, out float fxDuration))
                    {
                        RpcAttackFx(AttackFx.Cone, facing, fxRadius, fxHalfAngle, fxDuration, attackDebugColor);
                    }

                    break;
                }
                case HitKind.DashPath:
                    RpcAttackFx(AttackFx.Cone, facing, _dash.DashPathHitRadius, 180f, _dash.DashDuration, attackDebugColor);
                    break;
            }
        }

        /// <summary>서버: 돼지 반격 부채꼴을 다른 화면에 그린다(PlayerGuard.OnGuardBlockedServer — 옛 RpcAttackFx(Cone, …, attackDebugDuration, attackDebugColor) 그대로).</summary>
        internal void ServerBroadcastConeFx(Vector2 direction, float radius, float halfAngle)
        {
            RpcAttackFx(AttackFx.Cone, direction, radius, halfAngle, attackDebugDuration, attackDebugColor);
        }

        /// <summary>소유 클라이언트: 새 판정 묶음 id(스윙·돌진·대시·틱마다 1씩). PlayerDash.DashRoutine이 대시 1회 묶음에 쓴다(옛 '++_hitStep').</summary>
        internal int NextHitStep() => ++_hitStep;

        /// <summary>
        /// 털 폭발 판정(서버 전용, SheepWoolSkill이 털이 sheepWoolMax에 닿은 순간 부른다 — 단계 5b): 반경 sheepBurstRadius 원
        /// (DoConeHit 반각 180 = 원 전체) 기본공격 150% + 흡수 스킬 판정·속성, 실제로 깎았으면 적중 회복. 보호막·로그는 호출부(기존 순서 그대로).
        /// </summary>
        internal void ServerSheepBurstHit(Vector2 facing, float sheepBurstRadius, float sheepBurstDamageScale)
        {
            bool dealtHit = DoConeHit(facing, sheepBurstDamageScale, 1f, sheepBurstRadius, 180f);
            if (dealtHit && _health != null)
            {
                _health.OnDealtHit();
            }
        }

        /// <summary>털 폭발 연출(서버): 호스트 본인은 직접, 원격 소유자는 TargetShowSheepBurst, 다른 화면은 RpcAttackFx(2인 검증 1차 후속). 순서 그대로.
        /// 반경·색은 SheepWoolSkill이 SheepSkillConfig에서 넘긴다(단계 5b).</summary>
        internal void ServerShowSheepBurst(Vector2 facing, float sheepBurstRadius, Color sheepBurstColor)
        {
            if (isLocalPlayer)
            {
                ShowAttackDebug(facing, sheepBurstRadius, 180f, 0.3f, sheepBurstColor);
            }
            else if (connectionToClient != null)
            {
                TargetShowSheepBurst(connectionToClient);
            }

            RpcAttackFx(AttackFx.Cone, facing, sheepBurstRadius, 180f, 0.3f, sheepBurstColor);
        }

        [ClientRpc(includeOwner = false)]
        private void RpcAttackFx(byte fx, Vector2 facing, float radius, float halfAngle, float duration, Color color)
        {
            if (isLocalPlayer)
            {
                return; // 안전장치 — 소유자는 이미 로컬에서 그렸다
            }

            switch (fx)
            {
                case AttackFx.Cone:
                    ShowAttackDebug(facing, radius, halfAngle, duration, color);
                    break;
                case AttackFx.BreathCone:
                    ShowAttackDebug(facing, radius, halfAngle, duration, color, followFacing: true);
                    ShowBreathLight();
                    break;
                case AttackFx.SwingCone:
                    if (!_weaponView.HasSlashFx || BuildAttackSpec().DrawConeOverSlash)
                    {
                        ShowAttackDebug(facing, radius, halfAngle, duration, color);
                    }

                    _weaponView.PlayRemoteSwing(facing, true);
                    Sfx.Play(SfxId.SwordSwing, transform.position);
                    break;
                case AttackFx.EarWhip:
                    HitKindOwner(HitKind.EarWhip)?.ShowAttackFx(facing); // 귀 직선 — 이 오브젝트의 동기화된 스킬과 무관(기존과 같음)
                    _weaponView.PlayRemoteSwing(facing, false);
                    Sfx.Play(SfxId.SwordSwing, transform.position);
                    break;
            }
        }

        // +X 방향을 중심으로 좌우 halfAngle만큼 벌어진 반지름 1짜리 부채꼴 스프라이트(피벗 = 원 중심).
        /// <summary>반각별 흰 부채꼴 스프라이트 캐시(공격 디버그·PlayerGuard 가드 부채꼴 공용 — 색은 렌더러가 입힌다). 키 = 반각 반올림.</summary>
        internal Sprite GetConeSprite(float halfAngle)
        {
            int key = Mathf.RoundToInt(halfAngle);
            if (!_coneSprites.TryGetValue(key, out Sprite sprite))
            {
                sprite = CreateConeSprite(halfAngle);
                _coneSprites[key] = sprite;
            }

            return sprite;
        }

        private static Sprite CreateConeSprite(float halfAngle)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center, dy = y - center;
                    bool inside = dx * dx + dy * dy <= center * center
                        && Mathf.Abs(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg) <= halfAngle;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            var controller = GetComponent<PlayerController>(); // 에디터 기즈모 — Awake 전(편집 모드)에도 동작하게 직접 찾는다
            Vector2 dir = Application.isPlaying && controller != null ? controller.FacingDirection.normalized : Vector2.down;
            Vector3 origin = transform.position;
            Gizmos.color = Color.red;
            Vector3 left = Quaternion.Euler(0f, 0f, attackHalfAngle) * dir;
            Vector3 right = Quaternion.Euler(0f, 0f, -attackHalfAngle) * dir;
            Gizmos.DrawLine(origin, origin + left * attackRadius);
            Gizmos.DrawLine(origin, origin + right * attackRadius);
            Gizmos.DrawWireSphere(origin, attackRadius);
        }

        internal int ComputeSkillDamage(float damageScale)
        {
            int baseDamage = attackDamage + _stats.AttackDamageBonusSum;
            return Mathf.Max(1, Mathf.RoundToInt(baseDamage * damageScale));
        }

        /// <summary>
        /// 대시 경로 판정 헬퍼(서버 전용) — 소 돌진(OxKnockbackSkill)과 같은 규칙: 플레이어 위치 원형 OverlapCircleAll(attackHitLayers),
        /// hitSet으로 대상별 1회, 몬스터는 대미지 후 살아있으면 속성 명중, 보스는 흡수 스킬 대미지(DamageSource.AbsorbSkill) + 속성 명중.
        /// knockbackDistance는 절대 거리(칸) — 몬스터별 knockbackDistance로 나눠 배율로 바꾼다(0이면 넉백 없음).
        /// 살아있는 적을 하나라도 맞혔으면 true(적중 회복은 호출부에서 대시 1회당 1번).
        /// 소 돌진은 넉백·갑옷 규칙이 달라 별도 함수(OxKnockbackSkill.ChargeHitOnce)로 둔다. 원점은 HitOrigin(원격 요청이면 요청 위치).
        /// </summary>
        private bool HitCircleOnce(float radius, System.Collections.Generic.HashSet<GameObject> hitSet, int damage, float knockbackDistance)
        {
            if (!isServer)
            {
                return false;
            }

            Vector2 origin = HitOrigin;
            bool dealtHit = false;
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(origin, radius, attackHitLayers))
            {
                if (hit.gameObject == gameObject || hit.isTrigger || !hitSet.Add(hit.gameObject))
                {
                    continue;
                }

                // 잡몹/보스 공통 처리(리팩터 B안 단계 1, HitResolver). 잡몹 넉백은 절대 거리(knockbackDistance > 0 && 몬스터 KnockbackDistance > 0).
                // 흡수 스킬 판정이므로 보스에게도 속성 적용.
                var info = new AbsorbRoguelike.Combat.HitInfo(damage, origin, netIdentity, DamageSource.AbsorbSkill);
                var options = new AbsorbRoguelike.Combat.HitOptions
                {
                    ElementApplications = 1,
                    ApplyElementToBoss = true,
                    RecordHit = true,
                    Knockback = AbsorbRoguelike.Combat.KnockbackMode.AbsoluteDistance,
                    KnockbackValue = knockbackDistance,
                };
                var result = AbsorbRoguelike.Combat.HitResolver.Resolve(hit.gameObject, info, options,
                    (t, isBoss) => ApplyElementOnHit(t, damage, isBoss, true), OnEnemyHitServer);
                // 체력이 실제로 깎였을 때만 — 개인 할당량 초과분은 제외(2026-09-28)
                dealtHit |= result.Damaged;
            }

            return dealtHit;
        }

        // ── 남은 12지신 5종 (GDD v2.33, 2026-09-29) ─────────────────────────────────────────────────────────

        /// <summary>
        /// 플레이어 명중 경로(DoConeHit·귀 채찍·소 돌진·대시 경로·바나나) 공통 후처리(서버 전용): 살아있는 적을 맞힐 때마다 부른다.
        /// ① 최근 타격 대상·시각 기록 — 개 동료(DogCompanion)가 파티 전원 기록 중 가장 최근 것을 쫓는다.
        /// ② 현재 스킬의 OnHitEnemy — 쥐 갉기: 취약 스택 +1(RatGnawSkill, 단계 5c). 대미지를 준 뒤에 쌓으므로 이번 타격은 배율 전 값.
        /// </summary>
        internal void OnEnemyHitServer(GameObject target)
        {
            if (!isServer || target == null)
            {
                return;
            }

            LastHitTarget = target;
            LastHitTime = Time.time;

            CurrentSkill?.OnHitEnemy(target);
        }

        /// <summary>
        /// 단일 대상 흡수 스킬 명중(서버 전용) — 바나나(BananaProjectile)·개 물기(DogCompanion)가 쓴다. 대미지 식은 ComputeSkillDamage
        /// (기본공격 × damageScale), 보스는 DamageSource.AbsorbSkill, 속성은 대상별 1회. knockbackDistance는 절대 거리(칸, 0이면 없음) —
        /// 잡몹만 밀고 보스는 밀지 않는다. recordHit이 true면 최근 타격 기록(OnEnemyHitServer)에 남긴다(개 물기는 false — 개가 제 물기를 쫓지 않게).
        /// 반환: 살아있는 적의 체력을 실제로 깎았으면 true(적중 회복 판단은 호출부).
        /// </summary>
        public bool ServerApplySkillHit(GameObject targetObject, float damageScale, Vector2 knockbackFrom, float knockbackDistance, bool recordHit)
        {
            if (!isServer || targetObject == null)
            {
                return false;
            }

            int damage = ComputeSkillDamage(damageScale);
            // 사전 체크(이 함수만의 규칙): 이미 죽은 잡몹·처치된 보스는 아무것도 하지 않는다. 잡몹/보스가 아니면 false.
            MonsterStub monster = targetObject.GetComponent<MonsterStub>();
            if (monster != null)
            {
                if (monster.IsDead)
                {
                    return false;
                }
            }
            else
            {
                BossStub boss = targetObject.GetComponent<BossStub>();
                if (boss == null || boss.IsDefeated)
                {
                    return false;
                }
            }

            // 잡몹/보스 공통 처리(리팩터 B안 단계 1, HitResolver). 잡몹 넉백은 절대 거리, 보스는 밀지 않는다. recordHit false(개 물기)면 기록 생략.
            var info = new AbsorbRoguelike.Combat.HitInfo(damage, knockbackFrom, netIdentity, DamageSource.AbsorbSkill);
            var options = new AbsorbRoguelike.Combat.HitOptions
            {
                ElementApplications = 1,
                ApplyElementToBoss = true,
                RecordHit = recordHit,
                Knockback = AbsorbRoguelike.Combat.KnockbackMode.AbsoluteDistance,
                KnockbackValue = knockbackDistance,
            };
            var result = AbsorbRoguelike.Combat.HitResolver.Resolve(targetObject, info, options,
                (t, isBoss) => ApplyElementOnHit(t, damage, isBoss, true), OnEnemyHitServer);
            return result.Damaged;
        }

        /// <summary>바나나가 적을 맞혔을 때(서버). 체력이 실제로 깎였으면 적중 회복(ICD)을 시도한다.</summary>
        public void ServerOnBananaHit(GameObject target, Vector2 knockbackFrom, float damageScale, float knockbackDistance)
        {
            if (ServerApplySkillHit(target, damageScale, knockbackFrom, knockbackDistance, true) && _health != null)
            {
                _health.OnDealtHit();
            }
        }

        /// <summary>원숭이 바나나 던지기 요청(소유 클라이언트·호스트 — MonkeyBananaSkill이 공격마다 부른다, 단계 5c). [Command]는 여기 그대로.</summary>
        internal void SendThrowBanana(Vector2 dir) => CmdThrowBanana(dir);

        /// <summary>원숭이 바나나 던지기(결정 ㉘-2 c): 본문은 현재 스킬의 ServerOnAttackCommand(MonkeyBananaSkill, 단계 5c — 원숭이가 아니면 무시,
        /// 5b 닭 CmdLayEgg와 같은 패턴). Command 이름·인자·위치(PlayerCombat)는 그대로.</summary>
        [Command]
        private void CmdThrowBanana(Vector2 dir)
        {
            if (_controller.SkillHost != null)
            {
                _controller.SkillHost.ServerOnAttackCommand(dir);
            }
        }

        /// <summary>원격 소유자 화면의 털 폭발 연출. 시그니처(인자 없음)는 그대로 두고 반경·색은 이 클라이언트의 SheepSkillConfig에서 읽는다(단계 5b —
        /// 옛 PlayerCombat 필드와 같은 Player.prefab 값). config가 비면 그리지 않는다(Host Awake 에러 로그로 드러남).</summary>
        [TargetRpc]
        private void TargetShowSheepBurst(NetworkConnectionToClient target)
        {
            SheepSkillConfig cfg = _controller.SkillHost != null ? _controller.SkillHost.SheepConfig : null;
            if (cfg == null)
            {
                return;
            }

            ShowAttackDebug(_controller.FacingDirection.normalized, cfg.sheepBurstRadius, 180f, 0.3f, cfg.sheepBurstColor);
        }
    }
}
