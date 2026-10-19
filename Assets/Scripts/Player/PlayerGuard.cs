using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 대검 가드(리팩터 B안 단계 4b, PlayerController에서 분리): 가드 상태·서버 보고(CmdSetGuard)·관찰자용 syncedGuarding,
    /// 방향성 막기(IDamageFilter.BlocksFromDirection)와 막았을 때의 서버 반응 골격(IDamageFilter.OnBlockedFromDirection → 현재 스킬 OnGuardBlocked),
    /// 가드 시각(스프라이트 톤·가드 부채꼴 디버그).
    /// 자체 Update류는 두지 않는다 — PlayerController가 기존 자리에서 ServerTick(Update 서버 블록)·TryResendGuardFacing(Update 로컬 분기)·
    /// LateTick(LateUpdate)을 부른다. 입력 핸들러(OnGuardStarted/OnGuardCanceled)도 PlayerController가 _controls에 구독한다.
    /// 흡수 스킬(단계 5b): 돼지 반격(PigCounterSkill)·양털(SheepWoolSkill)은 스킬 객체 훅으로 묻는다 — 가드 각 ModifyGuardBlockAngle,
    /// 막은 순간 OnGuardBlocked(서버). 원격 소유자용 [TargetRpc] TargetShowPigCounter는 NetworkBehaviour인 여기 남기고 internal 래퍼로 연다.
    /// </summary>
    public class PlayerGuard : NetworkBehaviour, AbsorbRoguelike.Combat.IDamageFilter
    {
        [Header("가드 (대검)")]
        [SerializeField] private Color guardColor = new Color(0.4f, 0.6f, 1f); // 임시 시각 표시: 가드 중엔 파란 톤
        [SerializeField] private float guardBlockAngle = 120f; // [PLAYTEST] 방향성 가드 무적 부채꼴 폭(정면 기준 좌우 절반씩)
        [Tooltip("가드 중 막는 방향(부채꼴)을 표시하고, 막아낸 순간 번쩍인다(튜닝용 디버그).")]
        [SerializeField] private bool showGuardDebug = true;
        [SerializeField] private float guardDebugRadius = 1f;
        [SerializeField] private Color guardDebugColor = new Color(0.3f, 0.6f, 1f, 0.25f);
        [SerializeField] private Color guardBlockFlashColor = new Color(0.7f, 0.9f, 1f, 0.7f);
        [SerializeField] private float guardBlockFlashDuration = 0.15f;

        // 돼지 반격 수치 5개(pigGuardBlockAngle·pigCounterDamageScale·pigCounterCooldown·pigCounterCooldownFlurry·pigCounterRadiusMargin)는
        // 단계 5b에서 Assets/Data/AbsorbSkills/PigSkillConfig.asset(이름·값 그대로)으로 옮겼다 — AbsorbSkillHost가 참조.

        // 관찰자용 가드 시각 전파(2026-09-30 2인 검증 1차 후속, 연출 전용 — 판정은 서버의 _isGuarding). 서버가 ServerTick에서 쓴다.
        [SyncVar(hook = nameof(OnSyncedGuardingChanged))] private bool syncedGuarding;

        private bool _isGuarding;
        private float _nextGuardFacingSendTime;
        private const float GuardFacingSendInterval = 0.1f; // 가드 중 facing 재전송 간격(초) — 원격 플레이어 방향성 가드용

        private SpriteRenderer _guardDebugRenderer;
        private float _guardBlockFlashRemaining;
        private float _guardDebugHalfAngle = -1f;

        private PlayerController _controller;
        private PlayerCombat _combat;
        private SpriteRenderer _spriteRenderer;
        private Color _defaultColor;

        /// <summary>기능적 가드 상태(이동속도 감소 등). 대시 중에는 꺼진다. 서버의 IsAttackBlockedByGuard가 원격 플레이어의 이 값과 FacingDirection을 읽는다.</summary>
        public bool IsGuarding => _isGuarding;

        /// <summary>화면에 그릴 가드 여부: 로컬 플레이어는 자기 상태, 그 외는 동기화된 값.</summary>
        private bool VisualGuarding => isLocalPlayer ? IsGuarding : syncedGuarding;

        /// <summary>현재 적용되는 가드 무적 부채꼴 폭 — guardBlockAngle에 현재 스킬의 ModifyGuardBlockAngle(돼지 pigGuardBlockAngle, [PLAYTEST] 확장치)을 덮는다.
        /// 서버 판정(IsAttackBlockedByGuard)과 가드 부채꼴 표시가 같은 값을 쓴다.</summary>
        private float CurrentGuardBlockAngle
        {
            get
            {
                float angle = guardBlockAngle;
                _controller.SkillHost.Current?.ModifyGuardBlockAngle(ref angle);
                return angle;
            }
        }

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _combat = GetComponent<PlayerCombat>();
        }

        /// <summary>몸 스프라이트·기본색 캐시. PlayerController.Awake의 기존 자리(옛 _defaultColor 캐시)에서 부른다(컴포넌트 Awake 순서 비의존).</summary>
        internal void CacheRenderer()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null)
            {
                _defaultColor = _spriteRenderer.color;
            }
        }

        // IDamageFilter(단계 4b — PlayerController에서 이동): 방향 막기만 담당. 전부 무시(대시 무적)는 PlayerDash. 판정 순서는 PlayerHealth가 지킨다.
        bool AbsorbRoguelike.Combat.IDamageFilter.IgnoresAllDamage => false;

        bool AbsorbRoguelike.Combat.IDamageFilter.BlocksFromDirection(Vector2 attackerPosition) => IsAttackBlockedByGuard(attackerPosition);

        void AbsorbRoguelike.Combat.IDamageFilter.OnBlockedFromDirection(Vector2 attackerPosition) => OnGuardBlockedServer(attackerPosition); // 돼지 반격·양털(스킬 객체 OnGuardBlocked, 쿨타임은 PigCounterSkill)

        /// <summary>
        /// 가드 상태 쓰기(옛 IsGuarding setter). 소유 클라이언트가 바꾸면 facing과 함께 서버에 보고한다(CmdSetGuard).
        /// 호스트 본인은 로컬 값이 곧 서버 값이라 Command를 보내지 않는다.
        /// </summary>
        internal void SetGuarding(bool value)
        {
            if (_isGuarding == value)
            {
                return;
            }

            _isGuarding = value;
            if (isLocalPlayer && !isServer)
            {
                CmdSetGuard(value, _controller.FacingDirection);
                _nextGuardFacingSendTime = Time.time + GuardFacingSendInterval;
            }
        }

        /// <summary>가드 기능·시각을 함께 끈다(공격·소 돌진·대시 시작 — 옛 'IsGuarding = false; SetGuardVisual(false);').</summary>
        internal void ForceOff()
        {
            SetGuarding(false);
            SetGuardVisual(false);
        }

        /// <summary>가드 버튼 눌림 상태로 기능·시각을 맞춘다(공격 종료·대시 종료·용 브레스 이동 끊기 — 옛 'IsGuarding = Guard.IsPressed(); SetGuardVisual(IsGuarding);').</summary>
        internal void RestoreFromInput()
        {
            SetGuarding(_controller.Controls.Gameplay.Guard.IsPressed());
            SetGuardVisual(IsGuarding);
        }

        /// <summary>서버: PlayerController.Update 서버 블록의 기존 자리(facing 동기화 앞)에서 매 프레임 — 가드 시각 전파(값이 바뀔 때만 dirty).</summary>
        internal void ServerTick()
        {
            syncedGuarding = _isGuarding;
        }

        /// <summary>
        /// 원격 클라이언트: 가드 중에는 facing을 일정 간격으로 서버에 다시 보낸다(방향성 가드 판정용). 보냈으면 true —
        /// PlayerController.Update가 같은 if/else 체인에서 _lastSentFacing을 갱신하고, false면 기존 else(비가드 facing 전송)로 넘어간다.
        /// 조건이 거짓이면 부작용이 없어 옛 체인과 결과가 같다.
        /// </summary>
        internal bool TryResendGuardFacing()
        {
            if (!isServer && _isGuarding && Time.time >= _nextGuardFacingSendTime)
            {
                _nextGuardFacingSendTime = Time.time + GuardFacingSendInterval;
                CmdSetGuard(true, _controller.FacingDirection);
                return true;
            }

            return false;
        }

        /// <summary>PlayerController.LateUpdate의 기존 자리(원격·로컬 모두 부채꼴 다음)에서 — 가드 부채꼴 디버그.</summary>
        internal void LateTick()
        {
            UpdateGuardDebug();
        }

        /// <summary>늦게 접속했거나 이미 가드 중인 다른 플레이어도 처음부터 맞게 보이게(훅은 값이 바뀔 때만 온다). PlayerController.OnStartClient 기존 자리에서.</summary>
        internal void ApplyInitialRemoteGuardVisual()
        {
            if (!isLocalPlayer && syncedGuarding)
            {
                SetGuardVisual(true);
            }
        }

        /// <summary>소유 클라이언트 → 서버: 가드 상태와 facing(가드 중에는 GuardFacingSendInterval마다 재전송). 서버 사본의 값만 바꾼다 —
        /// 서버의 IsAttackBlockedByGuard·돼지 반격 방향 fallback이 이 값을 읽는다. 호스트는 호출하지 않는다(로컬 값 = 서버 값).</summary>
        [Command]
        private void CmdSetGuard(bool guarding, Vector2 facing)
        {
            _isGuarding = guarding;
            if (facing.sqrMagnitude > 0.0001f)
            {
                _controller.ServerSetFacingFromGuard(facing); // _facingDirection = 정규화, syncedFacing = 같은 값(관찰자 화면 무기·가드 부채꼴 방향)
            }
        }

        // 가드 시각(스프라이트 파란 톤)을 다른 플레이어 오브젝트에 반영한다. 로컬 플레이어는 입력 경로가 직접 칠한다.
        private void OnSyncedGuardingChanged(bool oldValue, bool newValue)
        {
            if (!isLocalPlayer)
            {
                SetGuardVisual(newValue);
            }
        }

        internal void OnGuardStarted(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            // NOTE(디버깅용 임시 원복, 2026-09-16): 원래는 연출을 위해 "버튼을 누르고 있으면 대시/공격 중에도
            // 방패 시각을 계속 유지"하는 방식이었으나(SetGuardVisual(true)를 여기서 무조건 호출),
            // 그 때문에 공격 중에 가드 버튼을 누르면 기능(IsGuarding)은 꺼진 채로 시각만 켜지는 것처럼 보여
            // 상태 디버깅이 어려웠다. 문제 원인 파악이 끝날 때까지 시각을 기능 상태와 1:1로 되돌린다.
            // SetGuardVisual(true);

            if (_controller.IsDashing || _controller.IsOxCharging)
            {
                return; // 대시·소 돌진(몸이 움직이는 기술)은 끊지 않는다 — 끝나는 시점에 버튼 눌림 상태를 다시 확인해 가드로 복귀
            }

            if (_combat.HasAttackLoop)
            {
                _combat.CancelAttackForGuard(); // 가드 우선(결정 ⑲A): 스윙·재사용 대기 중이어도 즉시 끊는다
            }

            SetGuarding(true);
            SetGuardVisual(true);
            // TODO: 대검 가드 판정(피해 감소) 연결
        }

        internal void OnGuardCanceled(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            SetGuarding(false);
            SetGuardVisual(false);
        }

        private void SetGuardVisual(bool active)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = active ? guardColor : _defaultColor;
            }
        }

        /// <summary>
        /// 대검 가드의 방향성 100% 무적 판정(GDD "대검 가드의 구체 동작(확정, 2026-09-22)").
        /// 가드 중이 아니면 무조건 false. 가드 중이면 "공격자 위치 → 플레이어 위치" 방향과 플레이어가
        /// 바라보는 방향(FacingDirection) 사이의 각도가 guardBlockAngle의 절반 이하일 때만 막아낸다
        /// (측면/후방 공격은 그대로 통과).
        /// </summary>
        public bool IsAttackBlockedByGuard(Vector2 attackerPosition)
        {
            if (!IsGuarding)
            {
                return false;
            }

            Vector2 playerToAttacker = attackerPosition - (Vector2)transform.position;
            float angleFromFacing = Vector2.Angle(_controller.FacingDirection, playerToAttacker);
            bool blocked = angleFromFacing <= CurrentGuardBlockAngle * 0.5f;
            if (blocked)
            {
                _guardBlockFlashRemaining = guardBlockFlashDuration;
                if (isClient)
                {
                    AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.Guard, transform.position); // 호스트 화면(번쩍임과 같은 자리)
                }

                if (isServer && !isLocalPlayer && connectionToClient != null)
                {
                    TargetGuardBlockFlash(connectionToClient); // 원격 소유자 화면에도 막아낸 번쩍임(호스트 본인은 위 줄로 이미 표시)
                }
            }

            return blocked;
        }

        [TargetRpc]
        private void TargetGuardBlockFlash(NetworkConnectionToClient target)
        {
            _guardBlockFlashRemaining = guardBlockFlashDuration;
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.Guard, transform.position); // 원격 소유자 화면
        }

        /// <summary>
        /// 가드로 공격을 막아낸 순간 PlayerHealth가 호출한다(서버 전용, 호출 시점·조건은 PlayerHealth 그대로). 반응은 현재 스킬 객체의
        /// OnGuardBlocked(단계 5b) — 돼지 반격(PigCounterSkill: 쿨타임·플레이어→공격자 방향·반경 여유·연출·DoConeHit)·양털(SheepWoolSkill: 털 +1, 다 차면 털 폭발 + 보호막).
        /// 나머지 스킬·None은 기존처럼 아무것도 안 한다. 원격 플레이어도 CmdSetGuard로 가드·facing이 서버에 올라와 여기까지 온다(2인 검증 준비).
        /// </summary>
        public void OnGuardBlockedServer(Vector2 attackerPosition)
        {
            if (!isServer)
            {
                return;
            }

            _controller.SkillHost.Current?.OnGuardBlocked(attackerPosition);
        }

        /// <summary>서버: 돼지 반격 부채꼴을 원격 소유자 화면에 그린다(PigCounterSkill이 '호스트 본인이 아니고 connectionToClient가 있을 때' 부른다).
        /// [TargetRpc]는 NetworkBehaviour가 필요해 여기 남겼다(단계 5b).</summary>
        internal void ServerShowPigCounterToOwner(Vector2 direction, float radius, float halfAngle)
        {
            TargetShowPigCounter(connectionToClient, direction, radius, halfAngle);
        }

        [TargetRpc]
        private void TargetShowPigCounter(NetworkConnectionToClient target, Vector2 direction, float radius, float halfAngle)
        {
            _combat.ShowAttackDebug(direction, radius, halfAngle);
        }

        private void UpdateGuardDebug()
        {
            if (!showGuardDebug)
            {
                if (_guardDebugRenderer != null)
                {
                    _guardDebugRenderer.enabled = false;
                }

                return;
            }

            if (_guardDebugRenderer == null)
            {
                var go = new GameObject("GuardDebugCone");
                go.transform.SetParent(transform, false);
                _guardDebugRenderer = go.AddComponent<SpriteRenderer>();
                _guardDebugRenderer.sortingOrder = 9;
            }

            // 돼지 흡수/해제로 적용 각도가 바뀌면 부채꼴 스프라이트를 다시 만든다.
            float halfAngle = CurrentGuardBlockAngle * 0.5f;
            if (!Mathf.Approximately(halfAngle, _guardDebugHalfAngle))
            {
                _guardDebugHalfAngle = halfAngle;
                // 흰색 부채꼴이라 공격 디버그와 같은 캐시(PlayerCombat)를 공유해도 된다(색은 렌더러가 입힘)
                _guardDebugRenderer.sprite = _combat.GetConeSprite(halfAngle);
            }

            if (_guardBlockFlashRemaining > 0f)
            {
                _guardBlockFlashRemaining -= Time.deltaTime;
            }

            // 로컬은 자기 상태, 다른 플레이어 오브젝트는 동기화된 가드·facing(2인 검증 1차 후속).
            bool guarding = VisualGuarding;
            _guardDebugRenderer.enabled = guarding;
            if (guarding)
            {
                Vector2 facing = _controller.VisualFacing;
                float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
                _guardDebugRenderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                _guardDebugRenderer.transform.localScale = Vector3.one * guardDebugRadius;
                _guardDebugRenderer.color = _guardBlockFlashRemaining > 0f ? guardBlockFlashColor : guardDebugColor;
            }
        }
    }
}
