using System.Collections.Generic;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Player;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 공격 AI의 공통 뼈대(리팩터 C3, 2026-10-12 — 예전 BossChargeAttack). 패턴 하나하나는 <see cref="BossPatternBase"/> 에셋 +
    /// 런타임 객체(Assets/Scripts/Monster/Boss/Patterns/)로 나뉘고, 이 클래스는 패턴들이 같이 쓰는 것만 맡는다.
    /// - 공통 상태: Idle(추적·패턴 선택) / Pattern(패턴 진행) / Stagger(경직, 파랑 틴트·딜 타임) / Recovery(짧은 회복).
    /// - 페이즈(bossPhase 1~4): 보스 전체 체력 비율(세트의 70/40/15%)로 서버가 올리기만 한다. 폭주(4)는 쿨 배율(세트 항목별) + 붉은 틴트.
    /// - 패턴 선택: 근접 우선 패턴(A) → 열린 것 중 쿨이 돌아온 것에서 가중 무작위(세트 순서) → 후보 2개 이상이면 직전 패턴 제외.
    ///   쿨은 Idle에서만 줄어든다. 상세 규칙·수치는 docs/04_구현위키/보스패턴_CDE.md·보스1_구현.md.
    /// - 공용 도구: 가장 가까운 플레이어, 원형·경로·부채꼴 판정, 파편(직선·원형), 카메라 흔들림 Rpc, 상태이상(IStatusTarget).
    /// 이동/판정은 서버 권위, 예고 표시·몸 색·페이즈 문구는 SyncVar로 모든 클라이언트가 각자 그리고, 흔들림만 ClientRpc.
    /// 로그 접두사는 예전 검색이 그대로 통하게 [BossCharge]를 유지한다.
    /// </summary>
    [RequireComponent(typeof(BossStub))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class BossBrain : NetworkBehaviour, AbsorbRoguelike.Combat.IStatusTarget
    {
        /// <summary>공통 상태. 패턴 안의 세부 단계는 patternStep(패턴마다 뜻이 다름).</summary>
        private enum BrainState : byte
        {
            Idle,
            Pattern,
            Stagger, // B·C 돌진 끝 경직, E 무방비. 끝나면 건 패턴의 쿨을 채운다
            Recovery // A 휘두르기 뒤·D 충격파 뒤 짧은 회복(쿨은 들어갈 때 이미 채움)
        }

        private const byte NoPattern = byte.MaxValue;

        [SerializeField] private BossPatternSet patternSet;
        [SerializeField] private float detectRange = 9f;
        [SerializeField] private float walkSpeed = 1f;
        [SerializeField] private Color staggerTint = new Color(0.6f, 0.6f, 1f);
        [SerializeField] private Color deflectTint = new Color(0.45f, 0.45f, 0.45f);

        [Header("파편 (GDD 보스 대미지 규칙, 프로토타입)")]
        [Tooltip("패턴이 남기는 파편. 흡수로 머금고 뱉어야 보스에게 제대로 된 대미지가 들어간다. null이면 파편을 건너뛴다. 개수는 패턴 에셋마다.")]
        [SerializeField] private GameObject debrisPrefab;
        [Tooltip("원형 파편(A·D·E)의 보스 중심 거리. [PLAYTEST]")]
        [SerializeField] private float debrisRingRadius = 2f;

        [Header("페이즈 표시 (규칙·비율은 패턴 세트 에셋)")]
        [SerializeField] private Color enrageTint = new Color(1f, 0.55f, 0.55f);
        // 페이즈 문구는 언어 표 키 boss.phase2~4(2026-10-06 언어 설정 — 예전 인스펙터 문자열 필드 대체). 색만 인스펙터 값.
        [SerializeField] private Color phase2TextColor = new Color(1f, 0.6f, 0.3f);
        [SerializeField] private Color phase3TextColor = new Color(0.85f, 0.65f, 0.35f);
        [SerializeField] private Color phase4TextColor = new Color(1f, 0.25f, 0.25f);

        // 동기화(예고 표시용). 방향을 단계보다 먼저 선언·대입한다 — 같은 프레임에 바뀌면 같은 메시지로 함께 도착한다.
        [SyncVar] private Vector2 direction; // 현재 패턴의 조준 방향(A 휘두르기·B/C 돌진)
        [SyncVar] private BrainState state;
        [SyncVar] private byte patternIndex = NoPattern; // 세트 항목 인덱스(Idle·Stagger·Recovery에서는 직전 패턴)
        [SyncVar] private byte patternStep;
        /// <summary>1~4(4 = 폭주). 0은 서버 시작 전 기본값 — hook은 0에서 바뀔 때(스폰·늦은 입장 초기 동기화) 문구를 띄우지 않는다.</summary>
        [SyncVar(hook = nameof(OnBossPhaseChanged))] private byte bossPhase;

        private BossPatternRuntime[] _runtimes = new BossPatternRuntime[0];
        private float[] _cooldowns = new float[0];
        private BossPatternRuntime _current; // 서버: 진행 중 패턴
        private int _lastPattern = -1; // 연속 방지 대상(ExcludeFromRepeatGuard가 아닌 패턴만 기록)
        private int _staggerSource = -1; // Stagger가 끝날 때 어느 패턴 쿨을 채울지
        private int _queuedDebugPattern = -1;
        private Vector2 _fallbackDirection; // 대상이 없을 때 디버그 강제 실행 방향(예전 chargeDirection — 돌진 조준만 기록)
        private bool _clientStarted;

        private BossStub _boss;
        private Rigidbody2D _body;
        private SpriteRenderer _sprite;
        private Color _baseColor;
        private float _timer; // Stagger·Recovery 남은 시간

        // 상태이상(StatusEffectReceiver가 건다, 서버 전용). 냉기 둔화 = 속도 배율(출처별 슬롯), 대지 경직·전기 마비 = 상태 정지.
        private readonly float[] _slowRemaining = new float[SlowSlots.Count];
        private readonly float[] _slowMultiplier = { 1f, 1f };
        private float _stunRemaining;

        /// <summary>둔화 배율(1 = 정상). 걷기·돌진 속도에 곱한다.</summary>
        public float SpeedMultiplier => SlowSlots.StrongestMultiplier(_slowRemaining, _slowMultiplier);

        /// <summary>현재 페이즈(1~4). 서버 시작 전에는 1로 취급.</summary>
        public int BossPhaseValue => Mathf.Max(1, (int)bossPhase);

        /// <summary>동기화된 조준 방향(패턴 예고 표시·서버 판정 공용).</summary>
        public Vector2 Direction => direction;

        public Rigidbody2D Body => _body;

        /// <summary>예고 오브젝트 정렬 기준(몸 SpriteRenderer).</summary>
        public SpriteRenderer BodySprite => _sprite;

        public BossPatternSet PatternSet => patternSet;

        private float CooldownScale => bossPhase >= 4 ? patternSet.EnrageCooldownMultiplier : 1f;

        /// <summary>둔화: walkSpeed·돌진 속도(B·C)에 배율을 곱한다. 돌진은 거리 기준으로 끝나므로 길이는 그대로고 느려지기만 한다.
        /// 잡몹(MonsterBehaviorController.ApplySlow)과 같은 규칙 — 출처별 슬롯이 따로 있고, 같은 슬롯 재호출은 덮어쓰며,
        /// 실제 배율은 남은 시간이 있는 슬롯 중 가장 낮은 값이다(보스는 현재 냉기 슬롯만 쓴다). 서버 전용, 처치됐으면 무시.</summary>
        public void ApplySlow(float multiplier, float duration, SlowSource source)
        {
            if (!isServer || _boss.IsDefeated)
            {
                return;
            }

            _slowMultiplier[(int)source] = multiplier;
            _slowRemaining[(int)source] = duration;
        }

        /// <summary>경직·마비(대지 0.3초 경직, 전기 방전 1초 마비가 같은 경로): duration초 동안 그 자리에 멈추고 상태 타이머
        /// (모든 패턴의 예고·돌진·경직·휘두르기)도 흐르지 않는다. 돌진 중에 걸려도 그대로 멈춘다(GDD v2.17 확정 — 돌진 중에도 경직·마비 적용).
        /// 이미 멈춰 있으면 남은 시간이 더 긴 쪽을 유지한다. 서버 전용, 처치됐으면 무시.</summary>
        public void ApplyStun(float duration)
        {
            if (!isServer || _boss.IsDefeated)
            {
                return;
            }

            _stunRemaining = Mathf.Max(_stunRemaining, duration);
        }

        private void Awake()
        {
            _boss = GetComponent<BossStub>();
            _body = GetComponent<Rigidbody2D>();
            _sprite = GetComponent<SpriteRenderer>();
            _baseColor = _sprite.color;

            if (patternSet == null)
            {
                Debug.LogError($"[BossCharge] {name} patternSet 없음 — 패턴을 쓰지 않는다");
                return;
            }

            // 패턴마다 보스 전용 런타임 객체(예고 오브젝트 포함)를 만든다. 처음부터 열린 패턴(unlockPhase 1)의 첫 쿨도 여기서.
            int count = patternSet.Patterns.Count;
            _runtimes = new BossPatternRuntime[count];
            _cooldowns = new float[count];
            for (int i = 0; i < count; i++)
            {
                BossPatternSet.Entry entry = patternSet.Patterns[i];
                _runtimes[i] = entry.pattern != null ? entry.pattern.CreateRuntime(this) : null;
                if (entry.unlockPhase <= 1)
                {
                    _cooldowns[i] = patternSet.FirstCooldown(entry);
                }
            }
        }

        public override void OnStartServer()
        {
            bossPhase = 1;
        }

        public override void OnStartClient()
        {
            _clientStarted = true;

            // 원격 클라이언트: 위치는 프리팹의 NetworkTransform(ServerToClient)이 채운다 — 로컬 물리가 흔들지 않게 Kinematic(2인 검증 준비).
            if (!isServer && _body != null)
            {
                _body.bodyType = RigidbodyType2D.Kinematic;
            }

            // 늦게 들어온 클라이언트: SyncVar(state·patternIndex·patternStep·direction)는 이미 들어와 있어 다음 UpdateVisuals에서
            // 단계가 바뀐 것으로 보고 그 순간부터 예고를 그린다(예전과 같은 동작).
        }

        private void Update()
        {
            if (isServer)
            {
                ServerTick();
            }

            UpdateVisuals();
        }

        private void ServerTick()
        {
            if (_boss.IsDefeated)
            {
                state = BrainState.Idle; // 모든 예고가 state 기준이라 함께 꺼진다
                _current = null;
                _body.linearVelocity = Vector2.zero;
                return;
            }

            if (patternSet == null)
            {
                return;
            }

            UpdateBossPhase();
            SlowSlots.Tick(_slowRemaining, Time.deltaTime);

            if (_stunRemaining > 0f)
            {
                _stunRemaining -= Time.deltaTime;
                _body.linearVelocity = Vector2.zero;
                return;
            }

            switch (state)
            {
                case BrainState.Idle:
                    TickIdle();
                    break;
                case BrainState.Pattern:
                    if (_current != null)
                    {
                        Apply(_current.ServerTick(Time.deltaTime));
                    }
                    break;
                case BrainState.Stagger:
                    _body.linearVelocity = Vector2.zero;
                    _timer -= Time.deltaTime;
                    if (_timer <= 0f)
                    {
                        state = BrainState.Idle;
                        RefillStaggerSourceCooldown();
                    }
                    break;
                case BrainState.Recovery:
                    _body.linearVelocity = Vector2.zero;
                    _timer -= Time.deltaTime;
                    if (_timer <= 0f)
                    {
                        state = BrainState.Idle;
                    }
                    break;
            }
        }

        // 패턴 결과 처리: Recovery는 쿨을 바로 채우고(A·D), Stagger는 경직이 끝날 때 채운다(B·C·E).
        private void Apply(PatternTick tick)
        {
            switch (tick.Result)
            {
                case PatternTick.Kind.Recovery:
                    FillCooldown(patternIndex);
                    _current = null;
                    state = BrainState.Recovery;
                    _timer = tick.Duration;
                    break;
                case PatternTick.Kind.Stagger:
                    _current = null;
                    state = BrainState.Stagger;
                    _timer = tick.Duration;
                    _staggerSource = patternIndex;
                    _body.linearVelocity = Vector2.zero;
                    break;
            }
        }

        private void FillCooldown(int index)
        {
            if (index < 0 || index >= _cooldowns.Length)
            {
                return;
            }

            BossPatternSet.Entry entry = patternSet.Patterns[index];
            _cooldowns[index] = entry.pattern.Cooldown * (entry.applyEnrageMultiplier ? CooldownScale : 1f);
        }

        // Stagger를 건 패턴의 쿨만 채운다(B·C는 폭주 배율, E는 세트에서 배율 끔).
        private void RefillStaggerSourceCooldown()
        {
            FillCooldown(_staggerSource);
            _staggerSource = -1;
        }

        // ── 페이즈 ─────────────────────────────────────────────

        /// <summary>보스 전체 체력(currentHealth — 개인 할당량 모드도 동일) 비율로 페이즈를 올린다. 내려가지 않는다(재생 회복 포함).</summary>
        private void UpdateBossPhase()
        {
            int max = _boss.MaxHealth;
            if (max <= 0)
            {
                return;
            }

            float ratio = _boss.CurrentHealth / (float)max;
            byte target = ratio <= patternSet.EnrageHealthRatio ? (byte)4
                : ratio <= patternSet.Phase3HealthRatio ? (byte)3
                : ratio <= patternSet.Phase2HealthRatio ? (byte)2
                : (byte)1;
            if (target <= bossPhase)
            {
                return;
            }

            byte previous = (byte)Mathf.Max(1, bossPhase);
            // 새로 열린 패턴의 첫 쿨(한 번에 여러 페이즈를 건너뛰면 그 사이 패턴이 모두 열린다)
            for (int i = 0; i < _cooldowns.Length; i++)
            {
                BossPatternSet.Entry entry = patternSet.Patterns[i];
                if (entry.unlockPhase > previous && entry.unlockPhase <= target)
                {
                    _cooldowns[i] = patternSet.FirstCooldown(entry);
                }
            }

            bossPhase = target;
            Debug.Log($"[BossCharge] {name} phase {previous} → {target} (health {_boss.CurrentHealth}/{max})");
        }

        // 모든 클라이언트(호스트 포함)에서 페이즈 문구. 0에서 바뀌는 것(스폰·늦은 입장 초기 동기화)은 띄우지 않는다.
        private void OnBossPhaseChanged(byte oldValue, byte newValue)
        {
            if (oldValue == 0 || !_clientStarted || newValue <= oldValue)
            {
                return;
            }

            string text;
            Color color;
            switch (newValue)
            {
                case 2:
                    text = Loc.Get("boss.phase2");
                    color = phase2TextColor;
                    break;
                case 3:
                    text = Loc.Get("boss.phase3");
                    color = phase3TextColor;
                    break;
                case 4:
                    text = Loc.Get("boss.phase4");
                    color = phase4TextColor;
                    break;
                default:
                    return;
            }

            float top = _sprite != null && _sprite.sprite != null ? _sprite.sprite.bounds.max.y : 0.5f;
            Vector3 pos = transform.TransformPoint(new Vector3(0f, top + 28f * DamagePopup.Pixel, 0f));
            DamagePopup.SpawnText(pos, text, color);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.BossPhase, transform.position); // 페이즈 전환(문구와 같은 순간, 2026-10-14)
        }

        // ── 패턴 선택 ───────────────────────────────────────────

        private bool IsUnlocked(int index) => BossPhaseValue >= patternSet.Patterns[index].unlockPhase;

        private void TickIdle()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _cooldowns.Length; i++)
            {
                _cooldowns[i] -= dt;
            }

            Transform target = FindNearestPlayer();
            Vector2 toTarget = target != null ? ((Vector2)target.position - _body.position).normalized : Vector2.zero;

            if (_queuedDebugPattern >= 0)
            {
                int queued = _queuedDebugPattern;
                _queuedDebugPattern = -1;
                StartPattern(queued, toTarget != Vector2.zero ? toTarget : FallbackDirection());
                return;
            }

            if (target == null)
            {
                _body.linearVelocity = Vector2.zero;
                return;
            }

            // (1) 근접 우선 패턴(A) — 연속 방지 대상이 아니다.
            float distance = Vector2.Distance(_body.position, target.position);
            for (int i = 0; i < _runtimes.Length; i++)
            {
                BossPatternBase pattern = patternSet.Patterns[i].pattern;
                if (_runtimes[i] != null && pattern.IsMeleePriority && IsUnlocked(i) && _cooldowns[i] <= 0f
                    && distance <= pattern.MeleeTriggerRange)
                {
                    StartPattern(i, toTarget);
                    return;
                }
            }

            // (2) 열린 패턴 중 쿨이 돌아온 것에서 가중 무작위, (3) 직전 패턴은 다른 후보가 있으면 제외.
            int chosen = ChoosePattern();
            if (chosen < 0)
            {
                _body.linearVelocity = toTarget * (walkSpeed * SpeedMultiplier);
                return;
            }

            StartPattern(chosen, toTarget);
        }

        private readonly List<(int index, float weight)> _candidates = new List<(int, float)>();

        private int ChoosePattern()
        {
            _candidates.Clear();
            for (int i = 0; i < _runtimes.Length; i++)
            {
                BossPatternBase pattern = patternSet.Patterns[i].pattern;
                if (_runtimes[i] == null || pattern.IsMeleePriority || !IsUnlocked(i) || _cooldowns[i] > 0f)
                {
                    continue;
                }

                float w = pattern.GetWeight(this);
                if (w > 0f)
                {
                    _candidates.Add((i, w));
                }
            }

            if (_candidates.Count > 1)
            {
                _candidates.RemoveAll(c => c.index == _lastPattern);
            }

            float total = 0f;
            foreach (var c in _candidates)
            {
                total += c.weight;
            }

            if (total <= 0f)
            {
                return -1;
            }

            float roll = Random.value * total;
            foreach (var c in _candidates)
            {
                roll -= c.weight;
                if (roll <= 0f)
                {
                    return c.index;
                }
            }

            return _candidates[_candidates.Count - 1].index;
        }

        private void StartPattern(int index, Vector2 aim)
        {
            _body.linearVelocity = Vector2.zero;
            BossPatternBase pattern = patternSet.Patterns[index].pattern;
            if (!pattern.ExcludeFromRepeatGuard)
            {
                _lastPattern = index;
            }

            _current = _runtimes[index];
            patternIndex = (byte)index;
            _current.Begin(aim); // 방향·단계 0을 먼저 쓰고
            state = BrainState.Pattern; // 상태를 마지막에
            Debug.Log($"[BossCharge] {name} pattern {pattern.PatternId} (phase {BossPhaseValue})");
        }

        private Vector2 FallbackDirection()
        {
            return _fallbackDirection != Vector2.zero ? _fallbackDirection : Vector2.down;
        }

        /// <summary>세트에서 PatternId가 같은 첫 항목 인덱스(없으면 -1).</summary>
        public int FindPatternIndex(string patternId)
        {
            if (patternSet == null)
            {
                return -1;
            }

            for (int i = 0; i < patternSet.Patterns.Count; i++)
            {
                BossPatternBase pattern = patternSet.Patterns[i].pattern;
                if (pattern != null && pattern.PatternId == patternId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 디버그 메뉴(F1, 호스트): 쿨·페이즈 제한을 무시하고 패턴(PatternId, 예: "C")을 시작한다. Idle이 아니면(다른 패턴 중·경직 중)
        /// 다음 Idle에 바로 시작하도록 예약한다. 처치됐거나 세트에 없는 패턴이면 무시.
        /// </summary>
        [Server]
        public void DebugUsePattern(string patternId)
        {
            int index = FindPatternIndex(patternId);
            if (_boss.IsDefeated || index < 0 || _runtimes[index] == null)
            {
                return;
            }

            if (state == BrainState.Idle && _stunRemaining <= 0f)
            {
                Transform target = FindNearestPlayer();
                Vector2 dir = target != null ? ((Vector2)target.position - _body.position).normalized : FallbackDirection();
                StartPattern(index, dir);
            }
            else
            {
                _queuedDebugPattern = index;
            }
        }

        // ── 패턴이 쓰는 동기화 값 (서버) ───────────────────────

        /// <summary>서버: 조준 방향을 바꾼다(단계보다 먼저 부른다). rememberAsFallback = 대상이 없을 때 디버그 강제 실행이 쓸 방향으로도 기억(돌진 조준).</summary>
        public void SetDirection(Vector2 value, bool rememberAsFallback)
        {
            direction = value;
            if (rememberAsFallback)
            {
                _fallbackDirection = value;
            }
        }

        /// <summary>서버: 패턴 안 단계(예고 표시용, 뜻은 패턴마다).</summary>
        public void SetStep(byte step)
        {
            patternStep = step;
        }

        // ── 공용 도구 (서버) ───────────────────────────────────

        public Transform FindNearestPlayer()
        {
            Transform nearest = null;
            float nearestDistance = detectRange;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
                if (playerHealth != null && playerHealth.IsDead)
                {
                    continue; // 쓰러진 플레이어는 추적·조준 대상에서 뺀다(2026-10-07 쓰러짐·부활)
                }

                float distance = Vector2.Distance(_body.position, player.transform.position);
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = player.transform;
                }
            }

            return nearest;
        }

        public bool AnyPlayerWithin(float radius)
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
                if (playerHealth != null && playerHealth.IsDead)
                {
                    continue; // 쓰러진 플레이어는 추적·조준 대상에서 뺀다(2026-10-07 쓰러짐·부활)
                }

                if (Vector2.Distance(_body.position, player.transform.position) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>돌진 경로: 돌진 폭의 절반 + 플레이어 반 칸 안에 들어온 플레이어를 돌진 1회당 한 번만 맞힌다(B·C 공용, alreadyHit는 패턴이 돌진마다 비움).</summary>
        public void HitPlayersInPath(float chargeWidth, int hitDamage, HashSet<PlayerHealth> alreadyHit)
        {
            float hitRadius = chargeWidth * 0.5f + 0.5f;
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                if (alreadyHit.Contains(health))
                {
                    continue;
                }

                if (Vector2.Distance(_body.position, health.transform.position) <= hitRadius)
                {
                    health.TakeDamage(AbsorbRoguelike.Dungeon.DifficultyScaling.MonsterDamage(hitDamage), _body.position); // 층 대미지 배율(2026-10-13)
                    alreadyHit.Add(health);
                }
            }
        }

        // D·E 원형 충격파: 예고가 끝난 순간 반경(+플레이어 반 칸) 안의 모든 플레이어. 공격자 위치가 보스 중심이라
        // 가드 방향 판정·대시 무적·양 보호막이 PlayerHealth.TakeDamage에서 기존대로 적용된다.
        public void HitPlayersInRadius(float radius, int hitDamage)
        {
            const float playerHalfSize = 0.5f;
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                if (Vector2.Distance(_body.position, health.transform.position) <= radius + playerHalfSize)
                {
                    health.TakeDamage(AbsorbRoguelike.Dungeon.DifficultyScaling.MonsterDamage(hitDamage), _body.position); // 층 대미지 배율(2026-10-13)
                }
            }
        }

        // 예고가 끝난 순간 부채꼴 안에 남아 있는 플레이어만 맞는다(예고를 보고 빠지거나 가드하면 피함).
        // 연타 휘두르기에서는 타격마다 따로 판정한다(타격 사이에 빠지거나 가드하면 그 타격은 피함).
        public void HitPlayersInCone(Vector2 aim, float radius, float halfAngle, int hitDamage)
        {
            const float playerHalfSize = 0.4f;
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                Vector2 offset = (Vector2)health.transform.position - _body.position;
                if (offset.magnitude > radius + playerHalfSize)
                {
                    continue;
                }

                // 몸에 거의 겹쳐 있으면 각도와 상관없이 맞는다.
                if (offset.magnitude <= 1f || Vector2.Angle(aim, offset) <= halfAngle)
                {
                    health.TakeDamage(AbsorbRoguelike.Dungeon.DifficultyScaling.MonsterDamage(hitDamage), _body.position); // 층 대미지 배율(2026-10-13)
                }
            }
        }

        // B·C: 벽 쪽이 아니라 방 안쪽(돌진 반대 방향 back)으로 옆으로 흩어서 떨어뜨려, 플레이어가 주우러 갈 수 있게 한다.
        public void SpawnDebrisLine(Vector2 origin, Vector2 back, int count)
        {
            if (debrisPrefab == null || count <= 0)
            {
                return;
            }

            Vector2 side = new Vector2(-back.y, back.x);
            for (int i = 0; i < count; i++)
            {
                float spread = count > 1 ? (i / (float)(count - 1) - 0.5f) * 2f : 0f;
                Vector2 pos = origin + back * 1.8f + side * spread;
                NetworkServer.Spawn(Instantiate(debrisPrefab, pos, Quaternion.identity));
            }
        }

        // A·D·E: 보스 주변 반경 debrisRingRadius에 무작위 시작 각도로 고르게 흩뿌린다.
        public void SpawnDebrisRing(Vector2 origin, int count)
        {
            if (debrisPrefab == null || count <= 0)
            {
                return;
            }

            float start = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float angle = start + i * Mathf.PI * 2f / count;
                Vector2 pos = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * debrisRingRadius;
                NetworkServer.Spawn(Instantiate(debrisPrefab, pos, Quaternion.identity));
            }
        }

        /// <summary>서버: 모든 화면 카메라 흔들림(D·E 충격파).</summary>
        public void CameraShake(float duration, float amplitude)
        {
            RpcCameraShake(duration, amplitude);
        }

        [ClientRpc]
        private void RpcCameraShake(float duration, float amplitude)
        {
            PlayerCameraRig.RequestCameraShake(duration, amplitude);
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.BossSlam, transform.position); // D 지진 강타·E 포효 대지분쇄 충격(2026-10-14)
        }

        /// <summary>서버: 돌진이 벽에 부딪혀 경직되는 순간(B 돌진·C 연속 돌진 마지막) — 모든 화면 효과음. 경직 자체는 벽 없이 끝나도 오므로
        /// 상태 동기화만으로는 구분할 수 없어 최소 RPC를 둔다(2026-10-14).</summary>
        public void NotifyWallHit()
        {
            RpcWallHit();
        }

        [ClientRpc]
        private void RpcWallHit()
        {
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.BossWallHit, transform.position);
        }

        // ── 벽 충돌 (패턴에 위임) ──────────────────────────────

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!isServer || state != BrainState.Pattern || _current == null
                || collision.collider.GetComponent<PlayerController>() != null)
            {
                return;
            }

            Apply(_current.OnServerCollisionEnter(collision));
        }

        // C 중간 돌진이 벽에 붙은 채 끝나면 재조준 방향이 다시 벽 쪽일 때 Enter가 또 오지 않아 벽을 계속 밀 수 있다 — 패턴이 Stay로 처리.
        private void OnCollisionStay2D(Collision2D collision)
        {
            if (!isServer || state != BrainState.Pattern || _current == null
                || collision.collider.GetComponent<PlayerController>() != null || collision.contactCount == 0)
            {
                return;
            }

            Apply(_current.OnServerCollisionStay(collision));
        }

        // ── 표시 ───────────────────────────────────────────────

        // 예고 진하기(결정 ㊶, 2026-10-02): 각 화면이 (상태·패턴·단계)가 바뀐 순간의 로컬 Time.time을 기준으로 진행률을 계산한다 —
        // 원격 클라이언트도 SyncVar 도착 시점부터 같은 길이로 진해진다(판정·타이머는 서버 그대로, 표시 전용).
        private int _seenKey = -1;
        private float _stepStartLocal;

        private void TrackStepStart()
        {
            int key = ((int)state << 16) | (patternIndex << 8) | patternStep;
            if (key == _seenKey)
            {
                return;
            }

            bool firstSeen = _seenKey == -1;
            _seenKey = key;
            _stepStartLocal = Time.time;

            // 효과음(2026-10-14): 패턴 단계가 바뀐 순간 그 패턴이 정한 소리(돌진 시작·포효 등). 각 화면이 동기화 값 변화를 보고 1회.
            if (!firstSeen && state == BrainState.Pattern && patternIndex < _runtimes.Length && _runtimes[patternIndex] != null)
            {
                string sfx = _runtimes[patternIndex].StepSfxId(patternStep);
                if (sfx != null)
                {
                    AbsorbRoguelike.Audio.Sfx.Play(sfx, transform.position);
                }
            }
        }

        // 보스 쓰러짐 효과음(2026-10-14): 동기화된 IsDefeated가 false → true로 바뀌는 순간(각 화면 1회). 처음 본 값은 기준으로만.
        private bool? _shownDefeated;

        private void UpdateDefeatSound()
        {
            bool defeated = _boss.IsDefeated;
            if (_shownDefeated == false && defeated)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.BossDown, transform.position);
            }

            _shownDefeated = defeated;
        }

        private void UpdateVisuals()
        {
            TrackStepStart();
            UpdateDefeatSound();

            bool inPattern = state == BrainState.Pattern;
            for (int i = 0; i < _runtimes.Length; i++)
            {
                _runtimes[i]?.DrawTelegraph(inPattern && patternIndex == i, patternStep, _stepStartLocal);
            }

            // 몸 색은 여기서 매 프레임 정한다. 상태이상 색은 몸 색을 건드리지 않고 StatusEffectReceiver의 자식 오버레이가
            // 위에 덮는다(회색 번쩍임 중에는 오버레이가 숨는다) — 우선순위는 StatusEffectReceiver 주석 참고.
            // 몸 색 우선순위: 회색 번쩍임 > 경직 파랑(B·C·E 무방비 포함) > 폭주 붉은 틴트(bossPhase 4) > 기본 색.
            if (_boss.IsShowingDeflect)
            {
                _sprite.color = deflectTint; // 무기 공격이 안 먹혔다는 신호(보스 대미지 규칙)
            }
            else if (state == BrainState.Stagger)
            {
                _sprite.color = staggerTint;
            }
            else if (bossPhase >= 4)
            {
                _sprite.color = enrageTint;
            }
            else
            {
                _sprite.color = _baseColor;
            }
        }
    }
}
