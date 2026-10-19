using AbsorbRoguelike.Combat;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 플레이어 체력 시스템(결정 대기 체크리스트 A, 2026-09-17 확정 수치). 기본 최대체력 100, 피격 시
    /// 0.5초 무적시간(대시 무적과는 별개 — 여러 몬스터에게 한 프레임에 겹쳐 맞아 순삭당하는 것을 방지).
    /// 회복은 GDD '회복 시스템 설계 원칙'의 ICD 고정 틱 방식(2026-09-23 전환): 내 공격이 적에게 맞으면
    /// 쿨다운마다 한 번 고정량 회복. 대미지량에 비례하지 않는다. 수치는 [PLAYTEST] 임시값이고, 스테이지
    /// 진행도에 따른 회복량 성장은 스테이지 시스템이 생기면 붙인다.
    /// 대미지 적용/회복 계산은 서버(호스트) 권위로만 처리한다(ADR-003).
    /// </summary>
    public class PlayerHealth : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private float hitInvulnerabilityDuration = 0.5f;
        [SerializeField] private int onHitHealAmount = 3; // [PLAYTEST] 타격 시 ICD 1회 회복량(고정) — 닭 알 회복이 생겨 5→3(2026-09-28)
        [SerializeField] private float onHitHealCooldown = 5f; // [PLAYTEST] ICD 주기(초)

        [SyncVar] private int currentHealth;
        [SyncVar] private float healCooldownRemaining;
        /// <summary>양털 보호막(GDD v2.34): 다음 피격 1회를 통째로 막는다. 지속 시간 없음, 중첩 없음. 표시용으로 동기화.</summary>
        [SyncVar] private bool hasShield;
        /// <summary>쓰러짐(2026-10-07, 멀티 전용): 체력 0이 된 순간 접속 2명 이상이면 사망 대신 쓰러진다. 서버 권위, 훅으로 모든 화면에
        /// 어두운 오버레이·콜라이더 끔을 적용한다. IsDead(체력 0)는 그대로 true라 기존 IsDead 체크(조작·피격·회복·타깃)가 전부 그대로 막는다.</summary>
        [SyncVar(hook = nameof(OnDownedChanged))] private bool isDowned;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;

        [Header("피격 넉백 (2026-10-07 타격감, [PLAYTEST])")]
        [SerializeField] private float hitKnockbackDistance = 0.3f;
        [SerializeField] private float hitKnockbackTime = 0.1f;
        public bool IsDead => currentHealth <= 0;
        /// <summary>멀티에서 쓰러져 부활을 기다리는 중(동기화 값). 솔로 사망은 false(IsDead만 true).</summary>
        public bool IsDowned => isDowned;
        public float HealCooldownRemaining => healCooldownRemaining;
        /// <summary>양털 보호막(공격 1회 막기)이 있는지(동기화 값). HUD·파티 UI 표시용.</summary>
        public bool HasShield => hasShield;
        /// <summary>디버그 메뉴(F1)의 무적 토글. 켜져 있으면 모든 대미지를 가드 판정보다 먼저 무시한다(서버 전용 값).</summary>
        public bool DebugInvincible { get; private set; }

        /// <summary>
        /// 피격 넉백 요청(서버 전용). 인자 = (공격자 위치, 거리, 시간). TryTakeDamage에서 맞으면 (hitKnockbackDistance, hitKnockbackTime),
        /// 가드로 막으면 (hitKnockbackDistance*0.5, hitKnockbackTime). PlayerMotor가 구독해 ServerApplyKnockback으로 처리(리팩터 B안 단계 3, 단계 4a에서 PlayerController → PlayerMotor).
        /// </summary>
        public event System.Action<Vector2, float, float> ServerKnockbackRequested;

        /// <summary>
        /// 체력이 실제로 줄고 아직 살아 있음(서버 전용). 사망·쓰러짐·양털 보호막·가드·무적 경로에서는 발생하지 않는다. 넉백 요청보다 먼저 발생.
        /// AbsorptionController가 구독해 패시브를 떨군다(옛 DropTraitOnHit 직접 호출 자리, 리팩터 B안 단계 3).
        /// </summary>
        public event System.Action ServerDamagedAlive;

        // 맞기 전 무시/막기 판정(대시 무적·정면 가드 등). 같은 오브젝트의 구현체를 구성 순서대로 차례로 묻는다 — 지금은 PlayerGuard(방향 막기)·PlayerDash(대시 무적, 전부 무시) 둘(리팩터 B안 단계 4b, 배열 순서 [PlayerGuard, PlayerDash]).
        private IDamageFilter[] _damageFilters;
        private float _hitInvulnerabilityRemaining;
        private Collider2D[] _bodyColliders;      // 쓰러짐 동안 끄는 물리 콜라이더(트리거 제외)
        private bool _downedStateApplied;

        private void Awake()
        {
            _damageFilters = GetComponents<IDamageFilter>();
            currentHealth = maxHealth;
        }

        public override void OnStartClient()
        {
            ApplyDownedState(isDowned, playSound: false); // 늦게 접속·스폰된 화면도 현재 쓰러짐 상태를 그린다(소리는 없이)
        }

        private void OnDownedChanged(bool _, bool downed)
        {
            ApplyDownedState(downed);
        }

        /// <summary>쓰러짐 표시·물리(모든 화면, 같은 값이면 무시): 몸 어둡게 오버레이, 물리 콜라이더 끔(다른 플레이어·몬스터와 충돌 없음), 정지.</summary>
        private void ApplyDownedState(bool downed, bool playSound = true)
        {
            if (downed == _downedStateApplied)
            {
                return;
            }

            _downedStateApplied = downed;
            // 효과음(2026-10-14): 같은 값이면 위에서 돌아가므로 호스트(서버 직접 + 훅)도 화면마다 1회. 쓰러짐 해제 = 부활 완료(ServerRevive만 해제한다).
            if (playSound)
            {
                AbsorbRoguelike.Audio.Sfx.Play(downed ? AbsorbRoguelike.Audio.SfxId.PlayerDown : AbsorbRoguelike.Audio.SfxId.ReviveDone, transform.position);
            }
            if (_bodyColliders == null)
            {
                var all = GetComponents<Collider2D>();
                var solid = new System.Collections.Generic.List<Collider2D>();
                foreach (Collider2D c in all)
                {
                    if (!c.isTrigger)
                    {
                        solid.Add(c);
                    }
                }

                _bodyColliders = solid.ToArray();
            }

            foreach (Collider2D c in _bodyColliders)
            {
                if (c != null)
                {
                    c.enabled = !downed;
                }
            }

            var body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }

            PlayerDownedVisual.Set(gameObject, downed);
        }

        /// <summary>서버: 쓰러짐 값을 바꾸고 호스트 화면에도 바로 반영한다(호스트에서 훅이 안 불리는 경우 대비 — ApplyDownedState는 같은 값이면 무시).</summary>
        [Server]
        private void ServerSetDowned(bool downed)
        {
            isDowned = downed;
            ApplyDownedState(downed);
        }

        /// <summary>
        /// 부활(서버, PlayerRevive가 3초 홀드 완료 시 호출). 체력을 health로 세우고 쓰러짐 해제, 직후 피격 무적(hitInvulnerabilityDuration)을
        /// 재사용해 일어나자마자 맞는 것을 막는다. 적중 회복 쿨다운(healCooldownRemaining)은 그대로 둔다. 패시브·흡수 스킬은 쓰러질 때 안 벗겨졌으므로 그대로.
        /// </summary>
        [Server]
        public void ServerRevive(int health)
        {
            if (!isDowned)
            {
                return;
            }

            currentHealth = Mathf.Clamp(health, 1, maxHealth);
            _hitInvulnerabilityRemaining = hitInvulnerabilityDuration;
            ServerSetDowned(false);
            Debug.Log($"[PlayerHealth] {name} revived ({currentHealth}/{maxHealth})");
        }

        /// <summary>
        /// 부활 비용(서버): 살리는 사람 체력을 그대로 깎는다. 피격 판정·무적·보호막·패시브 방출·넉백 없음.
        /// 호출부(PlayerRevive)가 체력 2 이상·cost = 체력/2 내림을 보장하므로 0이 되지 않지만, 안전하게 최소 1을 남긴다.
        /// </summary>
        [Server]
        public void ServerPayReviveCost(int amount)
        {
            if (IsDead || amount <= 0)
            {
                return;
            }

            currentHealth = Mathf.Max(1, currentHealth - amount);
        }

        private void Update()
        {
            if (!isServer || IsDead)
            {
                return;
            }

            if (_hitInvulnerabilityRemaining > 0f)
            {
                _hitInvulnerabilityRemaining -= Time.deltaTime;
            }

            if (healCooldownRemaining > 0f)
            {
                healCooldownRemaining = Mathf.Max(0f, healCooldownRemaining - Time.deltaTime);
            }
        }

        /// <summary>
        /// 양털 보호막 부여(서버 전용, 양털 폭발). 지속 시간 없이 맞을 때까지 유지되고, 이미 있으면 그대로(중첩 없음, GDD v2.34).
        /// </summary>
        [Server]
        public void AddShield()
        {
            if (IsDead)
            {
                return;
            }

            hasShield = true;
            Debug.Log($"[PlayerHealth] {name} wool shield ready (blocks next hit)");
        }

        /// <summary>내 공격이 적에게 맞았을 때(한 번 휘두를 때 1회) 호출된다. ICD가 준비돼 있고 체력이
        /// 깎여 있을 때만 고정량을 회복하고 쿨다운을 건다 — 가득 차 있으면 쿨다운을 소모하지 않는다.</summary>
        public void OnDealtHit()
        {
            if (!isServer || IsDead || healCooldownRemaining > 0f || currentHealth >= maxHealth)
            {
                return;
            }

            currentHealth = Mathf.Min(maxHealth, currentHealth + onHitHealAmount);
            healCooldownRemaining = onHitHealCooldown;
        }

        /// <summary>보스 개인 할당량 & 지원 전환 시스템(결정 대기 체크리스트 G)의 지원 게이지 발동 시
        /// 호출된다. 자동 회복(ICD)과는 별개의 즉시 회복 경로다.</summary>
        public void Heal(int amount)
        {
            if (!isServer || IsDead)
            {
                return;
            }

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            Debug.Log($"[PlayerHealth] {name} healed {amount} ({currentHealth}/{maxHealth})");
        }

        /// <summary>디버그 메뉴(F1) 무적 토글(서버). 규칙 변경 없음 — 테스트용 진입점.</summary>
        [Server]
        public void SetDebugInvincible(bool on)
        {
            DebugInvincible = on;
            Debug.Log($"[PlayerHealth] {name} debug invincible: {on}");
        }

        /// <summary>디버그 메뉴(F1) 체력 전체 회복(서버). 사망 상태는 되살리지 않는다(기존 Heal과 같은 조건).</summary>
        [Server]
        public void DebugHealFull()
        {
            Heal(maxHealth);
        }

        /// <summary>
        /// 몬스터/보스 공격 판정이 이 메서드를 호출한다. 대시 무적(IDamageFilter.IgnoresAllDamage — 지금은 PlayerDash.IsInvulnerable)과
        /// 피격 후 무적시간을 둘 다 체크해서, 둘 중 하나라도 걸려 있으면 대미지를 무시한다.
        /// 하위호환용 시그니처 — 공격자 위치 정보가 없어 방향성 가드 판정은 스킵하고 대미지를 그대로 적용한다.
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (!isServer || IsDead || DebugInvincible || _hitInvulnerabilityRemaining > 0f || AnyFilterIgnoresAllDamage())
            {
                return;
            }

            ApplyDamage(amount);
        }

        /// <summary>
        /// 공격자 위치를 아는 호출부(몬스터 공격 등)는 이 오버로드를 써야 한다. 방향성 가드
        /// (GDD "대검 가드의 구체 동작(확정, 2026-09-22)")로 막히면 대미지를 전혀 적용하지 않는다.
        /// </summary>
        public void TakeDamage(int amount, Vector2 attackerPosition)
        {
            TryTakeDamage(amount, attackerPosition);
        }

        /// <summary>
        /// TakeDamage(amount, attackerPosition)와 같은 규칙(변경 없음)에 "체력이 실제로 깎였는지"만 돌려준다(2026-10-02, 잡몹 명중 효과용).
        /// 가드 정면 막기·대시 무적·피격 무적·디버그 무적·양털 보호막으로 막혔으면 false — 이때는 피격 둔화·넉백도 걸지 않는다.
        /// </summary>
        public bool TryTakeDamage(int amount, Vector2 attackerPosition)
        {
            bool hit = TryTakeDamageCore(amount, attackerPosition, out bool guarded);
            // 피격 넉백(2026-10-07, 타격감): 맞으면 살짝 밀리고 가드로 막아도 절반. 더 크게 미는 공격(소 등)은 호출부가 뒤에 덮어쓴다.
            if (hit)
            {
                ServerKnockbackRequested?.Invoke(attackerPosition, hitKnockbackDistance, hitKnockbackTime);
            }
            else if (guarded)
            {
                ServerKnockbackRequested?.Invoke(attackerPosition, hitKnockbackDistance * 0.5f, hitKnockbackTime);
            }

            return hit;
        }

        private bool TryTakeDamageCore(int amount, Vector2 attackerPosition, out bool guarded)
        {
            guarded = false;
            if (!isServer || IsDead || DebugInvincible || _hitInvulnerabilityRemaining > 0f || AnyFilterIgnoresAllDamage())
            {
                return false;
            }

            foreach (IDamageFilter filter in _damageFilters)
            {
                if (filter.BlocksFromDirection(attackerPosition))
                {
                    Debug.Log($"[PlayerHealth] {name} blocked an attack with guard");
                    filter.OnBlockedFromDirection(attackerPosition); // 돼지 흡수 시 공격자 쪽으로 즉시 반격(쿨타임은 구현체 — 지금은 PlayerGuard — 가 관리)
                    guarded = true;
                    return false;
                }
            }

            return ApplyDamage(amount);
        }

        /// <summary>대시 무적처럼 모든 대미지를 무시하는 필터가 하나라도 있는가(가드 판정 전).</summary>
        private bool AnyFilterIgnoresAllDamage()
        {
            foreach (IDamageFilter filter in _damageFilters)
            {
                if (filter.IgnoresAllDamage)
                {
                    return true;
                }
            }

            return false;
        }

        /// <returns>체력이 실제로 깎였으면 true(양털 보호막으로 막으면 false).</returns>
        private bool ApplyDamage(int amount)
        {
            _hitInvulnerabilityRemaining = hitInvulnerabilityDuration;

            // 양털 보호막: 이번 피격 1회를 통째로 막는다 — 대미지 0, 패시브도 벗겨지지 않는다(가드로 막은 것과 같은 취급, GDD v2.34).
            // 가드·대시 무적으로 이미 막힌 공격은 여기까지 오지 않으므로 보호막을 쓰지 않는다.
            if (hasShield)
            {
                hasShield = false;
                Debug.Log($"[PlayerHealth] {name} wool shield blocked {amount} damage");
                return false;
            }

            currentHealth = Mathf.Max(0, currentHealth - amount);

            Debug.Log($"[PlayerHealth] {name} took {amount} damage ({currentHealth}/{maxHealth})");

            if (IsDead)
            {
                // 사망 후 처리: 로컬 조작 중지는 PlayerController, 실패 화면·다시 시작은 RunEndScreenController가 IsDead를 보고 처리한다.
                // 멀티(접속 2명 이상, PauseMenuController.IsSoloSession과 같은 기준)면 사망 대신 쓰러짐 — 팀원이 부활시킬 수 있다(2026-10-07).
                // 패시브·흡수 스킬은 벗기지 않는다(DropTraitOnHit 안 부름, 쓰러짐 중 유지가 확정 사양). 보호막만 해제.
                hasShield = false;
                if (NetworkServer.connections.Count >= 2)
                {
                    ServerSetDowned(true);
                    Debug.Log($"[PlayerHealth] {name} downed (waiting for revive)");
                }
                else
                {
                    Debug.Log($"[PlayerHealth] {name} died");
                }

                return true;
            }

            // 맞으면 장착 중인 패시브가 벗겨져 근처에 떨어진다(GDD 피격 시 방출 시스템). 가드로 막으면 벗겨지지 않는다.
            ServerDamagedAlive?.Invoke(); // AbsorptionController.DropTraitOnHit가 구독(리팩터 B안 단계 3)

            return true;
        }
    }
}
