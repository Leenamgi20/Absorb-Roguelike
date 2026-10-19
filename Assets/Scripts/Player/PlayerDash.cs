using System.Collections;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 대시(리팩터 B안 단계 4b, PlayerController에서 분리): 대시 입력·쿨타임·대시 무적(IDamageFilter.IgnoresAllDamage)·
    /// 적 통과(모든 대시, 결정 ㉓A)·대시 먼지·알 요청([Command] CmdLayEgg). 대시 경로 판정은 PlayerCombat.RequestHit(DashPath)로 요청한다.
    /// 자체 Update류는 두지 않는다 — PlayerController.Update 로컬 분기 끝에서 (SkillHost.TickLocalDash) → (Combat.TryResumeDragonBreath) → TickCooldown
    /// 순서로 부른다. 흡수 스킬(단계 5b): 말·닭은 스킬 객체(HorseDoubleDashSkill·RoosterEggSkill)의 훅으로 묻는다 — 파라미터 ModifyDash,
    /// 대시 시작 OnDashStart, 대시 끝 NotifyDashEnd(OnDashEnd), 알 CmdLayEgg 본문 ServerOnDashCommand. 말·닭 공용 dashPathHitRadius는
    /// PlayerCombat(서버 판정·연출)과 DashRoutine이 스킬과 무관하게 읽어서 여기 남겼다.
    /// </summary>
    public class PlayerDash : NetworkBehaviour, AbsorbRoguelike.Combat.IDamageFilter
    {
        [Header("대시")]
        [SerializeField] private float dashSpeed = 12f;
        [SerializeField] private float dashDuration = 0.2f;
        [Tooltip("대시 시작 먼지(2026-10-08 사용자 결정: 꼬리와 같은 dash_dust_trail을 1배로 — 큰 kick 그림은 튀어서 안 씀). 비어 있으면 없음.")]
        [SerializeField] private Sprite[] dashKickDustFrames;
        [Tooltip("대시 중 꼬리 먼지(dash_dust_trail, 32×32). 비어 있으면 없음.")]
        [SerializeField] private Sprite[] dashDustFrames;
        [SerializeField] private float dashCooldown = 0.8f;

        [Header("흡수 스킬 — 말·닭 대시 (GDD 메인 슬롯 표, 2026-09-28 추가, 수치 전부 [PLAYTEST])")]
        [Tooltip("말·닭 대시 경로 판정 반지름(칸). [PLAYTEST]")]
        [SerializeField] private float dashPathHitRadius = 0.8f;
        // 말 수치(horseDashCharges·horseSecondDashWindow·horseDashDamageScale)·닭 수치(roosterDashDamageScale·roosterEggPrefab·maxEggsPerPlayer)는
        // 단계 5b에서 Assets/Data/AbsorbSkills/HorseSkillConfig·RoosterSkillConfig.asset(이름·값 그대로)으로 옮겼다 — AbsorbSkillHost가 참조.
        [Tooltip("대시 동안 몸이 통과하는(물리 충돌을 끄는) 레이어 — 몬스터/보스(Monster=8). 벽은 그대로 막힌다. 2026-09-29 결정 ㉓A로 모든 대시에 적용(이전엔 말 전용이라 필드 이름이 horse*).")]
        [SerializeField] private LayerMask horsePassThroughLayers = 1 << 8;

        /// <summary>대시 1회의 파라미터. 기본값(스킬 없음/기존 3종)은 dashSpeed·dashDuration·dashCooldown 그대로다.</summary>
        internal struct DashParams
        {
            public float Speed;
            public float MoveDuration;
            public float InvulnDuration; // MoveDuration보다 길면 이동이 끝난 뒤에도 무적만 이어진다
            public float CooldownScale;
            public float PathDamageScale; // 0보다 크면 이동 중 경로 판정(말/닭)
            public float PathKnockbackDistance;
            public bool DeferCooldown; // 말: 남은 연속 대시가 있으면 쿨다운 대신 이어쓰기 창을 연다
            public bool PassThroughEnemies; // 이동 중 몬스터/보스와 물리 충돌 없음 — 모든 대시 기본(결정 ㉓A, 보스·벽 사이 끼임 방지)
        }

        private bool _isInvulnerable;
        private float _dashCooldownRemaining;
        private bool _horsePassThroughActive;
        private LayerMask _excludeLayersBeforePassThrough;
        private int _dashSerial; // 연장 무적 도중 새 대시가 시작되면 이전 코루틴이 무적을 끄지 않게

        private PlayerController _controller;
        private PlayerCombat _combat;
        private PlayerGuard _guard;
        private Rigidbody2D _rigidbody;

        public bool IsDashing { get; private set; }

        /// <summary>대시 무적. 소유 클라이언트가 바꾸면 서버에도 보고한다(CmdSetInvulnerable, 2인 검증 준비) — 서버의 PlayerHealth가
        /// 원격 플레이어의 이 값을 읽는다(IDamageFilter.IgnoresAllDamage). 호스트 본인은 로컬 값이 곧 서버 값이라 Command를 보내지 않는다.</summary>
        public bool IsInvulnerable
        {
            get => _isInvulnerable;
            private set
            {
                if (_isInvulnerable == value)
                {
                    return;
                }

                _isInvulnerable = value;
                if (isLocalPlayer && !isServer)
                {
                    CmdSetInvulnerable(value);
                }
            }
        }

        /// <summary>남은 대시 쿨타임. 쓰기는 말 스킬(HorseDoubleDashSkill.TickLocalDash — 이어쓰기 창이 끝나거나 말이 벗겨지면 쿨다운 시작, 단계 5b).</summary>
        public float DashCooldownRemaining
        {
            get => _dashCooldownRemaining;
            internal set => _dashCooldownRemaining = value;
        }

        /// <summary>대시 쿨타임(초) 설정값. 말 스킬이 쿨다운을 시작할 때 읽는다(단계 5b).</summary>
        internal float DashCooldown => dashCooldown;

        /// <summary>말 흡수 시 쿨다운 전까지 남은 대시 횟수(HUD 표시용, 로컬 플레이어 기준). 값은 말 스킬 객체(단계 5b) —
        /// config가 비어 말 객체가 없으면 옛 초기값(Max(1, 기본 2))과 같은 값.</summary>
        public int HorseDashesLeft
        {
            get
            {
                HorseDoubleDashSkill horse = _controller.SkillHost != null ? _controller.SkillHost.Get<HorseDoubleDashSkill>() : null;
                return horse != null ? horse.DashesLeft : Mathf.Max(1, HorseSkillConfig.DefaultDashCharges);
            }
        }

        /// <summary>대시 이동 시간(초). PlayerCombat의 판정 요청 검증(DashPath 최소 간격·묶음 창)과 연출 시간이 읽는다.</summary>
        public float DashDuration => dashDuration;

        /// <summary>대시 경로 판정 반지름(칸). PlayerCombat.ServerPerformHit(DashPath)·연출 전파가 읽는다.</summary>
        public float DashPathHitRadius => dashPathHitRadius;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _combat = GetComponent<PlayerCombat>();
            _guard = GetComponent<PlayerGuard>();
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        // IDamageFilter(단계 4b — PlayerController에서 이동): 전부 무시(대시 무적)만 담당. 방향 막기는 PlayerGuard.
        bool AbsorbRoguelike.Combat.IDamageFilter.IgnoresAllDamage => IsInvulnerable;

        bool AbsorbRoguelike.Combat.IDamageFilter.BlocksFromDirection(Vector2 attackerPosition) => false;

        void AbsorbRoguelike.Combat.IDamageFilter.OnBlockedFromDirection(Vector2 attackerPosition)
        {
        }

        /// <summary>소유 클라이언트 → 서버: 대시 무적 켜짐/꺼짐. 서버의 PlayerHealth.TakeDamage가 읽는다. 지연만큼 늦게 켜지고 늦게 꺼진다(지연 보상 없음).</summary>
        [Command]
        private void CmdSetInvulnerable(bool invulnerable)
        {
            _isInvulnerable = invulnerable;
        }

        internal void OnDash(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            TryStartDash(GetDashParams());
        }

        /// <summary>로컬 전용 — PlayerController.Update 로컬 분기 끝(TryResumeDragonBreath 다음)에서: 대시 쿨타임 감소.</summary>
        internal void TickCooldown(float deltaTime)
        {
            if (_dashCooldownRemaining > 0f)
            {
                _dashCooldownRemaining -= deltaTime;
            }
        }

        private void TryStartDash(DashParams p)
        {
            if (IsDashing || _dashCooldownRemaining > 0f)
            {
                return; // 대시 중 재입력은 무시(말 두 번째 대시도 첫 대시가 끝난 뒤에만 받는다)
            }

            _controller.SkillHost.Current?.OnDashStart(ref p); // 말 연속 대시 횟수·쿨다운 미루기(단계 5b — 옛 말 분기 자리)

            if (_combat.HasAttackLoop)
            {
                // 공격 도중 대시가 끼어들 때 기존 공격 루프를 확실히 끊는다 (안 그러면 대시 종료 후 새 공격 루프와 겹쳐 돈다).
                // 옛 코드(StopCoroutine → null → _isAttacking false → 무기 색 복구)와 CancelAttackForGuard 본문이 같아 그것을 부른다.
                _combat.CancelAttackForGuard();
            }

            // 닭 알: 대시 시작 위치에 낳는다. 원격 클라이언트도 이제 동기화된 흡수 스킬(OnSyncedSkillChanged)을 알지만
            // 특성은 서버 권위라, 대시마다 요청만 보내고 닭인지는 서버가 자기 값으로 판정한다(호스트/원격 공통).
            if (isLocalPlayer)
            {
                CmdLayEgg(transform.position);
            }

            StartCoroutine(DashRoutine(p));
        }

        /// <summary>대시마다 오는 알 요청(서버). 본문은 서버의 현재 스킬 객체가 맡는다(단계 5b — 닭이면 RoosterEggSkill.ServerOnDashCommand:
        /// 프리팹·사망 검사, 플레이어별 최대 maxEggsPerPlayer개, 넘으면 가장 오래된 알 제거, 연결 소유 없이 스폰). 닭이 아니면 아무것도 안 한다.
        /// [Command]라 NetworkBehaviour인 여기 남겼다(Host는 MonoBehaviour).</summary>
        [Command]
        private void CmdLayEgg(Vector2 position)
        {
            _controller.SkillHost.Current?.ServerOnDashCommand(position);
        }

        /// <summary>
        /// 흡수 스킬별 대시 파라미터 — 기본값을 만든 뒤 현재 스킬 객체의 ModifyDash(말·닭 경로 대미지, 단계 5b)를 덮는다. 스킬 없음/나머지는 기존 값(dashSpeed·dashDuration·dashCooldown, 무적 = 대시 이동 시간
        /// dashDuration) 그대로다. NOTE: GDD는 기본 대시 무적을 0.3초로 적지만 현재 코드는 dashDuration(0.2초)이라
        /// 기존값을 유지했다(TODO 한계에 기록). 지금은 무적을 이동 시간 뒤로 연장하는 스킬이 없다(옛 토끼 연장 무적은 결정 ㉑A로 제거 —
        /// InvulnDuration/extraInvuln 일반 로직은 이후 스킬용으로 남겨둔다). 서버의 PlayerCombat.ServerPerformHit(DashPath)도 이 값으로 다시 계산한다.
        /// </summary>
        internal DashParams GetDashParams()
        {
            var p = new DashParams
            {
                Speed = dashSpeed,
                MoveDuration = dashDuration,
                InvulnDuration = dashDuration,
                CooldownScale = 1f,
                PassThroughEnemies = true, // 결정 ㉓A(2026-09-29): 모든 대시가 적을 통과 — 평소 몸 충돌은 유지
            };

            // 말(HorseDoubleDashSkill)·닭(RoosterEggSkill): 경로 대미지 배율·넉백 0(단계 5b). 토끼는 2026-09-28 개편(결정 ㉑A)으로 대시 특화가 빠지고 기본 대시를 쓴다.
            _controller.SkillHost.Current?.ModifyDash(ref p);

            return p;
        }

        // 대시 먼지(표시 전용): 대시한 화면은 바로 그리고, 다른 화면에는 서버를 거쳐 알린다.
        private void PlayDashDust(Vector2 direction, float duration)
        {
            DashDustFx.Play(transform, direction, duration, dashKickDustFrames, dashDustFrames);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.Dash, transform.position); // 대시한 화면(다른 화면은 RpcDashDust)
            if (isLocalPlayer)
            {
                CmdDashDust(direction, duration);
            }
        }

        [Command]
        private void CmdDashDust(Vector2 direction, float duration)
        {
            RpcDashDust(direction, Mathf.Clamp(duration, 0f, 1f));
        }

        [ClientRpc(includeOwner = false)]
        private void RpcDashDust(Vector2 direction, float duration)
        {
            DashDustFx.Play(transform, direction, duration, dashKickDustFrames, dashDustFrames);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.Dash, transform.position);
        }

        private IEnumerator DashRoutine(DashParams p)
        {
            int serial = ++_dashSerial;
            IsDashing = true;
            // 대시로 굴러나가는 동작이므로 가드 자세는 풀린다 (버튼을 떼지 않아도 되며, 떼지 않고 있으면 대시 종료 후 다시 가드 상태로 판정됨).
            // (디버깅용 임시 원복) 시각도 기능 상태와 같이 즉시 끈다.
            _guard.ForceOff();
            IsInvulnerable = true; // 레이턴시 허용 설계 원칙(GDD)에 따라 무적시간은 관대하게 튜닝 예정
            if (!p.DeferCooldown)
            {
                _dashCooldownRemaining = dashCooldown * p.CooldownScale;
            }

            if (p.PassThroughEnemies)
            {
                SetEnemyPassThrough(true);
            }

            Vector2 moveInput = _controller.MoveInput;
            Vector2 dashDirection = moveInput.sqrMagnitude > 0.01f ? moveInput.normalized : _controller.FacingDirection;
            _rigidbody.linearVelocity = dashDirection * p.Speed;
            PlayDashDust(dashDirection, p.MoveDuration);

            if (p.PathDamageScale > 0f)
            {
                // 말/닭: 대시 이동 중 매 프레임 경로 판정(대시 1회당 대상별 1회). 말은 몸이 통과하므로 지나간 적이 전부 맞는다.
                if (isLocalPlayer)
                {
                    _combat.ShowAttackDebug(dashDirection, dashPathHitRadius, 180f, p.MoveDuration);
                }

                // 판정은 프레임마다 DashPath 요청으로 서버가 한다(호스트 직접·원격 CmdPerformHit). 대시 1회 = 묶음 1개(말 연속 대시는 각각 별개) —
                // 서버 HitSession이 대상별 1회(hitSet)·적중 회복 1회를 지키고, 배율·넉백은 서버의 GetDashParams로 다시 계산한다.
                int dashStep = _combat.NextHitStep();
                float elapsed = 0f;
                while (elapsed < p.MoveDuration)
                {
                    _combat.RequestHit(PlayerCombat.HitKind.DashPath, dashDirection, dashStep);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(p.MoveDuration);
            }

            IsDashing = false;
            SetEnemyPassThrough(false);
            _controller.SkillHost.NotifyDashEnd(p); // 말: DeferCooldown이면 이어쓰기 창 열기(HorseDoubleDashSkill.OnDashEnd, 단계 5b — 장착 여부 무관)

            float extraInvuln = p.InvulnDuration - p.MoveDuration;
            if (extraInvuln <= 0f)
            {
                IsInvulnerable = false;
            }

            PlayerControls controls = _controller.Controls;
            if (controls.Gameplay.Attack.IsPressed() && !controls.Gameplay.Guard.IsPressed() && !_controller.IsAttacking && !_combat.IsAttackInputBlockedByUI() && !_combat.IsDragonMoveLocked)
            {
                _combat.StartAttackLoop(); // 대시 중에도 공격 버튼이 눌려있었다면 대시가 끝나자마자 바로 공격 이어감
            }
            else
            {
                _guard.RestoreFromInput(); // 가드 버튼을 계속 누르고 있었다면 대시 종료 후 자동 복귀 — (디버깅용 임시 원복) 시각도 같이 복귀
            }

            if (extraInvuln > 0f)
            {
                yield return new WaitForSeconds(extraInvuln); // 연장 무적 스킬용: 이동은 끝났지만 무적만 이어진다(현재 쓰는 스킬 없음)
                if (serial == _dashSerial)
                {
                    IsInvulnerable = false; // 연장 무적 도중 새 대시가 시작됐으면 그 대시가 무적을 관리한다
                }
            }
        }

        /// <summary>대시 동안 몬스터/보스 레이어와 물리 충돌을 끈다(벽은 유지, 결정 ㉓A로 모든 대시). 이동 권한이 있는 로컬 인스턴스에서만.
        /// 대시 종료 때 끄고, 코루틴이 중간에 끊기는 경우(비활성화·파괴)는 OnDisable에서 복구한다.</summary>
        private void SetEnemyPassThrough(bool on)
        {
            if (!isLocalPlayer || _rigidbody == null || on == _horsePassThroughActive)
            {
                return;
            }

            if (on)
            {
                _excludeLayersBeforePassThrough = _rigidbody.excludeLayers;
                _rigidbody.excludeLayers = _excludeLayersBeforePassThrough | horsePassThroughLayers;
            }
            else
            {
                _rigidbody.excludeLayers = _excludeLayersBeforePassThrough;
            }

            _horsePassThroughActive = on;
        }

        private void OnDisable()
        {
            if (_horsePassThroughActive && _rigidbody != null)
            {
                _rigidbody.excludeLayers = _excludeLayersBeforePassThrough;
                _horsePassThroughActive = false;
            }
        }
    }
}
