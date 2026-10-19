using System.Collections.Generic;
using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Player;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 1기 최소 스텁. GDD 흡수 계층 구조: 보스는 잡몹처럼 "살아있는 채로 바로 흡수"가 아니라
    /// "처치 후 남은 시체를 흡수"하는 대상이다. 그래서 MonsterStub과 달리 체력이 0이 돼도 즉시
    /// 파괴하지 않고 IsDefeated 상태로 남겨 흡수를 기다린다(시체 = 흡수 가능 상태).
    /// 보스가 제시하는 속성 2종은 ① 이 스테이지 속성 + ② 나머지 중 무작위 1개(GDD v2.14). ①은 스폰 시점에
    /// FloorProgression.CurrentFloorElement로 정하고, ②는 흡수한 플레이어별로 PrepareOfferFor에서 정한다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(NetworkIdentity))]
    public class BossStub : NetworkBehaviour, AbsorbRoguelike.Combat.IDamageable
    {
        // 리팩터 C4: 설정값은 statsConfig.maxHealth(Assets/Data/Boss/Ogre_Stats.asset) — Awake에서 넣는다(예전 직렬화 값과 같은 시점·값).
        [SyncVar] private int maxHealth; // SyncVar: 서버에서 바뀌는 최대 체력을 참가자 체력바에도 반영

        [Header("수치 (리팩터 C4 — Assets/Data/Boss/<보스>_Stats.asset)")]
        [Tooltip("체력·개인 할당량·맨몸 무기 대미지·갑옷·재생 수치 에셋.")]
        [SerializeField] private BossStatsConfig statsConfig;

        [Header("보스 대미지 표시")]
        [SerializeField] private float deflectFlashDuration = 0.12f;

        private float _deflectFlashRemaining;
        private float _regenAccum;
        private StatusEffectReceiver _status;

        /// <summary>갑옷 남은 양(표시용 SyncVar — 판정은 서버가 이 값으로 한다). 갑옷 특성이 아니면 0.</summary>
        [SyncVar] private int currentArmor;
        /// <summary>재생 표시 상태: 0 없음 / 1 재생 중 / 2 재생 정지(화상). 표시 전용.</summary>
        [SyncVar] private byte regenState;

        public int CurrentArmor => currentArmor;
        public byte RegenState => regenState;

        /// <summary>무기 공격이 막힌 직후 잠깐 true — 보스 외형(BossBrain)이 회색으로 번쩍이는 데 쓴다.</summary>
        public bool IsShowingDeflect => _deflectFlashRemaining > 0f;

        [SyncVar] private int currentHealth;
        [SyncVar] private bool isDefeated;

        private readonly Dictionary<uint, int> _remainingQuota = new Dictionary<uint, int>();
        private bool _quotaInitialized;
        private int _supportGauge;

        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;
        public bool IsDefeated => isDefeated;

        // IDamageable(리팩터 B안 단계 1, 2026-10-08): 보스의 '죽음' = 처치됨.
        bool AbsorbRoguelike.Combat.IDamageable.IsDead => isDefeated;

        /// <summary>
        /// IDamageable 진입점(서버 전용). 기존 TakeDamage에 그대로 넘긴다 — 맨손 무기 1 고정·BossDeflect 팝업·취약·갑옷·개인 할당량은
        /// TakeDamage 안 규칙 그대로. 반환: 체력이 실제로 줄었는가(개인 할당량 초과분·처치 뒤 타격은 false).
        /// </summary>
        public bool ServerTakeHit(in AbsorbRoguelike.Combat.HitInfo hit)
        {
            int hpBefore = currentHealth;
            TakeDamage(hit.Amount, hit.Attacker, hit.Source, hit.Kind,
                isOxCharge: (hit.Flags & AbsorbRoguelike.Combat.HitFlags.OxCharge) != 0,
                isRatGnaw: (hit.Flags & AbsorbRoguelike.Combat.HitFlags.RatGnaw) != 0);
            return currentHealth < hpBefore;
        }

        private readonly System.Collections.Generic.HashSet<uint> _absorbedBy = new System.Collections.Generic.HashSet<uint>();

        // 흡수한 플레이어 netId(모든 화면 동기화, 2026-10-07): 흡수한 사람 화면에서는 시체를 바로 감춘다(속성 선택 창이 뜰 때 이미 안 보이게).
        // 파티원이 아직 흡수하지 않았으면 그 사람 화면에는 남는다. 오브젝트는 지우지 않는다 — 출구(FloorExit)가 이 보스의 IsDefeated·HasAbsorbed를 계속 읽는다.
        private readonly SyncList<uint> absorbedNetIds = new SyncList<uint>();
        private bool _corpseHidden;

        // ① 이 스테이지 속성(서버 전용). OnStartServer에서 정한다.
        private ElementType _stageElement = ElementType.None;

        // 흡수한 플레이어(netId)별 선택지 {①, ②} 캐시. 서버 로컬 필드라 원격 클라이언트에는 동기화하지 않는다 —
        // PendingBossChoice와 같은 호스트 기준 한계이고, 멀티 동기화는 이번 범위 밖이다(ADR-003).
        private readonly Dictionary<uint, ElementType[]> _offersByAbsorber = new Dictionary<uint, ElementType[]>();

        private void Awake()
        {
            // 예전 직렬화 값이 들어 있던 것과 같게 모든 화면에서 Awake 첫 줄에 넣는다(원격은 이후 SyncVar로 덮어씀).
            if (statsConfig != null)
            {
                maxHealth = statsConfig.maxHealth;
            }
            else
            {
                // 코드 기본값으로 대신 돌리지 않는다(AbsorbSkillHost 5c와 같은 방침).
                Debug.LogError($"[BossStub] {name}: statsConfig 참조가 비어 있다 — 프리팹의 BossStub에 Assets/Data/Boss 스탯 에셋을 연결해야 한다.", this);
            }

            currentHealth = maxHealth;
            AbsorbRoguelike.UI.GroundShadow.Attach(gameObject); // 발밑 타원 그림자(2026-10-08)
        }

        public override void OnStartServer()
        {
            var dungeon = AbsorbRoguelike.Dungeon.FloorProgression.Instance;
            if (dungeon != null && dungeon.CurrentFloorElement != ElementType.None)
            {
                _stageElement = dungeon.CurrentFloorElement;
            }
            else
            {
                // 매니저가 없는 테스트 씬: 5속성 중 무작위.
                _stageElement = ElementTypeExtensions.AllElements[Random.Range(0, ElementTypeExtensions.AllElements.Length)];
            }

            Debug.Log($"[BossStub] {name} stage element: {_stageElement.DisplayName()}");

            // 층 체력 배율(2026-10-13): 층 배율만 — 인원 배율은 제외(보스는 인원별 개인 할당량 규칙). 할당량(EnsureQuotaInitialized)은
            // 첫 피격 때 이 배율이 적용된 maxHealth로 나누고, 재생 상한·DebugSetHealthPercent도 같은 maxHealth를 쓴다.
            // 상황 특성(ApplySituation)은 갑옷·재생만 정하고 maxHealth를 바꾸지 않으므로 순서 영향 없음.
            maxHealth = DifficultyScaling.MonsterHealth(maxHealth, includeParty: false);
            currentHealth = maxHealth;

            ApplySituation(dungeon != null ? dungeon.CurrentBossSituation : AbsorbRoguelike.Dungeon.BossSituation.None);
        }

        /// <summary>
        /// 상황 특성 적용(서버). 스폰 시(OnStartServer)와 디버그 메뉴 교체(FloorProgression.DebugSetBossSituation) 때 부른다.
        /// 갑옷이면 갑옷을 가득 채우고(교체 시에도 다시 가득), 아니면 0. 재생은 Update가 매 프레임 특성을 직접 읽는다.
        /// 연타 휘두르기는 BossPatternSwing이 타격 시점에 직접 읽는다.
        /// </summary>
        [Server]
        public void ApplySituation(AbsorbRoguelike.Dungeon.BossSituation situation)
        {
            currentArmor = situation == AbsorbRoguelike.Dungeon.BossSituation.Armored ? statsConfig.armorMax : 0;
            _regenAccum = 0f;
            Debug.Log($"[BossStub] {name} situation: {situation.DisplayName()} (armor {currentArmor})");
        }

        /// <summary>
        /// 보스 흡수 시점에 흡수한 플레이어 기준으로 ②를 정한다. 플레이어마다 지금 든 속성이 달라 스폰 시점에는
        /// 제외 대상을 알 수 없기 때문이다(GDD Content Design: 지금 든 속성은 ②에서 제외). ①은 지금 든 속성과
        /// 같아도 항상 제시한다(GDD 보스=스테이지 속성 대표 규칙). 이미 이 플레이어의 선택지가 있으면 그대로 돌려준다.
        /// 호출부: AbsorptionController.AbsorbBoss(속성이 바뀌기 전 값으로 호출해야 한다).
        /// </summary>
        public ElementType[] PrepareOfferFor(NetworkIdentity absorber, ElementType absorberCurrent)
        {
            if (absorber == null)
            {
                return System.Array.Empty<ElementType>();
            }

            if (_offersByAbsorber.TryGetValue(absorber.netId, out ElementType[] cached))
            {
                return cached;
            }

            var candidates = new List<ElementType>();
            foreach (ElementType element in ElementTypeExtensions.AllElements)
            {
                if (element != _stageElement && element != absorberCurrent)
                {
                    candidates.Add(element);
                }
            }

            ElementType second = candidates[Random.Range(0, candidates.Count)]; // 5속성 - 최대 2개 제외 → 항상 3개 이상
            var offer = new[] { _stageElement, second };
            _offersByAbsorber[absorber.netId] = offer;
            return offer;
        }

        /// <summary>PrepareOfferFor로 정해둔 이 플레이어의 선택지 {①, ②}. 아직 없으면 빈 배열.</summary>
        public ElementType[] GetOfferedElementsFor(NetworkIdentity absorber)
        {
            return absorber != null && _offersByAbsorber.TryGetValue(absorber.netId, out ElementType[] offer)
                ? offer
                : System.Array.Empty<ElementType>();
        }

        private void Update()
        {
            if (_deflectFlashRemaining > 0f)
            {
                _deflectFlashRemaining -= Time.deltaTime;
            }

            if (isServer)
            {
                TickRegen(Time.deltaTime);
            }
        }

        /// <summary>
        /// 상황 특성 '재생'(서버): 초당 maxHealth × regenPercentPerSecond%를 누적해 정수만큼 currentHealth에 더한다(maxHealth 상한).
        /// 화상 스택이 하나라도 있으면(StatusEffectReceiver.IsBurning) 멈춘다. 처치된 뒤에는 동작하지 않는다.
        /// 개인 할당량 모드(usesPersonalQuota, 현재 보스 프리팹은 꺼짐): 보스 전체 체력만 회복하고 _remainingQuota는 건드리지 않는다.
        /// 다만 할당량을 다 채운 플레이어의 대미지는 보스 체력에 반영되지 않으므로(GDD '보스 개인 할당량'), 남은 할당량 합을 넘게
        /// 회복하면 아무도 잡을 수 없는 보스가 된다 — 그래서 할당량 모드에서는 회복 상한을 min(maxHealth, 남은 할당량 합)으로 둔다.
        /// (할당량 모드의 재생 규칙 자체는 기획 미정 — 재생이 사실상 막히는 것이 현재 동작.)
        /// </summary>
        private void TickRegen(float dt)
        {
            var dungeon = AbsorbRoguelike.Dungeon.FloorProgression.Instance;
            bool regenerating = dungeon != null && dungeon.CurrentBossSituation == AbsorbRoguelike.Dungeon.BossSituation.Regenerating
                                && !isDefeated && currentHealth > 0;
            if (!regenerating)
            {
                SetRegenState(0);
                _regenAccum = 0f;
                return;
            }

            if (_status == null)
            {
                _status = GetComponent<StatusEffectReceiver>();
            }

            if (_status != null && _status.IsBurning)
            {
                SetRegenState(2);
                _regenAccum = 0f;
                return;
            }

            SetRegenState(1);
            int cap = maxHealth;
            if (statsConfig.usesPersonalQuota && _quotaInitialized)
            {
                int quotaSum = 0;
                foreach (int remaining in _remainingQuota.Values)
                {
                    quotaSum += Mathf.Max(0, remaining);
                }

                cap = Mathf.Min(cap, quotaSum);
            }

            if (currentHealth >= cap)
            {
                _regenAccum = 0f;
                return;
            }

            _regenAccum += maxHealth * statsConfig.regenPercentPerSecond * 0.01f * dt;
            int heal = Mathf.FloorToInt(_regenAccum);
            if (heal > 0)
            {
                _regenAccum -= heal;
                currentHealth = Mathf.Min(cap, currentHealth + heal);
            }
        }

        private void SetRegenState(byte state)
        {
            if (regenState != state)
            {
                regenState = state;
            }
        }

        /// <summary>
        /// 디버그 메뉴(F1): 살아 있는 보스의 체력을 1로 만든다(서버, SyncVar라 체력바에 반영). 처치는 플레이어가
        /// 직접 한 대 쳐서 기존 처치·시체·출구 흐름을 그대로 타게 한다.
        /// </summary>
        [Server]
        public void DebugSetHealthToOne()
        {
            if (isDefeated || currentHealth <= 0)
            {
                return;
            }

            currentHealth = 1;
            Debug.Log($"[BossStub] {name} debug health set to 1");
        }

        /// <summary>
        /// 디버그 메뉴(F1): 살아 있는 보스의 체력을 최대 체력의 ratio배(최소 1)로 만든다 — 페이즈 전환(BossBrain) 확인용.
        /// DebugSetHealthToOne과 같이 처치는 하지 않는다. 개인 할당량 모드(usesPersonalQuota)에서도 보스 전체 체력(currentHealth)만
        /// 바꾸고 _remainingQuota는 건드리지 않는다(할당량 합과 체력이 어긋날 수 있다 — 디버그 전용).
        /// </summary>
        [Server]
        public void DebugSetHealthPercent(float ratio)
        {
            if (isDefeated || currentHealth <= 0)
            {
                return;
            }

            currentHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * ratio));
            Debug.Log($"[BossStub] {name} debug health set to {currentHealth}/{maxHealth} ({ratio:P0})");
        }

        [ClientRpc]
        private void RpcShowDeflect()
        {
            _deflectFlashRemaining = deflectFlashDuration;
        }

        /// <summary>
        /// GDD '보스 개인 할당량 & 지원 전환' 기믹(결정 대기 체크리스트 G, 2026-09-17 확정 규칙:
        /// 초과 대미지 1:1로 지원 게이지 전환, 임계값 도달 시 아직 할당량을 못 채운 파티원 전체에게
        /// 즉시 회복). usesPersonalQuota가 꺼져 있으면 기존처럼 공유 체력풀로 그대로 동작한다.
        /// </summary>
        /// <param name="kind">대미지 숫자 색(결정 ④B). 맨몸 무기(DamageSource.Weapon)는 항상 BossDeflect(회색)로 바꾼다.
        /// 표시값은 무기 1 제한 적용 후의 amount로 통일한다 — 개인 할당량(usesPersonalQuota) 분기에서 초과분이 보스
        /// 체력에 반영되지 않더라도 숫자는 준 대미지 그대로 보여준다(배분 규칙은 변경 없음).</param>
        /// <param name="isOxCharge">소 돌진(OxKnockbackSkill 돌진) 명중이면 true — 상황 특성 '갑옷'에서 갑옷을 크게 깎는다.</param>
        /// <param name="isRatGnaw">쥐 갉기 명중이면 true — 갑옷 상황 특성에서 갑옷을 armorBreakRatExtra만큼 더 깎는다.</param>
        /// <remarks>플레이어 공격은 IDamageable.ServerTakeHit(HitResolver)을 거쳐 여기로 온다(리팩터 B안 단계 1). 도트·방전·뱉기는 직접 호출.</remarks>
        public void TakeDamage(int amount, NetworkIdentity attacker, DamageSource source, DamagePopupKind kind = DamagePopupKind.Normal, bool isOxCharge = false, bool isRatGnaw = false)
        {
            if (!isServer || isDefeated)
            {
                return; // 대미지 적용은 서버(호스트)만 한다 — ADR-003
            }

            // 도트·방전은 직접 명중이 아니므로 갑옷을 깎지 않는다(대미지 감소만 받는다). 귀 채찍 끝부분/근접(EarWhipTip/Close)은
            // 직접 명중이다(2026-09-28, 결정 ㉑A) — DamagePopupKindExtensions.IsDirectHit.
            bool directHit = kind.IsDirectHit();

            if (source == DamageSource.Weapon)
            {
                amount = Mathf.Min(amount, statsConfig.weaponDamageToBoss);
                RpcShowDeflect();
                kind = DamagePopupKind.BossDeflect;
            }
            else if (directHit)
            {
                // 쥐 갉기 '취약'(2026-09-29): 직접 명중에만 배율(도트·방전 제외). 맨몸 무기 1 고정은 위에서 끝나 배율을 받지 않는다.
                // 갑옷 감소(ApplyArmor)보다 먼저 곱한다.
                if (_status == null)
                {
                    _status = GetComponent<StatusEffectReceiver>();
                }

                if (_status != null && _status.VulnerableStacks > 0)
                {
                    amount = Mathf.Max(1, Mathf.RoundToInt(amount * _status.VulnerableDamageMultiplier));
                }
            }

            if (currentArmor > 0)
            {
                ApplyArmor(ref amount, source, isOxCharge, directHit, isRatGnaw);
            }

            ShowDamagePopup(amount, kind);

            if (statsConfig.usesPersonalQuota)
            {
                ApplyQuotaDamage(amount, attacker);
            }
            else
            {
                currentHealth = Mathf.Max(0, currentHealth - amount);
            }

            Debug.Log($"[BossStub] {name} took {amount} damage ({currentHealth}/{maxHealth})");

            if (currentHealth <= 0)
            {
                isDefeated = true;
                Debug.Log($"[BossStub] {name} defeated — corpse ready for absorption");
            }
        }

        /// <summary>
        /// 상황 특성 '갑옷'(GDD ⑮A, 서버): 갑옷이 남아 있으면 이번 대미지를 armorDamageScale배(올림, 최소 1)로 줄이고, 직접 명중이면
        /// 출처별로 갑옷을 깎는다 — 소 돌진 armorBreakOxCharge, 뱉기 armorBreakSpit, 그 외 흡수 스킬 armorBreakAbsorbSkill, 맨몸 무기 0.
        /// 대미지 감소는 이번 타격(갑옷을 깨는 타격 포함)에 적용되고, 갑옷이 0이 된 다음 타격부터 원래 대미지가 들어간다.
        /// 무기 1 고정은 이 앞에서 이미 적용된다(1 × 0.4 → 올림 1이라 무기 대미지는 그대로 1).
        /// </summary>
        private void ApplyArmor(ref int amount, DamageSource source, bool isOxCharge, bool directHit, bool isRatGnaw)
        {
            amount = Mathf.Max(1, Mathf.CeilToInt(amount * statsConfig.armorDamageScale));

            int armorLoss = !directHit || source == DamageSource.Weapon ? 0
                : isOxCharge ? statsConfig.armorBreakOxCharge
                : source == DamageSource.Spit ? statsConfig.armorBreakSpit
                : statsConfig.armorBreakAbsorbSkill;
            if (isRatGnaw && directHit && source != DamageSource.Weapon)
            {
                armorLoss += statsConfig.armorBreakRatExtra; // 쥐: 흡수 스킬 기본 감소 + 추가 감소
            }

            if (armorLoss <= 0)
            {
                return;
            }

            currentArmor = Mathf.Max(0, currentArmor - armorLoss);
            if (currentArmor == 0)
            {
                Debug.Log($"[BossStub] {name} armor broken");
                ShowTextPopup(Loc.Pack("popup.armor_break"), new Color(1f, 0.85f, 0.3f)); // 키로 보내 받는 쪽 언어로(DamagePopup.SpawnText가 Unpack)
            }
        }

        // 글자 팝업("갑옷 파괴!") — ShowDamagePopup과 같은 경로(호스트는 바로, 원격 클라이언트는 RPC). 대미지 숫자보다 조금 위.
        private void ShowTextPopup(string message, Color color)
        {
            var sprite = GetComponent<SpriteRenderer>();
            float top = sprite != null && sprite.sprite != null ? sprite.sprite.bounds.max.y : 0.5f;
            Vector3 pos = transform.TransformPoint(new Vector3(0f, top + 20f * DamagePopup.Pixel, 0f));
            if (isClient)
            {
                DamagePopup.SpawnText(pos, message, color);
            }

            RpcShowText(message, color, pos);
        }

        [ClientRpc]
        private void RpcShowText(string message, Color color, Vector3 pos)
        {
            if (isServer)
            {
                return;
            }

            DamagePopup.SpawnText(pos, message, color);
        }

        // 대미지 숫자(결정 ④B) — 서버에서만 호출. MonsterStub.ShowDamagePopup과 같은 방식: 호스트는 바로 띄우고,
        // 원격 클라이언트는 RpcShowDamage로 받는다(호스트는 RPC 핸들러에서 중복 표시를 건너뜀).
        private void ShowDamagePopup(int amount, DamagePopupKind kind)
        {
            if (amount <= 0)
            {
                return;
            }

            // 몸 스프라이트 윗부분 + 12px(로컬 기준, WorldHealthBar와 같은 계산) — 체력바·스택 라벨 위.
            var sprite = GetComponent<SpriteRenderer>();
            float top = sprite != null && sprite.sprite != null ? sprite.sprite.bounds.max.y : 0.5f;
            Vector3 pos = transform.TransformPoint(new Vector3(0f, top + 12f * DamagePopup.Pixel, 0f));
            if (isClient)
            {
                DamagePopup.Spawn(pos, amount, kind);
                HitFx.OnDirectHit(gameObject, kind);
            }

            RpcShowDamage(amount, kind, pos);
        }

        [ClientRpc]
        private void RpcShowDamage(int amount, DamagePopupKind kind, Vector3 pos)
        {
            if (isServer)
            {
                return;
            }

            DamagePopup.Spawn(pos, amount, kind);
            HitFx.OnDirectHit(gameObject, kind);
        }

        /// <summary>
        /// 공격자의 남은 개인 할당량까지만 보스 체력을 깎고, 할당량을 넘는 초과분은 보스 체력에
        /// 반영되지 않은 채 지원 게이지로 전환된다("자신의 할당량을 다 채운 플레이어가 이후 주는
        /// 대미지는 보스의 남은 체력에는 반영되지 않는다", GDD '보스 개인 할당량' 참고).
        /// </summary>
        private void ApplyQuotaDamage(int amount, NetworkIdentity attacker)
        {
            EnsureQuotaInitialized();

            uint attackerId = attacker != null ? attacker.netId : 0;
            int remaining = _remainingQuota.TryGetValue(attackerId, out int value) ? value : 0;
            int appliedToBoss = Mathf.Min(amount, remaining);
            int excess = amount - appliedToBoss;

            if (appliedToBoss > 0)
            {
                _remainingQuota[attackerId] = remaining - appliedToBoss;
                currentHealth = Mathf.Max(0, currentHealth - appliedToBoss);
            }

            if (excess > 0)
            {
                AddSupportGauge(excess);
            }
        }

        /// <summary>스폰 후 첫 피격 시점에 현재 접속 중인 파티원 수 기준으로 할당량을 나눈다. 씬에 있는
        /// 모든 PlayerController를 균등 분배 대상으로 삼는다(플레이어 입장/퇴장을 반영하는 동적 재분배는
        /// 아직 없음 — 파티 구성 변경이 잦아지면 그때 추가한다). 쓰러진(부활 대기) 플레이어도 인원에 그대로 센다 —
        /// 쓰러짐은 일시 상태라 할당량 분배 규칙을 바꾸지 않는다(2026-10-07).</summary>
        private void EnsureQuotaInitialized()
        {
            if (_quotaInitialized)
            {
                return;
            }

            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            int quotaPerPlayer = players.Length > 0 ? Mathf.CeilToInt((float)maxHealth / players.Length) : maxHealth;

            foreach (PlayerController player in players)
            {
                _remainingQuota[player.netIdentity.netId] = quotaPerPlayer;
            }

            _quotaInitialized = true;
        }

        private void AddSupportGauge(int amount)
        {
            _supportGauge += amount;

            while (_supportGauge >= statsConfig.supportGaugeThreshold)
            {
                _supportGauge -= statsConfig.supportGaugeThreshold;
                HealPlayersWithRemainingQuota();
            }
        }

        /// <summary>지원 발동: 아직 할당량을 다 못 채운 파티원 전체에게 즉시 회복을 준다(근접 여부 무관,
        /// 결정 대기 체크리스트 G).</summary>
        private void HealPlayersWithRemainingQuota()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                uint id = player.netIdentity.netId;
                if (!_remainingQuota.TryGetValue(id, out int remaining) || remaining <= 0)
                {
                    continue;
                }

                // 쓰러진 플레이어는 Heal이 IsDead로 무시한다 — 지원 회복으로 되살아나지 않는다(부활은 PlayerRevive만, 2026-10-07 의도).
                PlayerHealth health = player.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    health.Heal(statsConfig.supportHealAmount);
                }
            }
        }

        /// <summary>
        /// 처치된 보스 시체를 흡수할 때 호출한다(호출부: AbsorptionController). 잡몹과 달리 파티 경쟁이
        /// 없고(개인별 보상, GDD '보스 계층 상세' 참고) 파티원 각자 로컬로 흡수할 수 있어야 하므로, 여기서는
        /// 오브젝트를 파괴하지 않는다 — 시체는 다음 층으로 넘어갈 때 레벨과 함께 정리된다.
        /// 한 플레이어는 한 번만 흡수할 수 있다(2026-09-23 수정: 전에는 반복 흡수로 영구 재화가 계속 쌓였음).
        /// </summary>
        public bool TryBeginAbsorb(NetworkIdentity absorber)
        {
            // 마지막 층 보스는 흡수하지 않는다(2026-10-07 사용자 결정) — 쓰러지면 금빛 포탈(FloorExit)로 나가야 승리.
            if (FloorProgression.Instance != null && FloorProgression.Instance.IsLastFloor)
            {
                return false;
            }

            return isServer && isDefeated && absorber != null && !_absorbedBy.Contains(absorber.netId);
        }

        public void MarkAbsorbedBy(NetworkIdentity absorber)
        {
            if (absorber != null && _absorbedBy.Add(absorber.netId))
            {
                absorbedNetIds.Add(absorber.netId);
            }
        }

        // 흡수한 사람 화면에서 시체(몸 그림)를 감춘다. 상태 이펙트·라벨·하얀 번쩍임은 몸 렌더러가 꺼지면 같이 숨는다.
        // 감추기 직전 디졸브 잔상(DissolveGhost)을 남긴다. 층 이동 중이면 DissolveGhost 억제창(층 이동 가드)이 똑같이 막고, 보스 오브젝트 파괴 자체에는 잔상 훅이 없다.
        private void LateUpdate()
        {
            if (_corpseHidden || !isDefeated || NetworkClient.localPlayer == null
                || !absorbedNetIds.Contains(NetworkClient.localPlayer.netId))
            {
                return;
            }

            _corpseHidden = true;
            var body = GetComponent<SpriteRenderer>();
            if (body != null)
            {
                // 비주얼 3단계 V9(2026-10-15): 흡수한 사람 화면에서만 시체가 부서지며 사라진다(각 화면 로컬, _corpseHidden으로 1회).
                DissolveGhost.Spawn(body, DissolveGhost.EdgeColorFor(gameObject));
                body.enabled = false;
            }
        }

        /// <summary>클라이언트에서도 읽을 수 있는 흡수 여부(동기화 목록). 첫 플레이 힌트의 '보스 흡수' 안내가 쓴다.</summary>
        public bool IsAbsorbedBy(uint netId)
        {
            return absorbedNetIds.Contains(netId);
        }

        /// <summary>다음 층 출구(FloorExit)가 "이 플레이어가 보상을 챙겼는가"를 확인할 때 쓴다.</summary>
        public bool HasAbsorbed(NetworkIdentity player)
        {
            return player != null && _absorbedBy.Contains(player.netId);
        }
    }
}
