using System.Collections.Generic;
using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>머리 위 라벨의 경직/마비 표시(표시용 SyncVar). 마비가 걸려 있으면 마비가 우선.</summary>
    public enum StunDisplay : byte
    {
        None,
        Stagger,  // 경직 — 대지 0.3초
        Paralyze, // 마비 — 전기 방전 1초
    }

    /// <summary>
    /// 속성 부가 효과(GDD v2.14 "속성 × 동물 스킬 상호작용": 명중 1회당 효과, v2.18 확률 없는 속성 효과 원칙). 몬스터(MonsterStub)와
    /// 보스(BossStub) 공용이고 서버 권위로만 계산한다 — 클라이언트는 표시용 SyncVar만 읽어 오버레이·라벨을 그린다.
    /// 호출부: PlayerController.ApplyElementOnHit(명중한 대상 1마리당 1회). 그래서 동물 스킬의 타격 모양에 따라
    /// 결과가 달라진다(호랑이 3연타 = 화염 3스택, 뱀 360° = 주변 전원 1회씩).
    ///
    /// 효과(수치는 전부 [PLAYTEST]):
    /// - 화염: 스택 +1(최대 5), 스택당 초당 4, 3초 지속(새로 맞으면 갱신)
    /// - 독: 스택 +1(최대 10), 스택당 초당 2, 5초 지속(갱신)
    /// - 도트 틱 간격 0.5초(화염·독 공용) — 틱당 화염 스택당 2, 독 스택당 1.
    ///   (2026-09-23 플레이 피드백 결정 ⑤A: 초당 2/1·틱 1초 → 초당 4/2·틱 0.5초)
    /// - 냉기: 냉기 스택 +1(최대 3), 스택별 이동속도 0.8/0.6/0.4배, 3초 지속(갱신) — 3초가 지나면 스택 0.
    ///   MonsterBehaviorController/BossBrain.ApplySlow의 냉기 슬롯(SlowSource.Ice). 뱀 휘감기(Snake 슬롯)와는
    ///   서로 덮어쓰지 않고, 둘 다 걸리면 더 강한(낮은) 배율이 적용된다.
    /// - 대지: 0.3초 경직, 대상별 재경직 쿨다운 1초
    /// - 전기: 감전 스택 +1, 4초 지속(갱신). 3스택이 되는 순간 방전 — 고정 대미지 12 + 1초 마비(대지 경직과 같은 ApplyStun
    ///   슬롯, 긴 쪽 유지) 후 스택 0. 확률 판정·다른 적으로의 연쇄는 없다.
    /// 도트·방전 대미지는 PlayerHealth.OnDealtHit(적중 회복)을 부르지 않는다 — 회복은 직접 휘두른 공격에만.
    ///
    /// 보스에게는 흡수 스킬 명중으로만 속성 효과를 건다(맨몸 무기 명중은 PlayerController.ApplyElementOnHit에서 조기 반환).
    ///
    /// 시각 표시: 몸 SpriteRenderer.color를 직접 바꾸지 않고 자식 SpriteRenderer 오버레이를 덮는다(EliteMonsterMarker
    /// 외곽선과 같은 방식, 매 프레임 몸의 sprite/flipX를 복사). 몸 색은 이미 정예 주황 틴트(EliteMonsterMarker.Start),
    /// 시체 어둡게(MonsterStub.ApplyCorpseState), 보스 경직 파랑·회색 번쩍임(BossBrain.UpdateVisuals가 매 프레임
    /// 덮어씀)이 쓰고 있어서, 같은 color를 쓰면 서로 덮어쓰기 때문이다.
    /// 우선순위: 보스 회색 번쩍임(IsShowingDeflect 동안 오버레이 숨김) > 상태이상 오버레이 > 정예 틴트·보스 경직 파랑·보스 폭주 붉은
    /// 틴트(베이스 색). 보스 베이스 색 안에서는 경직 파랑 > 폭주 붉은 틴트(bossPhase 4, BossBrain.enrageTint). 보스에는 정예
    /// 주황 틴트가 붙지 않으므로 폭주 틴트와 충돌하지 않는다.
    /// 몬스터 공격 예고는 별도 스프라이트라 충돌하지 않는다. 죽었거나 시체면 오버레이를 끈다.
    /// 머리 위 라벨: 활성 상태 전부를 한 줄로(화상 N · 독 N · 감전 N/3 · 냉기 N · 경직/마비) 속성 색으로 그린다
    /// (표시용 SyncVar fireStacks/poisonStacks/shockStacks/iceStacks/stunDisplay). 도트·방전 대미지는 종류별 색으로
    /// 대미지 숫자(AbsorbRoguelike.UI.DamagePopup)를 띄운다.
    ///
    /// NetworkBehaviour라 프리팹에 미리 붙여둔다(런타임 AddComponent 금지 — Mirror가 NetworkIdentity 초기화 때
    /// NetworkBehaviour 목록을 고정하기 때문).
    /// </summary>
    public class StatusEffectReceiver : NetworkBehaviour
    {
        [Header("수치 (리팩터 C4 — Assets/Data/Combat/StatusEffectConfig.asset, 잡몹·보스 공용)")]
        [Tooltip("화상·독·냉기·대지·전기·취약 수치 에셋.")]
        [SerializeField] private AbsorbRoguelike.Combat.StatusEffectConfig statusConfig;

        [Header("표시")]
        [SerializeField] private float overlayAlpha = 0.45f; // [PLAYTEST]

        /// <summary>지금 활성인 상태 중 가장 최근에 걸린 것(없으면 None). 오버레이 색에만 쓴다.</summary>
        [SyncVar] private ElementType visibleStatus = ElementType.None;

        // 라벨 표시 전용 SyncVar. 서버 계산은 아래 private 필드가 하고, 값이 바뀔 때만 SyncStackDisplay가 옮겨 담는다
        // (불필요한 동기화 방지 — visibleStatus와 같은 패턴). 로직에서 읽지 않는다.
        [SyncVar] private int fireStacks;
        [SyncVar] private int poisonStacks;
        [SyncVar] private int shockStacks;
        [SyncVar] private int iceStacks;
        [SyncVar] private StunDisplay stunDisplay = StunDisplay.None;
        [SyncVar] private int vulnerableStacks;

        private MonsterStub _monster;
        private BossStub _boss;
        private AbsorbRoguelike.Combat.IStatusTarget _statusTarget; // 잡몹 MonsterBehaviorController 또는 보스 BossBrain(리팩터 B안 단계 2)
        private NetworkIdentity _lastAttacker;

        private int _fireStacks;
        private float _fireRemaining;
        private float _fireTickTimer;
        private float _fireAccum;

        private int _poisonStacks;
        private float _poisonRemaining;
        private float _poisonTickTimer;
        private float _poisonAccum;

        private int _iceStacks;
        private float _iceRemaining;

        private float _earthRemaining;   // 내가 건 경직(대지)의 남은 시간 — 표시용
        private float _earthCooldown;

        private int _shockStacks;
        private float _shockRemaining;
        private float _paralyzeRemaining; // 내가 건 마비(전기 방전)의 남은 시간 — 표시용

        private int _vulnerableStacks;
        private float _vulnerableRemaining;
        private float _vulnerableDamagePerStack; // 마지막으로 취약을 건 쥐 스킬의 값(RatSkillConfig)

        // 속성별 마지막으로 걸린 시각(Time.time) — visibleStatus 결정용. 인덱스 = (int)ElementType.
        private readonly float[] _appliedAt = new float[6];

        private SpriteRenderer _body;
        private SpriteRenderer _overlay;
        private StatusFxVisuals _fx;
        private TextMesh _stackLabel;
        private int _labelFire = -1;
        private int _labelPoison = -1;
        private int _labelShock = -1;
        private int _labelIce = -1;
        private StunDisplay _labelStun = (StunDisplay)255;
        private int _labelArmor = -1;
        private int _labelRegen = -1;
        private int _labelVulnerable = -1;
        private GameLanguage _labelLanguage; // 언어가 바뀌면 같은 스택이어도 글자를 다시 쓴다
        private readonly List<string> _labelParts = new List<string>(7);

        private const float Pixel = 1f / 16f;
        private const int StackLabelFontSize = 32;   // 32 × 0.1 × 0.1 ≈ 0.32유닛 ≈ 5px
        private const int StackLabelSortingOrder = 20; // 체력바(15/16)보다 위, 대미지 숫자(25)보다 아래

        public ElementType VisibleStatus => visibleStatus;
        public int FireStacks => fireStacks;
        public int PoisonStacks => poisonStacks;
        public int ShockStacks => shockStacks;
        public int IceStacks => iceStacks;
        public StunDisplay CurrentStunDisplay => stunDisplay;
        /// <summary>화상 스택이 1 이상인지(서버 계산값). 상황 특성 '재생' 정지 판정(BossStub.TickRegen)에 쓴다.</summary>
        public bool IsBurning => _fireStacks > 0;
        public int VulnerableStacks => vulnerableStacks;

        /// <summary>
        /// 쥐 갉기 '취약' 받는 대미지 배율(서버 계산값) = 1 + vulnerableDamagePerStack × 스택. MonsterStub/BossStub.TakeDamage가
        /// **직접 명중 대미지에만** 곱한다(DamagePopupKind.IsDirectHit — 화상·독 도트·전기 방전은 제외, 2026-09-29 결정: 도트까지
        /// 곱하면 쥐+화염 조합이 과하게 커지고, 스택이 명중으로 쌓이는 스킬이라 명중에 되돌려주는 게 직관적).
        /// </summary>
        public float VulnerableDamageMultiplier => 1f + _vulnerableDamagePerStack * _vulnerableStacks;

        /// <summary>쥐 갉기 명중 1회(서버 전용): 취약 스택 +1(최대 maxStacks), 지속 갱신. 호출부: RatGnawSkill.OnHitEnemy.
        /// 수치는 거는 쪽(RatSkillConfig, 2026-10-14 StatusEffectConfig에서 이동)이 넘긴다.</summary>
        public void AddVulnerableStack(int maxStacks, float duration, float damagePerStack)
        {
            if (!isServer || IsTargetDead)
            {
                return;
            }

            _vulnerableStacks = Mathf.Min(_vulnerableStacks + 1, Mathf.Max(1, maxStacks));
            _vulnerableRemaining = duration;
            _vulnerableDamagePerStack = damagePerStack;
            SyncStackDisplay();
        }

        private bool IsTargetDead => (_monster != null && _monster.IsDead) || (_boss != null && _boss.IsDefeated);

        private void Awake()
        {
            if (statusConfig == null)
            {
                // 리팩터 C4: 코드 기본값으로 대신 돌리지 않는다(AbsorbSkillHost 5c와 같은 방침).
                Debug.LogError($"[StatusEffectReceiver] {name}: statusConfig 참조가 비어 있다 — 프리팹의 StatusEffectReceiver에 Assets/Data/Combat/StatusEffectConfig 에셋을 연결해야 한다.", this);
            }

            _monster = GetComponent<MonsterStub>();
            _boss = GetComponent<BossStub>();
            // 인터페이스로 받은 값은 ?.가 Unity의 '가짜 null'을 못 거르므로, 없으면 진짜 null로 저장해 둔다(같은 오브젝트라 수명은 같다).
            var statusTarget = GetComponent<AbsorbRoguelike.Combat.IStatusTarget>();
            _statusTarget = statusTarget as Object != null ? statusTarget : null;
        }

        /// <summary>
        /// 속성 무기 명중 1회(대상 1마리당 1회) — 서버 전용. hitDamage는 현재 효과 계산에 쓰지 않는다(방전은 고정 대미지).
        /// 보스 맨몸 무기 명중 제외 규칙은 호출부(PlayerController.ApplyElementOnHit)가 담당한다.
        /// </summary>
        public void ApplyOnHit(ElementType element, int hitDamage, NetworkIdentity attacker)
        {
            if (!isServer || element == ElementType.None || IsTargetDead)
            {
                return;
            }

            if (attacker != null)
            {
                _lastAttacker = attacker;
            }

            switch (element)
            {
                case ElementType.Fire:
                    if (_fireStacks == 0)
                    {
                        _fireTickTimer = statusConfig.tickInterval;
                    }

                    _fireStacks = Mathf.Min(_fireStacks + 1, statusConfig.fireMaxStacks);
                    _fireRemaining = statusConfig.fireDuration;
                    MarkApplied(element);
                    break;

                case ElementType.Poison:
                    if (_poisonStacks == 0)
                    {
                        _poisonTickTimer = statusConfig.tickInterval;
                    }

                    _poisonStacks = Mathf.Min(_poisonStacks + 1, statusConfig.poisonMaxStacks);
                    _poisonRemaining = statusConfig.poisonDuration;
                    MarkApplied(element);
                    break;

                case ElementType.Ice:
                    ApplyIceStack();
                    break;

                case ElementType.Earth:
                    if (_earthCooldown > 0f)
                    {
                        break;
                    }

                    _statusTarget?.ApplyStun(statusConfig.earthStunDuration);
                    _earthCooldown = statusConfig.earthStunCooldown;
                    _earthRemaining = statusConfig.earthStunDuration;
                    MarkApplied(element);
                    break;

                case ElementType.Electric:
                    ApplyShockStack();
                    break;
            }

            if (IsTargetDead)
            {
                ClearAll(); // 방전으로 죽은 경우
                return;
            }

            RefreshVisibleStatus();
            SyncStackDisplay();
        }

        // 냉기 스택 +1(최대 = 배열 길이), 지속 갱신, 현재 스택 배율로 냉기 슬롯 둔화를 다시 건다(같은 슬롯이라 덮어씀).
        // 둔화 슬롯도 iceStackDuration으로 걸어서 스택 만료와 같은 시각에 배율이 복귀한다.
        private void ApplyIceStack()
        {
            if (statusConfig.iceSlowMultipliers == null || statusConfig.iceSlowMultipliers.Length == 0)
            {
                return;
            }

            _iceStacks = Mathf.Min(_iceStacks + 1, statusConfig.iceSlowMultipliers.Length);
            _iceRemaining = statusConfig.iceStackDuration;
            float multiplier = statusConfig.iceSlowMultipliers[_iceStacks - 1];
            _statusTarget?.ApplySlow(multiplier, statusConfig.iceStackDuration, SlowSource.Ice);
            MarkApplied(ElementType.Ice);
        }

        // 감전 스택 +1, 지속 갱신. electricDischargeStacks에 도달하는 순간 방전.
        private void ApplyShockStack()
        {
            _shockStacks++;
            _shockRemaining = statusConfig.electricStackDuration;
            MarkApplied(ElementType.Electric);
            if (_shockStacks >= statusConfig.electricDischargeStacks)
            {
                Discharge();
            }
        }

        // 방전: 고정 대미지 → (살아 있으면) 마비 → 스택 0. 대미지는 도트처럼 PlayerHealth.OnDealtHit을 부르지 않는다.
        private void Discharge()
        {
            _shockStacks = 0;
            _shockRemaining = 0f;

            if (_monster != null)
            {
                _monster.TakeDamage(statusConfig.electricDischargeDamage, DamagePopupKind.ElectricDischarge);
            }
            else if (_boss != null)
            {
                _boss.TakeDamage(statusConfig.electricDischargeDamage, _lastAttacker, DamageSource.AbsorbSkill, DamagePopupKind.ElectricDischarge);
            }

            Debug.Log($"[StatusEffect] {name} 전기 방전 {statusConfig.electricDischargeDamage}");

            if (IsTargetDead)
            {
                return;
            }

            // 대지 경직과 같은 슬롯(Mathf.Max — 남은 시간이 긴 쪽 유지).
            _statusTarget?.ApplyStun(statusConfig.electricParalyzeDuration);
            _paralyzeRemaining = Mathf.Max(_paralyzeRemaining, statusConfig.electricParalyzeDuration);
            MarkApplied(ElementType.Electric);
        }

        private void SyncStackDisplay()
        {
            if (fireStacks != _fireStacks)
            {
                fireStacks = _fireStacks;
            }

            if (poisonStacks != _poisonStacks)
            {
                poisonStacks = _poisonStacks;
            }

            if (shockStacks != _shockStacks)
            {
                shockStacks = _shockStacks;
            }

            if (iceStacks != _iceStacks)
            {
                iceStacks = _iceStacks;
            }

            if (vulnerableStacks != _vulnerableStacks)
            {
                vulnerableStacks = _vulnerableStacks;
            }

            StunDisplay stun = _paralyzeRemaining > 0f ? StunDisplay.Paralyze
                : _earthRemaining > 0f ? StunDisplay.Stagger
                : StunDisplay.None;
            if (stunDisplay != stun)
            {
                stunDisplay = stun;
            }
        }

        private void MarkApplied(ElementType element)
        {
            _appliedAt[(int)element] = Time.time;
        }

        private void Update()
        {
            if (!isServer)
            {
                return;
            }

            if (IsTargetDead)
            {
                ClearAll();
                return;
            }

            float dt = Time.deltaTime;
            if (_earthCooldown > 0f) _earthCooldown -= dt;
            if (_earthRemaining > 0f) _earthRemaining -= dt;
            if (_paralyzeRemaining > 0f) _paralyzeRemaining -= dt;

            if (_iceStacks > 0)
            {
                _iceRemaining -= dt;
                if (_iceRemaining <= 0f)
                {
                    _iceStacks = 0;
                }
            }

            if (_vulnerableStacks > 0)
            {
                _vulnerableRemaining -= dt;
                if (_vulnerableRemaining <= 0f)
                {
                    _vulnerableStacks = 0;
                }
            }

            if (_shockStacks > 0)
            {
                _shockRemaining -= dt;
                if (_shockRemaining <= 0f)
                {
                    _shockStacks = 0;
                }
            }

            TickDot(ref _fireStacks, ref _fireRemaining, ref _fireTickTimer, ref _fireAccum, statusConfig.fireDpsPerStack, dt, DamagePopupKind.FireDot);
            if (IsTargetDead)
            {
                ClearAll();
                return;
            }

            TickDot(ref _poisonStacks, ref _poisonRemaining, ref _poisonTickTimer, ref _poisonAccum, statusConfig.poisonDpsPerStack, dt, DamagePopupKind.PoisonDot);
            if (IsTargetDead)
            {
                ClearAll();
                return;
            }

            RefreshVisibleStatus();
            SyncStackDisplay(); // 지속 종료로 스택이 0이 된 경우 반영
        }

        // tickInterval(0.5초)마다 스택×DPS×간격만큼 준다 — 결정 ⑤A 수치로 틱당 화염 스택당 2(4×0.5), 독 스택당 1(2×0.5).
        // 소수는 누적해 정수 부분만 적용한다. 지속이 끝나면 스택이 사라진다.
        private void TickDot(ref int stacks, ref float remaining, ref float tickTimer, ref float accum, float dpsPerStack, float dt, DamagePopupKind kind)
        {
            if (stacks <= 0)
            {
                return;
            }

            remaining -= dt;
            tickTimer -= dt;
            if (tickTimer <= 0f)
            {
                tickTimer += statusConfig.tickInterval;
                accum += stacks * dpsPerStack * statusConfig.tickInterval;
                int damage = Mathf.FloorToInt(accum);
                accum -= damage;
                if (damage > 0)
                {
                    DealDotDamage(damage, kind);
                }
            }

            if (remaining <= 0f)
            {
                stacks = 0;
                accum = 0f;
            }
        }

        // 몬스터는 MonsterStub.TakeDamage, 보스는 마지막으로 효과를 건 플레이어 기준으로 BossStub.TakeDamage(개인 할당량 로직 그대로).
        // kind는 대미지 숫자 색만 정한다(화상 주황·중독 보라).
        private void DealDotDamage(int amount, DamagePopupKind kind)
        {
            if (_monster != null)
            {
                _monster.TakeDamage(amount, kind);
            }
            else if (_boss != null)
            {
                _boss.TakeDamage(amount, _lastAttacker, DamageSource.AbsorbSkill, kind);
            }
        }

        private void ClearAll()
        {
            _fireStacks = 0;
            _fireRemaining = 0f;
            _fireAccum = 0f;
            _poisonStacks = 0;
            _poisonRemaining = 0f;
            _poisonAccum = 0f;
            _iceStacks = 0;
            _iceRemaining = 0f;
            _earthRemaining = 0f;
            _earthCooldown = 0f;
            _shockStacks = 0;
            _shockRemaining = 0f;
            _paralyzeRemaining = 0f;
            _vulnerableStacks = 0;
            _vulnerableRemaining = 0f;
            if (visibleStatus != ElementType.None)
            {
                visibleStatus = ElementType.None;
            }

            SyncStackDisplay();
        }

        private void RefreshVisibleStatus()
        {
            ElementType best = ElementType.None;
            float bestTime = float.MinValue;
            Consider(ElementType.Fire, _fireStacks > 0, ref best, ref bestTime);
            Consider(ElementType.Poison, _poisonStacks > 0, ref best, ref bestTime);
            Consider(ElementType.Ice, _iceStacks > 0, ref best, ref bestTime);
            Consider(ElementType.Earth, _earthRemaining > 0f, ref best, ref bestTime);
            Consider(ElementType.Electric, _shockStacks > 0 || _paralyzeRemaining > 0f, ref best, ref bestTime);

            if (visibleStatus != best)
            {
                visibleStatus = best;
            }
        }

        private void Consider(ElementType element, bool active, ref ElementType best, ref float bestTime)
        {
            if (active && _appliedAt[(int)element] >= bestTime)
            {
                best = element;
                bestTime = _appliedAt[(int)element];
            }
        }

        private void Start()
        {
            // 오버레이는 모든 접속자 화면에 그려야 하므로 서버 체크 없이 각자 만든다.
            _body = GetComponent<SpriteRenderer>();
            if (_body == null)
            {
                return;
            }

            var go = new GameObject("StatusOverlay");
            go.transform.SetParent(_body.transform, false);
            _overlay = go.AddComponent<SpriteRenderer>();
            _overlay.sortingLayerID = _body.sortingLayerID;
            _overlay.sortingOrder = _body.sortingOrder + 1;
            _overlay.enabled = false;

            // 속성 상태 도트 이펙트(불꽃·거품·서리·전기·기절 별, 2026-10-07) — 각 화면 로컬.
            _fx = gameObject.AddComponent<StatusFxVisuals>();
            _fx.Init(_body);

            // 상태 라벨 — 오버레이처럼 각 클라이언트가 로컬로 만든다. DamagePopup과 같은 내장 폰트·TextMesh.
            // 위치: 몸 스프라이트 top + 7px(로컬) — WorldHealthBar(top+2px, 높이 4px) 바로 위. 부모 스케일을 상쇄해 글자 크기를
            // 일정하게 둔다(flipX는 렌더러 속성이라 자식 트랜스폼에 영향 없음 → 항상 정방향).
            float top = _body.sprite != null ? _body.sprite.bounds.max.y : 0.5f;
            _stackLabel = DamagePopup.CreateText("StackLabel", transform, StackLabelFontSize, StackLabelSortingOrder);
            _stackLabel.transform.localPosition = new Vector3(0f, top + 7f * Pixel, 0f);
            Vector3 lossy = transform.lossyScale;
            _stackLabel.transform.localScale = new Vector3(
                lossy.x != 0f ? 1f / lossy.x : 1f,
                lossy.y != 0f ? 1f / lossy.y : 1f,
                1f);
            _stackLabel.gameObject.SetActive(false);
        }

        // 머리 위 라벨: 활성 상태 전부를 " · "로 이어 한 줄로 그린다. 텍스트는 값이 바뀔 때만 다시 만든다.
        private void UpdateStackLabel()
        {
            if (_stackLabel == null)
            {
                return;
            }

            // 보스 상황 특성(갑옷 남은 양·재생 상태)은 상태이상 앞에 붙인다. 보스가 아니면 둘 다 0이라 기존 동작 그대로.
            int armor = _boss != null ? _boss.CurrentArmor : 0;
            byte regen = _boss != null ? _boss.RegenState : (byte)0;
            bool any = fireStacks > 0 || poisonStacks > 0 || shockStacks > 0 || iceStacks > 0 || stunDisplay != StunDisplay.None
                       || armor > 0 || regen != 0 || vulnerableStacks > 0;
            bool show = _body.enabled && !IsTargetDead && any;
            if (_stackLabel.gameObject.activeSelf != show)
            {
                _stackLabel.gameObject.SetActive(show);
            }

            if (!show
                || (fireStacks == _labelFire && poisonStacks == _labelPoison && shockStacks == _labelShock
                    && iceStacks == _labelIce && stunDisplay == _labelStun && armor == _labelArmor && regen == _labelRegen
                    && vulnerableStacks == _labelVulnerable && Loc.Current == _labelLanguage))
            {
                return;
            }

            _labelFire = fireStacks;
            _labelPoison = poisonStacks;
            _labelShock = shockStacks;
            _labelIce = iceStacks;
            _labelStun = stunDisplay;
            _labelArmor = armor;
            _labelRegen = regen;
            _labelVulnerable = vulnerableStacks;
            _labelLanguage = Loc.Current;

            _labelParts.Clear();
            if (armor > 0) _labelParts.Add($"<color=#D9D9D9>{Loc.Get("status.armor", armor)}</color>");
            if (regen == 1) _labelParts.Add($"<color=#66E07A>{Loc.Get("status.regen")}</color>");
            else if (regen == 2) _labelParts.Add($"<color=#9A9A9A>{Loc.Get("status.regen_stopped")}</color>");
            if (vulnerableStacks > 0) _labelParts.Add($"<color=#C8A2FF>{Loc.Get("status.vulnerable", vulnerableStacks)}</color>");
            if (fireStacks > 0) _labelParts.Add(Colored(ElementType.Fire, Loc.Get("status.burn", fireStacks)));
            if (poisonStacks > 0) _labelParts.Add(Colored(ElementType.Poison, Loc.Get("status.poison", poisonStacks)));
            if (shockStacks > 0) _labelParts.Add(Colored(ElementType.Electric, Loc.Get("status.shock", shockStacks, statusConfig.electricDischargeStacks)));
            if (iceStacks > 0) _labelParts.Add(Colored(ElementType.Ice, Loc.Get("status.chill", iceStacks)));
            if (stunDisplay == StunDisplay.Paralyze) _labelParts.Add(Colored(ElementType.Electric, Loc.Get("status.paralyze")));
            else if (stunDisplay == StunDisplay.Stagger) _labelParts.Add(Colored(ElementType.Earth, Loc.Get("status.stagger")));
            _stackLabel.text = string.Join(" · ", _labelParts);
        }

        private static string Colored(ElementType element, string text)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(element.StatusTintColor())}>{text}</color>";
        }

        // 속성이 걸리는 순간 속성 색 스파크(2026-10-07, HitFx) — 동기화된 스택이 오르거나 경직·마비가 새로 걸릴 때. 각 화면이 로컬로 띄운다.
        // 첫 프레임은 기준값만 잡는다(늦게 접속한 화면에서 이미 걸린 상태로 스파크가 터지지 않게). 최대 스택에서 다시 맞으면 오르지 않아 생략된다.
        private bool _fxPrimed;
        private int _fxFire, _fxPoison, _fxShock, _fxIce;
        private StunDisplay _fxStun;

        private void UpdateStatusGainFx()
        {
            if (_fxPrimed && !IsTargetDead)
            {
                if (fireStacks > _fxFire) AbsorbRoguelike.UI.HitFx.OnElementApplied(gameObject, ElementType.Fire);
                if (poisonStacks > _fxPoison) AbsorbRoguelike.UI.HitFx.OnElementApplied(gameObject, ElementType.Poison);
                if (iceStacks > _fxIce) AbsorbRoguelike.UI.HitFx.OnElementApplied(gameObject, ElementType.Ice);
                if (shockStacks > _fxShock) AbsorbRoguelike.UI.HitFx.OnElementApplied(gameObject, ElementType.Electric);
                if (stunDisplay != _fxStun && stunDisplay == StunDisplay.Stagger) AbsorbRoguelike.UI.HitFx.OnElementApplied(gameObject, ElementType.Earth);
                if (stunDisplay != _fxStun && stunDisplay == StunDisplay.Paralyze) AbsorbRoguelike.UI.HitFx.OnElementApplied(gameObject, ElementType.Electric, 1.3f); // 방전

                // 효과음(2026-10-14): 스파크와 달리 '새로 걸림'(0 → 1 이상, 경직은 새로 걸릴 때)에만 — 스택이 쌓일 때마다 울리지 않게.
                // 여러 대상에 한꺼번에 걸리면 SfxLibrary의 minInterval·maxConcurrent가 줄여 준다.
                Vector3 at = transform.position;
                if (fireStacks > 0 && _fxFire == 0) AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.ElemFire, at);
                if (poisonStacks > 0 && _fxPoison == 0) AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.ElemPoison, at);
                if (iceStacks > 0 && _fxIce == 0) AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.ElemIce, at);
                if ((shockStacks > 0 && _fxShock == 0) || (stunDisplay != _fxStun && stunDisplay == StunDisplay.Paralyze)) AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.ElemElectric, at);
                if (stunDisplay != _fxStun && stunDisplay == StunDisplay.Stagger) AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.ElemEarth, at);
            }

            _fxPrimed = true;
            _fxFire = fireStacks;
            _fxPoison = poisonStacks;
            _fxShock = shockStacks;
            _fxIce = iceStacks;
            _fxStun = stunDisplay;
        }

        private void LateUpdate()
        {
            UpdateStackLabel();
            UpdateStatusGainFx();
            if (_fx != null)
            {
                _fx.Tick(fireStacks, poisonStacks, iceStacks, shockStacks, stunDisplay, IsTargetDead);
            }

            if (_overlay == null)
            {
                return;
            }

            bool deflecting = _boss != null && _boss.IsShowingDeflect; // 회색 번쩍임이 최우선
            bool show = _body.enabled && !IsTargetDead && !deflecting && visibleStatus != ElementType.None;
            _overlay.enabled = show;
            if (!show)
            {
                return;
            }

            _overlay.sprite = _body.sprite;
            _overlay.flipX = _body.flipX;
            _overlay.flipY = _body.flipY;
            Color tint = visibleStatus.StatusTintColor();
            tint.a = overlayAlpha;
            _overlay.color = tint;
        }
    }
}
