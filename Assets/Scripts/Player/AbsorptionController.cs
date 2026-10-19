using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Absorption;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Monster;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 흡수 액션(홀드 입력). GDD 설계: 잡몹은 방(룸)당 플레이어 1명이 1마리만 흡수 가능하고,
    /// 흡수한 특성이 기존 패시브 슬롯(1개)을 완전히 대체한다(커비처럼 슬롯 1개 — 새로 흡수하면 기존
    /// 특성은 그냥 버려짐, 별도 인벤토리/보관 없음). 지금 단계에서는 "방" 개념 자체가 아직 없으므로
    /// HasAbsorbedThisRoom 플래그를 룸 시스템이 생길 때 ResetRoomAbsorption()으로 초기화하는 자리만
    /// 마련해둔다.
    /// 같은 흡수 액션으로 보스 시체도 처리한다(GDD "메인 능력 획득도 흡수 액션으로 통일" 참고) — 방당
    /// 제한/쿨타임과 무관한 개인별 보상. 흡수 즉시 영구 강화 재화를 항상 지급하고, 추가로 속성
    /// 흡수/교체 선택을 기다리는 상태가 된다(PendingBossChoice — 속성이 없으면 새로 흡수, 있으면 다른
    /// 속성으로 교체하거나 그냥 유지할 수 있다. 융합/양자택일 없음). 보스는 패시브(특성) 축을 주지
    /// 않고 메인 능력(속성) 축만 다룬다(결정, 2026-09-17). 실제 선택은 <see cref="UI.BossChoiceUIController"/>가
    /// 동기화 값 PendingBossChoiceNetId를 폴링해 패널을 띄우고 ResolveBossElementChoice/DeclineBossElementChoice를
    /// 호출하는 방식으로 처리한다(원격 소유 클라이언트는 Command로 서버에 전달, 2인 검증 준비). 아래 ContextMenu 훅은 UI 없이도 빠르게 확인할 수 있는 보조 디버그
    /// 수단으로 남겨둔다.
    /// </summary>
    public class AbsorptionController : NetworkBehaviour
    {
        [SerializeField] private float absorbRange = 1.2f;
        [SerializeField] private float absorbCooldown = 1.5f; // 방당 1마리 제한과 별개로, 흡수 "시도" 자체의 연타/재시도 속도를 제한
        [SerializeField] private LayerMask monsterLayer;


        [Header("보스 처치 보상 — 흡수 시 항상 지급되는 영구 강화 재화")]
        [SerializeField] private int bossKillCurrency = 100; // [PLAYTEST] 구체 수치 미정 — 임시값

        [Header("파편 머금기·뱉기 (GDD 보스 대미지 규칙, 프로토타입)")]
        [SerializeField] private GameObject spitProjectilePrefab;
        [SerializeField] private float debrisPickupRange = 1.2f;
        [SerializeField] private Color aimLineColor = new Color(1f, 0.9f, 0.3f, 0.6f);

        [Header("피격 시 패시브 방출 (GDD '피격 시 패시브 특성(흡수체) 방출 시스템')")]
        [SerializeField] private GameObject droppedTraitPrefab;
        [SerializeField] private float traitDropDistance = 1.5f; // [PLAYTEST] 플레이어에게서 떨어지는 거리
        [SerializeField] private float traitPickupRange = 1.2f;

        [SyncVar] private int heldDebris;

        // 현재 특성 표시·원격 로컬 연출용 동기화 값(서버가 CurrentTrait와 함께 갱신 — SyncTraitState). 판정은 서버 CurrentTrait 그대로.
        [SyncVar(hook = nameof(OnSyncedSkillChanged))] private AbsorbSkill syncedSkill = AbsorbSkill.None;
        // HUD 표시용 특성 id(언어 표 키 trait.<id>.*, 2026-10-06 — 예전엔 표시 이름 문자열). 각 클라이언트가 자기 언어로 표시한다.
        [SyncVar] private string syncedTraitId;

        // 방당 흡수 사용 여부(HUD 표시용 동기화, 판정은 서버가 이 값으로 한다).
        [SyncVar] private bool hasAbsorbedThisRoom;
        /// <summary>흡수 버튼을 누르고 있는 중(서버가 홀드 시작·확정·취소 때 갱신) — 몸 늘이기 연출용(SlimeBodyAnimator), 모든 화면.</summary>
        [SyncVar] private bool syncedAbsorbHolding;
        public bool IsAbsorbHolding => syncedAbsorbHolding;

        /// <summary>흡수 판정 반경(읽기 전용) — 첫 플레이 힌트(FirstPlayHints)가 같은 범위로 근처 대상을 본다(2026-10-16).</summary>
        public float AbsorbRange => absorbRange;

        // 보스 속성 선택 대기(2인 검증 준비): 서버의 PendingBossChoice·제시 선택지를 소유 클라이언트 UI가 읽을 수 있게 동기화한다.
        // 선택지를 먼저 선언해 원격에서 pendingBossNetId가 바뀔 때 선택지는 이미 새 값이 되게 한다(선언 순서대로 역직렬화).
        [SyncVar] private byte pendingOfferCount;
        [SyncVar] private ElementType pendingOffer0 = ElementType.None;
        [SyncVar] private ElementType pendingOffer1 = ElementType.None;
        [SyncVar] private uint pendingBossNetId; // 0 = 대기 없음
        // 보스 방 출구를 쓸 준비가 됐는지(보스 흡수 + 속성 선택 끝) — 서버 FloorExit가 갱신, 모든 클라이언트가 출구 색·대기 인원 표시에 쓴다(2026-09-30).
        [SyncVar] private bool floorExitReady;

        /// <summary>지금 든 특성의 흡수 스킬(동기화 값, 없으면 None). 파티 UI·원격 클라이언트 로컬 연출용.</summary>
        public AbsorbSkill SyncedSkill => syncedSkill;

        /// <summary>파편을 머금고 있으면 공격 버튼이 무기 휘두르기 대신 뱉기가 된다(플레이어 공격 입력 쪽).</summary>
        public bool HasDebris => heldDebris > 0;

        public bool HasAbsorbedThisRoom
        {
            get => hasAbsorbedThisRoom;
            private set => hasAbsorbedThisRoom = value;
        }

        /// <summary>지금 든 특성의 언어 표 id(동기화 값, 없으면 null). HUD용 — 원격 클라이언트에서는 CurrentTrait(서버 전용)가 비어 있다.</summary>
        public string SyncedTraitId => syncedTraitId;

        /// <summary>지금 든 특성 이름(현재 언어, 없으면 null).</summary>
        public string SyncedTraitName => string.IsNullOrEmpty(syncedTraitId) ? null : Loc.Get(PassiveTraitData.NameKey(syncedTraitId));

        /// <summary>보스 속성 선택 대기 중인 보스의 netId(동기화 값, 0이면 없음). BossChoiceUIController가 폴링한다.</summary>
        public uint PendingBossChoiceNetId => pendingBossNetId;
        /// <summary>보스 방 출구 사용 준비 완료(동기화 값). FloorExit가 서버에서 갱신한다.</summary>
        public bool FloorExitReady => floorExitReady;

        [Server]
        public void SetFloorExitReady(bool ready)
        {
            if (floorExitReady != ready)
            {
                floorExitReady = ready;
            }
        }

        /// <summary>보스 속성 선택지 {①, ②}(동기화 값). 대기 중이 아니면 빈 배열.</summary>
        public ElementType[] PendingOfferedElements
        {
            get
            {
                if (pendingBossNetId == 0 || pendingOfferCount == 0)
                {
                    return System.Array.Empty<ElementType>();
                }

                return pendingOfferCount >= 2 ? new[] { pendingOffer0, pendingOffer1 } : new[] { pendingOffer0 };
            }
        }
        public PassiveTraitData CurrentTrait { get; private set; }

        private SpriteRenderer _aimLine;

        /// <summary>흡수는 했지만 아직 속성 흡수/교체 선택을 안 한 보스(영구 재화는 흡수 시점에 이미
        /// 지급됨). null이 아니면 BossChoiceUIController가 선택 패널을 띄운다. ResolveBossElementChoice
        /// 또는 DeclineBossElementChoice 호출로 비워진다. 서버 전용 값 — 소유 클라이언트 UI는 PendingBossChoiceNetId/PendingOfferedElements(동기화)를 읽는다.</summary>
        public BossStub PendingBossChoice { get; private set; }

        // 리팩터 B안 단계 3: PlayerController를 직접 알지 않는다. 조작 상태·조준은 IPlayerActionState로 읽고,
        // 특성·흡수 스킬 변화는 아래 이벤트로 알린다(PlayerController가 구독).
        private IPlayerActionState _actionState;
        private PlayerHealth _health;
        private MainAbilityController _mainAbilityController;
        private PlayerCurrency _playerCurrency;
        private PlayerControls _controls;
        private bool _inputBlockedByMenu; // ESC 메뉴·옵션 때문에 Gameplay 맵을 꺼 둔 상태
        private float _cooldownReadyTime;    // 서버 전용 — Time.time 기준(원격 플레이어 오브젝트는 서버에서 로컬 Update 타이머가 안 돈다)
        private float _absorbHoldStartTime = float.NegativeInfinity; // 서버 전용 — 홀드 시작 시각(확정 요청 대략 검증)
        private const float MinHoldSeconds = 0.4f; // 홀드 0.5초(Input System Hold, 2026-10-07 1초→0.5초) 대략 검증 — 지연 흔들림 여유

        /// <summary>홀드가 시작될 때 잠근 잡몹(결정 대기 체크리스트 C). 확정(성공) 또는 취소 시 즉시 풀린다.</summary>
        private MonsterStub _lockedMonster;

        /// <summary>
        /// 장착 패시브 특성이 바뀜(서버 전용). 인자 = 새 특성 또는 null. SyncTraitState 바로 다음에 발생 —
        /// 옛 PlayerController.ApplyPassiveTrait 직접 호출 자리(ApplyTrait·DropTraitOnHit·DebugClearTrait). PlayerController가 구독.
        /// </summary>
        public event System.Action<PassiveTraitData> ServerTraitChanged;

        /// <summary>
        /// 동기화된 흡수 스킬이 정해짐(모든 PC — OnStartClient 1회 + SyncedSkill 훅). 옛 PlayerController.ApplySyncedAbsorbSkill 직접 호출 자리.
        /// 호스트 이중 적용 방지(isServer면 무시)는 구독자(PlayerController) 핸들러 안에 있다.
        /// </summary>
        public event System.Action<AbsorbSkill> SyncedSkillChanged;

        private void Awake()
        {
            // 인터페이스 값은 Unity '가짜 null'을 != null이 거르지 못하므로, 없으면 진짜 null로 저장(단계 2 IStatusTarget과 같은 처리).
            var actionState = GetComponent<IPlayerActionState>();
            _actionState = (actionState as Object) != null ? actionState : null;
            _health = GetComponent<PlayerHealth>();
            if (_health != null)
            {
                _health.ServerDamagedAlive += DropTraitOnHit; // 옛 PlayerHealth의 `_absorption != null` 확인 뒤 직접 호출 자리(리팩터 B안 단계 3)
            }

            _mainAbilityController = GetComponent<MainAbilityController>();
            _playerCurrency = GetComponent<PlayerCurrency>();
            _controls = new PlayerControls();
            InputBindingStore.Register(_controls); // 옵션 창에서 바꾼 키 적용(2026-09-30)
        }

        public override void OnStartClient()
        {
            // 늦게 접속했거나 스폰 시점에 이미 특성이 있는 경우 — 훅이 안 불려도 원격 쪽 _absorbSkill을 맞춘다(서버는 무시됨).
            SyncedSkillChanged?.Invoke(syncedSkill);
            _lastHeldDebris = heldDebris; // 늦게 접속한 화면에서 이미 머금은 파편으로 소리가 나지 않게
        }

        private void OnSyncedSkillChanged(AbsorbSkill oldValue, AbsorbSkill newValue)
        {
            SyncedSkillChanged?.Invoke(newValue);

            // 흡수·방출 이펙트(2026-10-07): 탈이 생기거나 바뀌면 "퐁" 연기, 특성을 잃으면 금빛 별이 튀는 방출. 모든 화면(훅).
            // 효과음(2026-10-14): 탈 쓰기(새로 얻음·교체) "퐁" / 특성을 잃음(None이 됨). 이펙트와 같은 훅이라 화면마다 1회.
            if (newValue != AbsorbSkill.None)
            {
                Audio.Sfx.Play(Audio.SfxId.TraitEquip, transform.position);
            }
            else if (oldValue != AbsorbSkill.None)
            {
                Audio.Sfx.Play(Audio.SfxId.TraitLost, transform.position);
            }

            if (newValue != AbsorbSkill.None)
            {
                AbsorbRoguelike.UI.AbsorbFx.Puff(transform);
                var body = GetComponent<SlimeBodyAnimator>();
                if (body != null)
                {
                    body.Gulp(); // "꿀꺽"
                }
            }
            else if (oldValue != AbsorbSkill.None)
            {
                AbsorbRoguelike.UI.AbsorbFx.Emit(transform.position);
            }
        }

        /// <summary>흡수가 성사되는 순간 대상 자리에서 빛 알갱이가 이 플레이어에게 빨려 드는 연출(모든 화면, 호스트 포함).</summary>
        [ClientRpc]
        private void RpcAbsorbStream(Vector3 from)
        {
            AbsorbRoguelike.UI.AbsorbFx.Stream(from, transform);
            Audio.Sfx.Play(Audio.SfxId.AbsorbSuccess, transform.position); // 흡수 성사 "꿀꺽"(모든 화면 1회 — ClientRpc)
        }

        // 서버: CurrentTrait를 바꾼 뒤 항상 호출해 동기화 값을 맞춘다.
        private void SyncTraitState()
        {
            syncedSkill = CurrentTrait != null ? CurrentTrait.Skill : AbsorbSkill.None;
            syncedTraitId = CurrentTrait != null ? CurrentTrait.LocId : null;
        }

        // 서버: 보스 선택 대기 상태를 바꾼다(null이면 해제). 동기화 값도 같이 맞춘다.
        private void SetPendingBossChoice(BossStub boss, ElementType[] offered)
        {
            PendingBossChoice = boss;
            int count = boss != null && offered != null ? Mathf.Min(2, offered.Length) : 0;
            pendingOfferCount = (byte)count;
            pendingOffer0 = count > 0 ? offered[0] : ElementType.None;
            pendingOffer1 = count > 1 ? offered[1] : ElementType.None;
            pendingBossNetId = boss != null ? boss.netId : 0;
        }

        public override void OnStartLocalPlayer()
        {
            _controls.Gameplay.Enable();
            _controls.Gameplay.Absorb.started += OnAbsorbStarted;
            _controls.Gameplay.Absorb.performed += OnAbsorbPerformed;
            _controls.Gameplay.Absorb.canceled += OnAbsorbCanceled;

            // 씬에 플레이어가 나중에 스폰되는 타이밍(호스트 접속 후 캐릭터 생성)을 고려해, 스폰 시점에
            // 보스 선택 UI에 자기 자신을 등록한다(BossChoiceUIController가 폴링할 대상).
            BossChoiceUIController.RegisterLocalPlayer(this);
        }

        public override void OnStopLocalPlayer()
        {
            BossChoiceUIController.UnregisterLocalPlayer(this);

            _controls.Gameplay.Absorb.started -= OnAbsorbStarted;
            _controls.Gameplay.Absorb.performed -= OnAbsorbPerformed;
            _controls.Gameplay.Absorb.canceled -= OnAbsorbCanceled;
            _controls.Gameplay.Disable();
        }

        // 입력 에셋 인스턴스 정리(플레이어 이동 컴포넌트의 OnDestroy와 같은 이유).
        private void OnDestroy()
        {
            InputBindingStore.Unregister(_controls);
            _controls?.Dispose();

            if (_health != null)
            {
                _health.ServerDamagedAlive -= DropTraitOnHit;
            }

            Audio.Sfx.StopLoop(ref _absorbHoldLoop);
        }

        // 효과음(2026-10-14): 흡수 홀드 반복·파편 머금기. 둘 다 동기화 값(syncedAbsorbHolding·heldDebris)을 모든 화면이 보고 판단한다 —
        // 호스트는 서버가 바꾼 값을 같은 오브젝트에서 그대로 보므로 화면마다 정확히 한 번(새 네트워크 메시지 없음).
        private int _absorbHoldLoop;
        private int _lastHeldDebris;

        private void UpdateAbsorbSounds()
        {
            bool holding = syncedAbsorbHolding && isActiveAndEnabled;
            if (holding && _absorbHoldLoop == 0)
            {
                _absorbHoldLoop = Audio.Sfx.StartLoop(Audio.SfxId.AbsorbHold, transform);
            }
            else if (!holding && _absorbHoldLoop != 0)
            {
                Audio.Sfx.StopLoop(ref _absorbHoldLoop);
            }

            if (heldDebris > _lastHeldDebris)
            {
                Audio.Sfx.Play(Audio.SfxId.DebrisGrab, transform.position);
            }

            _lastHeldDebris = heldDebris;
        }

        private void OnDisable() => Audio.Sfx.StopLoop(ref _absorbHoldLoop);

        private void Update()
        {
            UpdateAbsorbSounds();
            if (!isLocalPlayer)
            {
                return;
            }

            // ESC 메뉴·옵션 창이 열려 있으면 흡수 홀드를 받지 않는다(2026-09-30). 맵을 끄면 홀드 중이던 흡수는 canceled →
            // 기존 OnAbsorbCanceled(취소) 경로로 끝난다. 닫히면 다시 켠다. 흡수 판정 로직은 그대로.
            // 쓰러짐(멀티)·사망·부활 진행 중(살리는 사람)에도 같은 경로로 흡수 홀드를 막는다(2026-10-07).
            bool menuBlocksInput = PauseMenuController.IsOpen || OptionsMenuController.IsOpen || AbsorbRoguelike.UI.RunEndScreenController.IsShown
                                   || (_actionState != null && _actionState.IsActionLocked);
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

            UpdateAimLine();
        }

        // 파편을 머금고 있는 동안 마우스 방향으로 날아갈 직선 경로를 보여준다. 벽에 막히는 지점에서 끊긴다.
        private void UpdateAimLine()
        {
            if (_aimLine == null)
            {
                var go = new GameObject("SpitAimLine");
                go.transform.SetParent(transform, false);
                _aimLine = go.AddComponent<SpriteRenderer>();
                _aimLine.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
                _aimLine.color = aimLineColor;
                _aimLine.sortingOrder = 12;
            }

            _aimLine.enabled = HasDebris;
            if (!HasDebris)
            {
                return;
            }

            Vector2 dir = _actionState.FacingDirection.normalized;
            float length = SpitRange();
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(transform.position, dir, length))
            {
                // 플레이어 판별: PlayerHealth는 Player.prefab 루트에만 있고 늘 PlayerController와 같은 오브젝트(리팩터 B안 단계 3, 직렬화 기준 파일로 확인)
                if (!hit.collider.isTrigger && hit.collider.GetComponent<PlayerHealth>() == null)
                {
                    length = hit.distance; // 첫 번째로 막히는 것(벽이든 적이든)에서 끊는다
                    break;
                }
            }

            _aimLine.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            _aimLine.transform.localScale = new Vector3(length, 1f / 16f, 1f); // 폭 1px
        }

        private float SpitRange()
        {
            var projectile = spitProjectilePrefab != null ? spitProjectilePrefab.GetComponent<SpitProjectile>() : null;
            return projectile != null ? projectile.Range : 12f;
        }

        /// <summary>머금은 파편을 dir 방향으로 뱉는다. 호스트는 바로, 원격 클라이언트는 서버로 요청이 간다.</summary>
        [Command]
        public void CmdSpit(Vector2 dir)
        {
            if (heldDebris <= 0 || spitProjectilePrefab == null)
            {
                return;
            }

            heldDebris = 0;
            Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
            GameObject go = Instantiate(spitProjectilePrefab, (Vector2)transform.position + d * 0.5f, Quaternion.identity);
            go.GetComponent<SpitProjectile>().Initialize(d, netIdentity);
            NetworkServer.Spawn(go);
        }

        private BossDebris FindNearestDebris()
        {
            BossDebris nearest = null;
            float nearestDistance = debrisPickupRange;
            foreach (BossDebris debris in FindObjectsByType<BossDebris>(FindObjectsSortMode.None))
            {
                float distance = Vector2.Distance(transform.position, debris.transform.position);
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = debris;
                }
            }

            return nearest;
        }

        private DroppedTrait FindNearestDroppedTrait()
        {
            DroppedTrait nearest = null;
            float nearestDistance = traitPickupRange;
            foreach (DroppedTrait dropped in FindObjectsByType<DroppedTrait>(FindObjectsSortMode.None))
            {
                float distance = Vector2.Distance(transform.position, dropped.transform.position);
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = dropped;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 피격 시 PlayerHealth.ServerDamagedAlive 이벤트로 불린다(서버, 체력이 실제로 줄고 살아 있을 때). 장착 중인 패시브가 벗겨져 근처 바닥에 떨어지고 슬롯이 빈다.
        /// 튕겨 다니지 않고 무작위 방향으로 traitDropDistance만큼 떨어진 자리에 놓인다 — 벽이 가로막으면 벽 앞에.
        /// 분해 보상은 없다(의도치 않은 손실, GDD).
        /// </summary>
        public void DropTraitOnHit()
        {
            if (!isServer || CurrentTrait == null || droppedTraitPrefab == null)
            {
                return;
            }

            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.down;
            }

            float distance = traitDropDistance;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(transform.position, dir, traitDropDistance))
            {
                if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<PlayerHealth>() == null) // 플레이어 판별(위 UpdateAimLine과 같은 근거)
                {
                    distance = Mathf.Max(0f, hit.distance - 0.3f);
                    break;
                }
            }

            GameObject go = Instantiate(droppedTraitPrefab, (Vector2)transform.position + dir * distance, Quaternion.identity);
            go.GetComponent<DroppedTrait>().Initialize(CurrentTrait);
            NetworkServer.Spawn(go);

            Debug.Log($"[Absorb] {name} dropped trait on hit: {CurrentTrait.DisplayName}");
            CurrentTrait = null;
            SyncTraitState();
            ServerTraitChanged?.Invoke(null);
        }

        // 판정(흡수 성사 여부)은 서버 권위 (ADR-003). 입력은 소유 클라이언트에서 받고, 호스트는 서버 메서드를 직접,
        // 원격 클라이언트는 CmdAbsorbStart/Confirm/Cancel로 같은 서버 메서드를 부른다(2인 검증 준비, 2026-09-29).
        // 로컬 UI(조준선)는 소유 클라이언트에 그대로 둔다.

        private void OnAbsorbStarted(InputAction.CallbackContext context)
        {
            if (isServer) ServerAbsorbStart(); else CmdAbsorbStart();
        }

        private void OnAbsorbPerformed(InputAction.CallbackContext context)
        {
            if (isServer) ServerAbsorbConfirm(); else CmdAbsorbConfirm();
        }

        private void OnAbsorbCanceled(InputAction.CallbackContext context)
        {
            if (isServer) ServerAbsorbCancel(); else CmdAbsorbCancel();
        }

        [Command]
        private void CmdAbsorbStart() => ServerAbsorbStart();

        [Command]
        private void CmdAbsorbConfirm() => ServerAbsorbConfirm();

        [Command]
        private void CmdAbsorbCancel() => ServerAbsorbCancel();

        /// <summary>홀드 시작(서버): 잡몹이라면 그 자리에서 잠가(TryLock) 다른 플레이어가 동시에 못 가져가게 한다
        /// (결정 대기 체크리스트 C). 보스는 파티 경쟁이 없는 개인별 보상이라 잠글 필요가 없다.</summary>
        private void ServerAbsorbStart()
        {
            if (!isServer)
            {
                return;
            }

            _absorbHoldStartTime = Time.time; // 확정 요청 대략 검증용(홀드 0.5초)
            syncedAbsorbHolding = true;

            // 파편은 홀드를 기다리지 않고 누르는 즉시 머금는다(커비 빨아들이기). 방당 제한·쿨타임과 무관.
            if (!HasDebris)
            {
                BossDebris debris = FindNearestDebris();
                if (debris != null && debris.TryTake())
                {
                    heldDebris = 1;
                    return;
                }
            }

            // 벗겨진 패시브도 누르는 즉시 되찾는다. 되찾기는 새 흡수가 아니므로 방당 제한·쿨타임과 무관.
            DroppedTrait dropped = FindNearestDroppedTrait();
            if (dropped != null && dropped.TryTake(out PassiveTraitData trait))
            {
                RpcAbsorbStream(dropped.transform.position);
                ApplyTrait(trait);
                Debug.Log($"[Absorb] {name} recovered dropped trait: {trait.DisplayName}");
                return;
            }

            if (HasAbsorbedThisRoom || Time.time < _cooldownReadyTime)
            {
                return;
            }

            MonsterStub nearest = FindNearestAbsorbableMonster();
            if (nearest != null && nearest.TryLock(netIdentity))
            {
                _lockedMonster = nearest;
            }
        }

        /// <summary>홀드 확정(서버, 0.5초 유지 완료): 보스 시체가 있으면 그쪽을 우선 처리하고, 아니면 홀드 시작
        /// 때 잠가둔 잡몹을 흡수한다 — 재스캔하지 않는다(이동 중 다른 대상으로 바뀌는 걸 방지).
        /// 홀드 시간은 서버가 시작 시각으로 대략 검증한다(MinHoldSeconds 미만이면 무시하고 잠금만 푼다).</summary>
        private void ServerAbsorbConfirm()
        {
            if (!isServer)
            {
                return;
            }

            float held = Time.time - _absorbHoldStartTime;
            _absorbHoldStartTime = float.NegativeInfinity;
            syncedAbsorbHolding = false;
            if (held < MinHoldSeconds)
            {
                Debug.LogWarning($"[Absorb] {name} absorb confirm rejected: held {held:0.00}s < {MinHoldSeconds}s");
                ReleaseLock();
                return;
            }

            BossStub nearestBoss = FindNearestBossCorpse();
            if (nearestBoss != null)
            {
                AbsorbBoss(nearestBoss);
                ReleaseLock();
                return;
            }

            MonsterStub nearestEliteCorpse = FindNearestEliteCorpse();
            if (nearestEliteCorpse != null)
            {
                AbsorbEliteCorpse(nearestEliteCorpse);
                ReleaseLock();
                return;
            }

            if (_lockedMonster == null || HasAbsorbedThisRoom || _lockedMonster.IsDead)
            {
                ReleaseLock();
                return;
            }

            RpcAbsorbStream(_lockedMonster.transform.position);
            ApplyTrait(_lockedMonster.TraitData);
            HasAbsorbedThisRoom = true;
            _cooldownReadyTime = Time.time + absorbCooldown;

            string traitName = CurrentTrait != null ? CurrentTrait.DisplayName : "(no trait data)";
            Debug.Log($"[Absorb] {name} absorbed {_lockedMonster.name} (passive slot now: {traitName})");
            _lockedMonster.Absorb();
            _lockedMonster = null;
        }

        /// <summary>홀드가 중간에 취소되면(버튼을 일찍 뗌) 잠가둔 대상을 즉시 풀어준다(서버).</summary>
        private void ServerAbsorbCancel()
        {
            if (!isServer)
            {
                return;
            }

            _absorbHoldStartTime = float.NegativeInfinity;
            syncedAbsorbHolding = false;
            ReleaseLock();
        }

        private void ReleaseLock()
        {
            if (_lockedMonster != null)
            {
                _lockedMonster.Unlock(netIdentity);
                _lockedMonster = null;
            }
        }

        private MonsterStub FindNearestAbsorbableMonster()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, absorbRange, monsterLayer);
            MonsterStub nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Collider2D hit in hits)
            {
                MonsterStub monster = hit.GetComponent<MonsterStub>();
                if (monster == null || monster.IsElite || monster.IsDead || monster.IsLockedByOther(netIdentity.netId))
                {
                    continue; // 정예몹은 잠금/방당 제한 경로가 아니라 아래 FindNearestEliteCorpse로 따로 처리한다
                }

                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }

            return nearest;
        }

        /// <summary>처치된 정예몹 시체를 찾는다. 보스와 동일하게 잠금/방당 제한 없이, 파티원 전원이
        /// 각자 독립적으로 흡수할 수 있다(GDD '잡몹/정예몹 계층 상세' 참고).</summary>
        private MonsterStub FindNearestEliteCorpse()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, absorbRange, monsterLayer);
            MonsterStub nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Collider2D hit in hits)
            {
                MonsterStub monster = hit.GetComponent<MonsterStub>();
                if (monster == null || !monster.TryBeginAbsorbCorpse(netIdentity))
                {
                    continue;
                }

                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }

            return nearest;
        }

        private BossStub FindNearestBossCorpse()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, absorbRange, monsterLayer);
            BossStub nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Collider2D hit in hits)
            {
                BossStub boss = hit.GetComponent<BossStub>();
                if (boss == null || !boss.TryBeginAbsorb(netIdentity))
                {
                    continue;
                }

                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = boss;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 보스 시체 흡수. 보스는 패시브(특성) 축을 주지 않고 메인 능력(속성) 축만 다룬다(결정,
        /// 2026-09-17 — 잡몹/정예몹과 달리 보스는 흡수해도 패시브 슬롯이 바뀌지 않는다). 흡수 즉시
        /// 영구 강화 재화를 항상 지급하고, 추가로 속성 흡수/교체 선택을 기다리는 상태(PendingBossChoice)로
        /// 들어간다. 파티 경쟁이 없는 개인별 보상이라 방당 제한(HasAbsorbedThisRoom)과 쿨타임을 건드리지
        /// 않는다.
        /// </summary>
        private void AbsorbBoss(BossStub boss)
        {
            if (_playerCurrency != null)
            {
                _playerCurrency.AddCurrency(bossKillCurrency);
            }

            boss.MarkAbsorbedBy(netIdentity);
            RpcAbsorbStream(boss.transform.position);

            // 선택지 ②는 "지금 든 속성"을 뺀 나머지에서 뽑으므로, 속성이 바뀌기 전 값으로 여기서 확정한다.
            ElementType current = _mainAbilityController != null ? _mainAbilityController.CurrentElement : ElementType.None;
            ElementType[] offered = boss.PrepareOfferFor(netIdentity, current);

            SetPendingBossChoice(boss, offered); // 소유 클라이언트 UI에는 동기화 값(pendingBossNetId·선택지)으로 전달된다
            string offeredLabel = offered.Length >= 2 ? $"{offered[0]} / {offered[1]}" : "(none)";
            Debug.Log($"[Absorb] {name} absorbed boss {boss.name} — granted {bossKillCurrency} currency, awaiting element choice (제시: {offeredLabel})");
        }

        /// <summary>
        /// 정예몹 시체 흡수. 파티원 각자 한 번씩 흡수할 수 있고, 전원이 흡수하면 시체가 사라진다.
        /// 정예몹은 메인 능력 선택지가 없으므로(그 축은 보스 전용) 패시브 특성만 준다.
        /// </summary>
        private void AbsorbEliteCorpse(MonsterStub elite)
        {
            RpcAbsorbStream(elite.transform.position);
            ApplyTrait(elite.TraitData);
            Debug.Log($"[Absorb] {name} absorbed elite corpse {elite.name}");
            elite.MarkCorpseAbsorbed(netIdentity);
        }

        /// <summary>PendingBossChoice가 제시한 속성 중 하나를 현재 메인 능력에 흡수/교체한다(원하면
        /// 아무것도 호출하지 않고 기존 속성을 그대로 유지해도 된다). 호스트는 서버 메서드를 직접, 원격 소유 클라이언트는
        /// CmdResolveBossElementChoice로 서버에 요청한다(2인 검증 준비). 결과(속성 SyncVar, 대기 해제)는 동기화로 돌아온다.</summary>
        public void ResolveBossElementChoice(int offeredElementIndex)
        {
            if (isServer)
            {
                ServerResolveBossElementChoice(offeredElementIndex);
            }
            else if (isLocalPlayer)
            {
                CmdResolveBossElementChoice(offeredElementIndex);
            }
        }

        [Command]
        private void CmdResolveBossElementChoice(int offeredElementIndex) => ServerResolveBossElementChoice(offeredElementIndex);

        // 서버: PendingBossChoice와 GetOfferedElementsFor(netIdentity)로 검증한 뒤 적용한다.
        private void ServerResolveBossElementChoice(int offeredElementIndex)
        {
            if (!isServer || PendingBossChoice == null || _mainAbilityController == null)
            {
                return;
            }

            ElementType[] offered = PendingBossChoice.GetOfferedElementsFor(netIdentity);
            if (offeredElementIndex < 0 || offeredElementIndex >= offered.Length)
            {
                Debug.LogWarning($"[Absorb] {name} boss element choice rejected: index {offeredElementIndex} (offered {offered.Length})");
                return;
            }

            _mainAbilityController.ApplyElement(offered[offeredElementIndex]);
            SetPendingBossChoice(null, null);
        }

        /// <summary>PendingBossChoice가 제시한 속성 중 어느 쪽도 선택하지 않고 기존 속성을 그대로
        /// 유지한다("유지" 선택지). ApplyElement를 호출하지 않고 대기만 비워, 선택 UI가
        /// 안전하게 패널을 닫을 수 있게 한다. 원격 소유 클라이언트는 Command로 서버 대기를 푼다(보스 방 출구 잠금 해제 조건).</summary>
        public void DeclineBossElementChoice()
        {
            if (isServer)
            {
                ServerDeclineBossElementChoice();
            }
            else if (isLocalPlayer)
            {
                CmdDeclineBossElementChoice();
            }
        }

        [Command]
        private void CmdDeclineBossElementChoice() => ServerDeclineBossElementChoice();

        private void ServerDeclineBossElementChoice()
        {
            if (!isServer || PendingBossChoice == null)
            {
                return;
            }

            Debug.Log($"[Absorb] {name} declined boss element choice — keeping current element");
            SetPendingBossChoice(null, null);
        }

        private void ApplyTrait(PassiveTraitData trait)
        {
            // 기존 특성은 그냥 사라진다 — 분해 보상(일시 공격력 버프·재화)·특성 등급·강화(상위) 특성은 2026-10-12 사용자 결정으로 없앴다(GDD 완성본 범위).
            if (CurrentTrait != null)
            {
                Debug.Log($"[Absorb] {name} replaces previous trait: {CurrentTrait.DisplayName}");
            }

            CurrentTrait = trait;
            SyncTraitState();
            ServerTraitChanged?.Invoke(CurrentTrait);
        }

        /// <summary>
        /// 다음 층으로 넘어갈 때 FloorProgression.AdvanceFloor가 호출한다(서버). 패시브 특성·속성·체력은 유지하고,
        /// 이전 층에 묶인 상태(머금은 파편, 잠근 잡몹, 미처리 보스 선택, 방당 흡수 사용 여부)만 비운다.
        /// </summary>
        public void ResetForNewFloor()
        {
            heldDebris = 0;
            ReleaseLock();
            SetPendingBossChoice(null, null);
            HasAbsorbedThisRoom = false;
            _cooldownReadyTime = 0f;
            _absorbHoldStartTime = float.NegativeInfinity;
            syncedAbsorbHolding = false;
        }

        /// <summary>방/스테이지 전환 시스템이 생기면, 새 방에 입장하는 시점에 호출해서 흡수 가능 여부를 초기화한다.</summary>
        [ContextMenu("Reset Room Absorption (Debug)")]
        public void ResetRoomAbsorption()
        {
            HasAbsorbedThisRoom = false;
            Debug.Log($"[Absorb] {name} room absorption reset (debug)");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>디버그 메뉴(F1): 특성을 바로 준다(서버). 기존 ApplyTrait 경로 그대로라 든 특성이 있으면 분해 규칙이 적용된다.</summary>
        [Server]
        public void DebugGrantTrait(PassiveTraitData trait)
        {
            if (trait == null)
            {
                return;
            }

            ApplyTrait(trait);
            Debug.Log($"[Absorb] {name} debug granted trait: {trait.DisplayName}");
        }

        /// <summary>디버그 메뉴(F1): 특성을 분해 없이 비운다(서버).</summary>
        [Server]
        public void DebugClearTrait()
        {
            CurrentTrait = null;
            SyncTraitState();
            ServerTraitChanged?.Invoke(null);
            Debug.Log($"[Absorb] {name} debug cleared trait");
        }

        /// <summary>디버그 메뉴(F1): 보스 파편 하나를 머금은 상태로 만든다(서버).</summary>
        [Server]
        public void DebugHoldDebris()
        {
            heldDebris = 1;
            Debug.Log($"[Absorb] {name} debug holding debris");
        }
#endif

        [ContextMenu("Debug: Resolve Boss Element Choice (Option 1)")]
        private void DebugResolveBossElementOption1() => ResolveBossElementChoice(0);

        [ContextMenu("Debug: Resolve Boss Element Choice (Option 2)")]
        private void DebugResolveBossElementOption2() => ResolveBossElementChoice(1);
    }
}
