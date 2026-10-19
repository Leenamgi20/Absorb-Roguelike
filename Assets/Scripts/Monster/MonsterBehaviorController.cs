using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>둔화 출처(슬롯). 잡몹(MonsterBehaviorController)·보스(BossBrain) 공용 ApplySlow 인자.</summary>
    public enum SlowSource
    {
        Snake, // 뱀 흡수 스킬 휘감기(PlayerController)
        Ice,   // 냉기 속성 스택 둔화(StatusEffectReceiver)
    }

    /// <summary>둔화 슬롯 계산 공용 헬퍼 — 슬롯마다 배율·남은 시간을 따로 두고, 남은 시간이 있는 슬롯 중 가장 낮은 배율을 쓴다.</summary>
    internal static class SlowSlots
    {
        public const int Count = 2; // SlowSource 항목 수

        public static float StrongestMultiplier(float[] remaining, float[] multiplier)
        {
            float best = 1f;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0f && multiplier[i] < best)
                {
                    best = multiplier[i];
                }
            }

            return best;
        }

        public static void Tick(float[] remaining, float dt)
        {
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0f)
                {
                    remaining[i] -= dt;
                }
            }
        }
    }

    /// <summary>
    /// 잡몹 공통 행동 패턴("원형", Content Design의 '원형(패턴)과 속성(스킨) 분리' 원칙 참고): 플레이어가
    /// 감지 범위 안에 들어오면 쫓아가고, 공격 범위 안에 들어오면 공격을 시도한다. 이 클래스 하나로 기본 잡몹을
    /// 그대로 커버하고, 동물별 패턴(잡몹 패턴 1차, 2026-10-02 — docs/04_구현위키/잡몹_패턴_1차.md)은 하위 클래스가
    /// 아래 protected virtual 훅만 오버라이드한다 — 기본 이동/추적/쿨타임 로직은 그대로.
    /// 확장 훅: CreateTelegraphVisual/PlaceTelegraphVisual(예고 모양), OnWindupFinished/PerformAttack(판정),
    /// ModifyIncomingDamage(피격 반응), OnAttackHitPlayer(명중 효과), InAttackSequence + TickAttackSequence(다단 공격 —
    /// 그동안 일반 이동 정지·공격 쿨다운 정지).
    /// 이동/공격 판정은 서버(호스트) 권위로만 실행한다(ADR-003) — 원격 클라이언트 화면은 NetworkTransform이
    /// 동기화해서 보여주고, 예고 표시는 각 화면이 windupSerial 변화 시점의 로컬 시간으로 진하기를 계산한다.
    /// </summary>
    [RequireComponent(typeof(MonsterStub))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class MonsterBehaviorController : NetworkBehaviour, AbsorbRoguelike.Combat.IStatusTarget
    {
        protected enum State
        {
            Idle,
            Chase,
            Attack
        }

        [Header("수치 (리팩터 C4 — Assets/Data/Monsters/<동물>.asset)")]
        [Tooltip("이 몬스터의 게임플레이 수치(MonsterStatsConfig 또는 행동별 하위 타입 — ChargeMonsterConfig 등). 일반·정예 프리팹이 같은 에셋을 쓴다.")]
        [SerializeField] private MonsterStatsConfig config;

        [Header("공격 예고 표시")]
        [Tooltip("예고 색. RGB만 쓰고 알파는 예고 진하기 규칙(TelegraphUtil.SetProgress, 결정 ㊶)이 정한다.")]
        [SerializeField] protected Color telegraphColor = new Color(1f, 0.2f, 0.2f, 0.35f);

        private bool _aggroed;

        /// <summary>현재 공격력(서버). Awake에서 config.attackDamage로 채우고 정예 배율(ApplyEliteDamageMultiplier)이 여기에 곱한다 —
        /// 공유 에셋 값은 런타임에 바꾸지 않는다.</summary>
        protected int attackDamage;

        private float _knockbackRemaining;
        private Vector2 _knockbackVelocity;
        // 둔화 슬롯(출처별, 인덱스 = (int)SlowSource) — 뱀 휘감기와 냉기가 서로 덮어쓰지 않는다.
        private readonly float[] _slowRemaining = new float[SlowSlots.Count];
        private readonly float[] _slowMultiplier = { 1f, 1f };
        private float _stunRemaining;

        protected MonsterStub MonsterStub { get; private set; }
        protected Rigidbody2D Rigidbody { get; private set; }
        protected Transform CurrentTarget { get; private set; }
        protected State CurrentState { get; private set; } = State.Idle;

        [SyncVar] private bool isWindingUp;
        [SyncVar] private Vector2 attackDirection;
        // 예고 진하기 동기화(2026-10-02): 예고를 새로 시작할 때마다 서버가 1 올린다. 각 화면은 값이 바뀐 순간의 로컬 Time.time을
        // 시작 시각으로 잡고 windupLength로 진행률을 계산한다(연속 공격의 매 타 재시작도 이 값 하나로 처리).
        [SyncVar] private byte windupSerial;
        [SyncVar] private float windupLength;

        private float _attackCooldownRemaining;
        private float _windupRemaining;
        private float _recoveryRemaining;
        private Transform _windupTarget;
        private GameObject _telegraph;
        private SpriteRenderer _telegraphRenderer;
        private byte _seenWindupSerial;
        private float _windupStartLocal;

        /// <summary>다단 공격(돌진·연속 공격·후퇴 깡총 등) 진행 중. true인 동안 일반 추적·공격 시작을 하지 않고
        /// TickAttackSequence만 부르며, 공격 쿨다운도 흐르지 않는다(시퀀스가 끝난 뒤부터 쿨). 서버 전용 값.</summary>
        protected bool InAttackSequence { get; set; }

        /// <summary>현재 예고 중인지(동기화 값) — 하위 클래스 표시용.</summary>
        protected bool IsWindingUp => isWindingUp;

        /// <summary>이 화면 기준 예고 진행률 0~1(windupSerial이 바뀐 순간의 로컬 시간 기준, 2차 — 양 털 폭발 원·닭 상승 연출용 표시 값).</summary>
        protected float WindupProgress => TelegraphUtil.Progress(_windupStartLocal, windupLength);

        /// <summary>현재 예고 길이(동기화 값).</summary>
        protected float WindupLength => windupLength;

        /// <summary>현재 공격(예고) 방향(동기화 값).</summary>
        protected Vector2 AttackDirection => attackDirection;

        /// <summary>공격 후딜 남은 시간(서버). 하위 클래스가 후딜 대신 다른 동작(깡총 후퇴 등)을 할 때 0으로 만든다.</summary>
        protected float RecoveryRemaining
        {
            get => _recoveryRemaining;
            set => _recoveryRemaining = value;
        }

        /// <summary>이 몬스터의 공통 수치 에셋(MonsterStub.maxHealth도 여기서 읽는다).</summary>
        public MonsterStatsConfig Stats => config;

        /// <summary>하위 클래스가 자기 config 타입(ChargeMonsterConfig 등)으로 읽는다. 타입이 다르면 null.</summary>
        protected T Config<T>() where T : MonsterStatsConfig => config as T;

        /// <summary>이 행동이 요구하는 config 타입. 하위 클래스가 자기 타입으로 오버라이드한다.</summary>
        protected virtual System.Type ConfigType => typeof(MonsterStatsConfig);

        protected virtual void Awake()
        {
            // 리팩터 C4: 수치 에셋이 없거나 타입이 맞지 않으면 설정 실수 — 코드 기본값으로 대신 돌리지 않고 알린다(AbsorbSkillHost 5c와 같은 방침).
            if (config == null || !ConfigType.IsInstanceOfType(config))
            {
                Debug.LogError($"[MonsterBehaviorController] {name}: config 참조가 비었거나 {ConfigType.Name}이 아니다 — 프리팹의 {GetType().Name}.config에 Assets/Data/Monsters 에셋을 연결해야 한다.", this);
            }
            else
            {
                attackDamage = config.attackDamage;
            }

            MonsterStub = GetComponent<MonsterStub>();
            Rigidbody = GetComponent<Rigidbody2D>();
            _telegraph = CreateTelegraphVisual();
            _telegraphRenderer = _telegraph != null ? _telegraph.GetComponent<SpriteRenderer>() : null;
        }

        /// <summary>원격 클라이언트(서버 아님): 위치는 프리팹의 NetworkTransform(ServerToClient)이 채운다. 로컬 물리가 위치를 흔들지 않게
        /// 리지드바디를 Kinematic으로 둔다(원격 플레이어 몸은 여전히 이 몸에 막힌다). 2인 검증 준비(2026-09-29).</summary>
        public override void OnStartClient()
        {
            if (!isServer && Rigidbody != null)
            {
                Rigidbody.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        /// <summary>예고 모양 생성(클라이언트마다 로컬, Awake에서 1회). 기본 = 몸에서 앞으로 뻗는 직사각형(길이 attackAreaLength × 폭 attackAreaWidth).</summary>
        protected virtual GameObject CreateTelegraphVisual()
        {
            return TelegraphUtil.CreateLine(transform, "AttackTelegraph", telegraphColor, GetComponent<SpriteRenderer>());
        }

        /// <summary>예고 위치·회전·크기 맞추기(매 프레임, 표시 중일 때만).</summary>
        protected virtual void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            TelegraphUtil.PlaceLine(telegraph, direction, config.attackAreaLength, config.attackAreaWidth);
        }

        /// <summary>기본 넉백 거리(칸). 절대 거리로 넉백을 주려는 호출부(말 대시 1.5칸)가 배율 환산에 쓴다.</summary>
        public float KnockbackDistance => config.knockbackDistance;

        /// <summary>발견 거리를 건너뛰고 바로 추적 상태로(서버). 호출부: RoomClearController — 방이 잠기는 순간 방 안 몬스터 전원.</summary>
        public void Aggro()
        {
            _aggroed = true;
        }

        /// <summary>플레이어에게 맞았을 때 공격자 반대 방향으로 밀려난다(서버에서만). 호출부: PlayerController 공격 판정.</summary>
        public void ApplyKnockback(Vector2 attackerPosition, float distanceScale = 1f)
        {
            if (!isServer || MonsterStub.IsDead || config.knockbackDuration <= 0f)
            {
                return;
            }

            _aggroed = true; // 맞으면 멀리서 맞았어도 쫓아온다
            Vector2 away = ((Vector2)transform.position - attackerPosition).normalized;
            _knockbackVelocity = away * (config.knockbackDistance * distanceScale / config.knockbackDuration);
            _knockbackRemaining = config.knockbackDuration;
        }

        /// <summary>이동속도를 multiplier배로 duration초 동안 낮춘다. 출처별 슬롯(SlowSource.Snake = 뱀 흡수 스킬 휘감기,
        /// SlowSource.Ice = 냉기 스택 둔화 — StatusEffectReceiver)이 따로 있어 서로 덮어쓰지 않는다. 같은 슬롯을 다시 부르면
        /// 그 슬롯의 배율·시간을 덮어쓴다(냉기 스택 상승 시 배율 갱신). 실제 속도는 남은 시간이 있는 슬롯 중 가장 낮은
        /// 배율(가장 강한 둔화)을 쓴다 — 곱하지 않는다. 서버 전용, 죽었으면 무시.</summary>
        public void ApplySlow(float multiplier, float duration, SlowSource source)
        {
            if (!isServer || MonsterStub.IsDead)
            {
                return;
            }

            _slowMultiplier[(int)source] = multiplier;
            _slowRemaining[(int)source] = duration;
        }

        /// <summary>경직·마비(StatusEffectReceiver — 대지 0.3초 경직, 전기 방전 1초 마비가 같은 슬롯). duration초 동안
        /// 이동·공격 예고·후딜 타이머가 모두 멈춘다. 서버 전용, 죽었으면 무시. 이미 멈춰 있으면 남은 시간이 더 긴 쪽을 유지한다.</summary>
        public void ApplyStun(float duration)
        {
            if (!isServer || MonsterStub.IsDead)
            {
                return;
            }

            _stunRemaining = Mathf.Max(_stunRemaining, duration);
        }

        /// <summary>피격 반응 훅(서버). MonsterStub.TakeDamage(amount, attackerPosition)가 대미지 적용 직전에 부른다.
        /// 기본은 그대로 돌려준다. 정면 방어(ShieldMonsterBehavior) 등이 오버라이드한다.</summary>
        public virtual int ModifyIncomingDamage(Vector2 attackerPosition, int damage)
        {
            return damage;
        }

        protected float CurrentMoveSpeed => config.moveSpeed * AbsorbRoguelike.Dungeon.DungeonGameManager.MonsterMoveSpeedScale * SlowSlots.StrongestMultiplier(_slowRemaining, _slowMultiplier);

        /// <summary>현재 둔화 배율(1 = 둔화 없음). 돌진 속도 등 이동속도 외의 속도에도 같은 둔화를 곱할 때 쓴다.</summary>
        protected float SlowFactor => SlowSlots.StrongestMultiplier(_slowRemaining, _slowMultiplier);

        /// <summary>정예몹 공격력 배율 적용(결정 대기 체크리스트 D). EliteMonsterMarker.OnStartServer가 호출한다.</summary>
        public void ApplyEliteDamageMultiplier(float multiplier)
        {
            attackDamage = Mathf.RoundToInt(attackDamage * multiplier);
        }

        private void Update()
        {
            UpdateTelegraph();
            UpdateClientVisuals();

            if (!isServer || MonsterStub.IsDead)
            {
                Rigidbody.linearVelocity = Vector2.zero;
                if (isServer && isWindingUp)
                {
                    isWindingUp = false; // 예고 중에 죽으면(정예몹 시체) 공격을 취소한다 — 예고 범위가 시체에 남지 않게
                }

                if (isServer && InAttackSequence)
                {
                    InAttackSequence = false;
                    OnAttackSequenceCancelled();
                }

                return;
            }

            if (_attackCooldownRemaining > 0f && !InAttackSequence)
            {
                _attackCooldownRemaining -= Time.deltaTime;
            }

            SlowSlots.Tick(_slowRemaining, Time.deltaTime);

            // 넉백 중에는 다른 행동을 멈춘다 — 예고 중에 맞으면 공격이 그만큼 늦어진다.
            if (_knockbackRemaining > 0f)
            {
                _knockbackRemaining -= Time.deltaTime;
                Rigidbody.linearVelocity = _knockbackVelocity;
                return;
            }

            // 대지 경직: 제자리에 멈추고 예고·후딜 타이머도 흐르지 않는다 — 예고 중에 경직되면 예고 판정도 그만큼 늦어진다.
            // (공격 쿨타임·둔화 시간은 위에서 이미 흐른다.)
            if (_stunRemaining > 0f)
            {
                _stunRemaining -= Time.deltaTime;
                Rigidbody.linearVelocity = Vector2.zero;
                return;
            }

            if (InAttackSequence)
            {
                TickAttackSequence();
                return;
            }

            if (isWindingUp)
            {
                Rigidbody.linearVelocity = Vector2.zero;
                _windupRemaining -= Time.deltaTime;
                if (_windupRemaining <= 0f)
                {
                    isWindingUp = false;
                    _recoveryRemaining = config.recoveryDuration;
                    OnWindupFinished(_windupTarget);
                }

                return;
            }

            if (_recoveryRemaining > 0f)
            {
                // 공격 후 제자리에 버티지 않고 공격 반대 방향으로 천천히 물러나 간격을 만든다.
                Rigidbody.linearVelocity = -attackDirection * (CurrentMoveSpeed * config.recoveryRetreatSpeedMultiplier);
                _recoveryRemaining -= Time.deltaTime;
                return;
            }

            CurrentTarget = FindNearestPlayer();
            CurrentState = DecideState(CurrentTarget);
            RunState();
        }

        private void UpdateTelegraph()
        {
            if (_telegraph == null)
            {
                return;
            }

            // 예고를 새로 시작했으면(서버가 windupSerial을 올림) 이 화면의 로컬 시간으로 진하기 타이머를 재시작한다.
            if (windupSerial != _seenWindupSerial)
            {
                _seenWindupSerial = windupSerial;
                _windupStartLocal = Time.time;
                _warnSoundPending = true;
            }

            // 죽은 뒤(정예몹 시체)에는 SyncVar가 풀리기 전 프레임에도 예고를 숨긴다.
            bool show = isWindingUp && !MonsterStub.IsDead && !MonsterStub.IsDefeated;

            // 효과음(2026-10-14): 예고가 새로 시작될 때(windupSerial 변화 — 각 화면 1회) 예고음, 예고가 끝나 공격이 나가는 순간 WindupEndSfxId.
            // 예고는 끝나거나(공격) 죽어서만 꺼지므로, 살아 있는 채로 꺼지면 공격 순간이다.
            if (_warnSoundPending)
            {
                _warnSoundPending = false;
                if (show)
                {
                    AbsorbRoguelike.Audio.Sfx.Play(WarnSfxId, transform.position);
                }
            }

            if (_telegraphWasShown && !show && !MonsterStub.IsDead && !MonsterStub.IsDefeated && WindupEndSfxId != null)
            {
                AbsorbRoguelike.Audio.Sfx.Play(WindupEndSfxId, transform.position);
            }

            _telegraphWasShown = show;
            _telegraph.SetActive(show);
            if (show)
            {
                PlaceTelegraphVisual(_telegraph, attackDirection);
                TelegraphUtil.SetProgress(_telegraphRenderer, TelegraphUtil.Progress(_windupStartLocal, windupLength), telegraphColor, windupLength);
            }
        }

        private bool _warnSoundPending;
        private bool _telegraphWasShown;

        /// <summary>공격 예고 효과음 id(기본 공통 예고음). 개 돌진처럼 고유 소리가 있으면 하위 클래스가 바꾼다(겹쳐 울리지 않게 하나만).</summary>
        protected virtual string WarnSfxId => AbsorbRoguelike.Audio.SfxId.MonsterWarn;

        /// <summary>예고가 끝나 공격이 나가는 순간의 효과음 id(기본 없음). 용 브레스가 쓴다.</summary>
        protected virtual string WindupEndSfxId => null;

        /// <summary>클라이언트 표시 훅(모든 화면에서 매 프레임, 서버 판정 전에 호출). 방패·경직 색 등 하위 클래스 전용 표시.</summary>
        protected virtual void UpdateClientVisuals()
        {
        }

        /// <summary>씬에 있는 모든 PlayerController 중 가장 가까운 대상을 찾는다. 협동(1~4인) 대상 몬스터라
        /// 매 프레임 전체 스캔이지만, 잡몹 수가 많아지면 그때 공간 분할 등으로 최적화한다.</summary>
        protected virtual Transform FindNearestPlayer()
        {
            // 한 번 발견하거나 맞으면 넓은 거리(aggroRange)로 계속 쫓는다 — 방 안 몬스터가 놓치지 않고 몰려온다.
            Transform target = FindNearestPlayerWithin(_aggroed ? config.aggroRange : config.detectRange * config.detectRangeScale);
            if (target != null)
            {
                _aggroed = true;
            }

            return target;
        }

        protected Transform FindNearestPlayerWithin(float range)
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            Transform nearest = null;
            float nearestDistance = range;

            foreach (PlayerController player in players)
            {
                if (IsDownedOrDead(player))
                {
                    continue; // 쓰러진(멀티)·사망한 플레이어는 추적·조준 대상에서 뺀다(2026-10-07 쓰러짐·부활)
                }

                float distance = Vector2.Distance(transform.position, player.transform.position);
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = player.transform;
                }
            }

            return nearest;
        }

        /// <summary>쓰러졌거나 사망한 플레이어인지(타깃 후보 제외용). 대미지는 PlayerHealth가 IsDead로 이미 막는다.</summary>
        protected static bool IsDownedOrDead(Component player)
        {
            var health = player != null ? player.GetComponent<PlayerHealth>() : null;
            return health != null && health.IsDead;
        }

        protected virtual State DecideState(Transform target)
        {
            if (target == null)
            {
                return State.Idle;
            }

            float distance = Vector2.Distance(transform.position, target.position);
            return distance <= config.attackRange ? State.Attack : State.Chase;
        }

        private void RunState()
        {
            switch (CurrentState)
            {
                case State.Chase:
                    ChaseTarget(CurrentTarget);
                    break;
                case State.Attack:
                    Rigidbody.linearVelocity = Vector2.zero;
                    TryAttack(CurrentTarget);
                    break;
                default:
                    Rigidbody.linearVelocity = Vector2.zero;
                    break;
            }
        }

        /// <summary>공격 범위 밖, 감지 범위 안일 때의 이동. 원거리 개체는 오히려 거리를 벌리는 식으로
        /// 오버라이드할 수 있다.</summary>
        protected virtual void ChaseTarget(Transform target)
        {
            Vector2 direction = ((Vector2)(target.position - transform.position)).normalized;
            Rigidbody.linearVelocity = direction * CurrentMoveSpeed;
        }

        private void TryAttack(Transform target)
        {
            if (_attackCooldownRemaining > 0f)
            {
                return;
            }

            // 바로 때리지 않고 방향을 고정한 채 예고부터 한다 — 판정은 예고가 끝나는 순간(OnWindupFinished → PerformAttack).
            _attackCooldownRemaining = config.attackCooldown * AbsorbRoguelike.Dungeon.DungeonGameManager.MonsterAttackCooldownScale; // 전체 난이도 배율(2026-10-06)
            OnAttackStarted(target);
            StartWindup(target, ((Vector2)(target.position - transform.position)).normalized, FirstWindupDuration);
        }

        /// <summary>새 공격(쿨다운 1회분)의 첫 예고 길이. 연속 공격 등은 짧게 오버라이드.</summary>
        protected virtual float FirstWindupDuration => config.windupDuration;

        /// <summary>새 공격을 시작할 때(첫 예고 직전, 서버) — 하위 클래스가 시퀀스 카운터 등을 초기화한다.</summary>
        protected virtual void OnAttackStarted(Transform target)
        {
        }

        /// <summary>예고 시작(서버). 방향 고정 + 예고 길이 동기화 + windupSerial 증가(모든 화면이 진하기를 처음부터 다시 그림).
        /// 다단 공격 중에 부르면 시퀀스를 끝내고 예고 상태로 넘어간다.</summary>
        protected void StartWindup(Transform target, Vector2 direction, float duration)
        {
            InAttackSequence = false;
            _windupTarget = target;
            duration *= AbsorbRoguelike.Dungeon.DungeonGameManager.MonsterWindupScale; // 전체 난이도 배율(2026-10-06) — windupLength에도 반영돼 예고 진하기가 맞는다
            _windupRemaining = duration;
            windupLength = duration;
            attackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : (attackDirection != Vector2.zero ? attackDirection : Vector2.down);
            unchecked
            {
                windupSerial++;
            }

            isWindingUp = true;
        }

        /// <summary>예고가 끝난 순간(서버). 기본 = 대상이 있으면 PerformAttack. 돌진 등은 여기서 시퀀스를 시작한다.
        /// 호출 직전에 RecoveryRemaining = recoveryDuration이 설정돼 있다.</summary>
        protected virtual void OnWindupFinished(Transform target)
        {
            if (target != null)
            {
                PerformAttack(target);
            }
        }

        /// <summary>다단 공격 한 프레임(서버, InAttackSequence일 때만). 넉백·경직 처리 뒤에 불린다.</summary>
        protected virtual void TickAttackSequence()
        {
            InAttackSequence = false;
        }

        /// <summary>죽어서 다단 공격이 끊겼을 때(서버) — 하위 클래스가 상태 SyncVar를 정리한다.</summary>
        protected virtual void OnAttackSequenceCancelled()
        {
        }

        /// <summary>예고가 끝난 순간의 실제 공격 판정. 기본은 예고했던 직사각형 범위 안에 대상이 남아 있을 때만
        /// 대미지를 준다(예고를 보고 빠져나가면 안 맞음). 투사체 등 다른 방식은 하위 클래스에서 오버라이드한다.</summary>
        protected virtual void PerformAttack(Transform target)
        {
            if (!IsInArea(target.position, attackDirection, config.attackAreaLength, config.attackAreaWidth, out _))
            {
                return;
            }

            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (health != null)
            {
                DealDamage(health, attackDamage);
            }

            Debug.Log($"[MonsterBehavior] {name} attacks {target.name} for {attackDamage}");
        }

        /// <summary>플레이어에게 대미지(서버). 체력이 실제로 깎였을 때만(가드·대시 무적·양털 보호막이면 false) 명중 효과
        /// (OnAttackHitPlayer)를 건다. knockbackOverride ≥ 0이면 이번 타격의 넉백 거리를 그 값으로 바꾼다(연속 공격 마지막 타 등).</summary>
        protected bool DealDamage(PlayerHealth health, int damage, float knockbackOverride = -1f)
        {
            // 층 대미지 배율(2026-10-13): 행동 고유 배율(갉기 버스트·끝부분 등)이 곱해진 최종값에 여기서 한 번만 곱한다.
            if (!isServer || health == null || !health.TryTakeDamage(AbsorbRoguelike.Dungeon.DifficultyScaling.MonsterDamage(damage), transform.position))
            {
                return false;
            }

            OnAttackHitPlayer(health, health.GetComponent<PlayerController>(), knockbackOverride);
            return true;
        }

        /// <summary>명중 효과 훅(서버, 실제 피해가 들어갔을 때만). 기본 = 부품 ⑥ 피격 둔화·넉백(설정값이 0이면 없음).</summary>
        protected virtual void OnAttackHitPlayer(PlayerHealth health, PlayerController player, float knockbackOverride)
        {
            if (player == null)
            {
                return;
            }

            if (config.onHitSlowMultiplier > 0f && config.onHitSlowDuration > 0f)
            {
                player.Motor.ServerApplySlow(config.onHitSlowMultiplier, config.onHitSlowDuration);
            }

            float distance = knockbackOverride >= 0f ? knockbackOverride : config.onHitKnockbackDistance;
            if (distance > 0f)
            {
                player.Motor.ServerApplyKnockback(transform.position, distance, config.onHitKnockbackTime);
            }
        }

        /// <summary>몸 중심에서 direction으로 뻗은 길이 length × 폭 width 직사각형 안인지(대상 반 칸 여유). forward = 앞쪽 거리.</summary>
        protected bool IsInArea(Vector2 point, Vector2 direction, float length, float width, out float forward)
        {
            const float targetHalfSize = 0.5f;
            Vector2 offset = point - (Vector2)transform.position;
            forward = Vector2.Dot(offset, direction);
            float side = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
            return forward >= -targetHalfSize
                && forward <= length + targetHalfSize
                && side <= width * 0.5f + targetHalfSize;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (config == null)
            {
                return; // 에디터 기즈모 — 에셋 연결 전
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, config.detectRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, config.attackRange);
        }
    }
}
