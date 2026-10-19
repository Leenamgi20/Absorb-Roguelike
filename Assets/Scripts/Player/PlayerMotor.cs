using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 플레이어 이동(리팩터 B안 단계 4a, PlayerController에서 분리): 입력 이동 속도·피격 둔화·피격 넉백.
    /// 자체 FixedUpdate는 두지 않는다 — 컴포넌트 간 FixedUpdate 순서가 보장되지 않으므로 PlayerController.FixedUpdate가
    /// 마우스 방향·무기 방향 갱신 뒤 FixedTick을 직접 부른다(기존 순서 그대로). 입력·가드·대시 상태는 PlayerController 읽기 전용 프로퍼티로 읽는다.
    /// 대시·소 돌진 코루틴의 속도 제어와 Rigidbody2D 초기화(보간·원격 Kinematic)는 PlayerController에 남아 있다.
    /// </summary>
    public class PlayerMotor : NetworkBehaviour
    {
        [Header("이동")]
        [SerializeField] private float moveSpeed = 4.5f;

        private PlayerController _controller;
        private PlayerStats _stats;
        private PlayerHealth _health;
        private Rigidbody2D _rigidbody;

        // 잡몹 명중 효과(2026-10-02, 부품 ⑥): 피격 둔화·피격 넉백. 이동이 소유 클라이언트 권위라 서버가 판정 후 TargetRpc로 넘겨
        // 소유 클라이언트(로컬 플레이어)에서만 값이 쓰인다. 둔화는 중첩 없이 강한 배율 유지 + 남은 시간 갱신.
        private float _hitSlowMultiplier = 1f;
        private float _hitSlowRemaining;
        private Vector2 _hitKnockbackVelocity;
        private float _hitKnockbackRemaining;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _stats = GetComponent<PlayerStats>();
            _health = GetComponent<PlayerHealth>();
            _rigidbody = GetComponent<Rigidbody2D>();

            // PlayerHealth가 서버 런타임(TryTakeDamage)에서만 내는 이벤트라 Awake 순서와 무관하다(구독자는 이 컴포넌트 하나).
            if (_health != null)
            {
                _health.ServerKnockbackRequested += ServerApplyKnockback;
            }
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.ServerKnockbackRequested -= ServerApplyKnockback;
            }
        }

        /// <summary>로컬 플레이어 전용 — PlayerController.FixedUpdate가 UpdateFacingFromMouse·무기 방향 다음에 부른다.</summary>
        public void FixedTick()
        {
            if (_hitSlowRemaining > 0f)
            {
                _hitSlowRemaining -= Time.fixedDeltaTime;
                if (_hitSlowRemaining <= 0f)
                {
                    _hitSlowMultiplier = 1f;
                }
            }

            if (_health != null && _health.IsDowned)
            {
                _rigidbody.linearVelocity = Vector2.zero; // 쓰러진 몸은 그 자리에 멈춘다(콜라이더도 꺼져 있다)
                _hitKnockbackRemaining = 0f;
                return;
            }

            if (_controller.IsDashing || _controller.IsOxCharging)
            {
                _hitKnockbackRemaining = 0f; // 대시가 피격 넉백을 끊는다(대시 중 넉백 무시)
                return; // 대시·소 돌진 중에는 코루틴이 속도를 직접 제어한다.
            }

            if (_hitKnockbackRemaining > 0f)
            {
                _hitKnockbackRemaining -= Time.fixedDeltaTime;
                _rigidbody.linearVelocity = _hitKnockbackVelocity; // 피격 넉백 동안은 입력 이동 대신 밀려난다
                return;
            }

            float speedMultiplier = (_controller.IsGuarding ? _controller.GuardMoveSpeedMultiplier : 1f) * _stats.PassiveMoveSpeedMultiplier * _hitSlowMultiplier;
            _rigidbody.linearVelocity = _controller.MoveInput * (moveSpeed * speedMultiplier);
        }

        /// <summary>
        /// 잡몹 명중 효과 — 피격 둔화(서버 전용 진입점, 2026-10-02). 이동이 소유 클라이언트 권위라 호스트 본인은 바로, 원격 소유자는
        /// TargetRpc로 적용한다. 이미 둔화 중이면 중첩하지 않고 더 강한(낮은) 배율을 유지하며 남은 시간은 긴 쪽으로 갱신한다.
        /// 호출부는 PlayerHealth.TryTakeDamage가 true(실제 피해)일 때만 부른다(MonsterBehaviorController.OnAttackHitPlayer).
        /// </summary>
        [Server]
        public void ServerApplySlow(float multiplier, float duration)
        {
            if (multiplier <= 0f || multiplier >= 1f || duration <= 0f)
            {
                return;
            }

            if (isLocalPlayer)
            {
                LocalApplyHitSlow(multiplier, duration);
            }
            else if (connectionToClient != null)
            {
                TargetApplyHitSlow(connectionToClient, multiplier, duration);
            }
        }

        /// <summary>
        /// 잡몹 명중 효과 — 피격 넉백(서버 전용 진입점, 2026-10-02). from 반대 방향으로 time초 동안 distance칸 밀린다.
        /// 방향은 소유 클라이언트가 자기 현재 위치 기준으로 계산한다. 대시 중이면 무시.
        /// 호출부: MonsterBehaviorController.OnAttackHitPlayer, PlayerHealth.ServerKnockbackRequested(이 컴포넌트가 구독).
        /// </summary>
        [Server]
        public void ServerApplyKnockback(Vector2 from, float distance, float time)
        {
            if (distance <= 0f || time <= 0f)
            {
                return;
            }

            if (isLocalPlayer)
            {
                LocalApplyHitKnockback(from, distance, time);
            }
            else if (connectionToClient != null)
            {
                TargetApplyHitKnockback(connectionToClient, from, distance, time);
            }
        }

        [TargetRpc]
        private void TargetApplyHitSlow(NetworkConnectionToClient target, float multiplier, float duration)
        {
            LocalApplyHitSlow(multiplier, duration);
        }

        [TargetRpc]
        private void TargetApplyHitKnockback(NetworkConnectionToClient target, Vector2 from, float distance, float time)
        {
            LocalApplyHitKnockback(from, distance, time);
        }

        private void LocalApplyHitSlow(float multiplier, float duration)
        {
            _hitSlowMultiplier = _hitSlowRemaining > 0f ? Mathf.Min(_hitSlowMultiplier, multiplier) : multiplier;
            _hitSlowRemaining = Mathf.Max(_hitSlowRemaining, duration);
        }

        private void LocalApplyHitKnockback(Vector2 from, float distance, float time)
        {
            if (_controller.IsDashing || _controller.IsOxCharging)
            {
                return;
            }

            Vector2 away = (Vector2)transform.position - from;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : -_controller.FacingDirection.normalized;
            _hitKnockbackVelocity = away * (distance / time);
            _hitKnockbackRemaining = time;
        }
    }
}
