using AbsorbRoguelike.Absorption;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 1단계(흡수 시스템) 작업을 위한 최소 몬스터 스텁. 이동/AI 없이 제자리에 서서 맞아주는 역할만 한다.
    /// 대미지 적용은 호출부(PlayerController/AbsorptionController의 공격·흡수 판정)가 이미 isServer로
    /// 감싸고 있으므로, 이 컴포넌트 자체는 별도 권위 체크 없이 TakeDamage/Absorb가 호출되면 그대로
    /// 처리한다. 체력은 SyncVar로 동기화해 원격 클라이언트도 같은 값을 보게 하고, 파괴는
    /// NetworkServer.Destroy로 해서 모든 접속자에게 제거가 전파되게 한다.
    /// 씬에 직접 배치된 "씬 오브젝트"로 취급되며(별도 프리팹 스폰 없음), 호스트가 시작되면 Mirror가
    /// 자동으로 이 오브젝트들을 네트워크에 등록한다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(NetworkIdentity))]
    public class MonsterStub : NetworkBehaviour, AbsorbRoguelike.Combat.IDamageable
    {
        // SyncVar: 층·정예 배율(서버에서 곱함)을 참가자에게도 보내야 체력바가 보인다(2026-09-30 2인 검증 — 참가자 쪽은 현재 ≥ 최대라 늘 숨겨졌음).
        // 리팩터 C4: 설정값은 행동 config(MonsterStatsConfig.maxHealth, Assets/Data/Monsters/<동물>.asset) — Awake에서 넣는다(예전 직렬화 값과 같은 시점·값).
        [SyncVar] private int maxHealth;
        [SerializeField] private PassiveTraitData traitData; // 이 몬스터를 흡수하면 넘겨줄 패시브 특성

        [Header("정예몹(Elite)")]
        [Tooltip("체크하면 잡몹 원형을 그대로 쓰되 정예몹 규칙(처치 후 시체 흡수, 파티 전원 개별 흡수 가능, 잠금 없음)을 따른다.")]
        [SerializeField] private bool isElite;

        [SyncVar] private int currentHealth;
        [SyncVar] private uint lockedByNetId; // 0 = 잠금 없음
        [SyncVar(hook = nameof(OnDefeatedChanged))] private bool isDefeated; // 정예몹 전용 — 죽어도 파괴하지 않고 시체로 남아 흡수를 기다린다

        private readonly System.Collections.Generic.HashSet<uint> _corpseAbsorbedBy = new System.Collections.Generic.HashSet<uint>();
        private bool _corpseApplied;
        private bool _hasBeenHit; // 과다 대미지 보호는 첫 타격에만 적용

        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;
        public bool IsDead => currentHealth <= 0;
        public PassiveTraitData TraitData => traitData;
        public bool IsLocked => lockedByNetId != 0;
        public bool IsElite => isElite;
        public bool IsDefeated => isDefeated;

        private void Awake()
        {
            // 예전에는 프리팹 직렬화 값이 Awake 전에 들어 있었다. 같은 값을 모든 화면(서버·원격 클라)에서 Awake 첫 줄에 넣어
            // 이후 순서(currentHealth 맞춤 → 서버 OnStartServer 정예 배율 → 원격은 SyncVar 덮어쓰기)를 그대로 둔다.
            MonsterStatsConfig stats = GetComponent<MonsterBehaviorController>() is MonsterBehaviorController behavior ? behavior.Stats : null;
            if (stats != null)
            {
                maxHealth = stats.maxHealth;
            }
            else
            {
                Debug.LogError($"[MonsterStub] {name}: 최대 체력을 읽을 MonsterBehaviorController.config가 없다 — Assets/Data/Monsters 에셋을 연결해야 한다.", this);
            }

            currentHealth = maxHealth;
            AbsorbRoguelike.UI.GroundShadow.Attach(gameObject); // 발밑 타원 그림자(2026-10-08)
        }

        private MonsterBehaviorController _behavior;
        private bool _difficultyHealthApplied; // 층·인원 체력 배율은 스폰마다 한 번만(서버)

        /// <summary>
        /// 최대 체력 확정 순서(2026-10-13): 기본(MonsterStatsConfig.maxHealth, Awake) → 정예 배율(EliteMonsterMarker) → 층 × 인원 배율.
        /// Mirror는 컴포넌트 순서대로 OnStartServer를 부르므로 순서에 기대지 않는다 — 정예 마커가 붙어 있으면 마커가 정예 배율 직후
        /// <see cref="ApplyDifficultyHealthScale"/>를 부르고, 없으면 여기서 부른다.
        /// </summary>
        public override void OnStartServer()
        {
            if (GetComponent<EliteMonsterMarker>() == null)
            {
                ApplyDifficultyHealthScale();
            }
        }

        /// <summary>층 × 인원 체력 배율(DifficultyScaling) 적용(서버, 한 번만). currentHealth도 같이 맞춘다(SyncVar라 원격 클라는 자동 수신).</summary>
        public void ApplyDifficultyHealthScale()
        {
            if (!isServer || _difficultyHealthApplied)
            {
                return;
            }

            _difficultyHealthApplied = true;
            maxHealth = AbsorbRoguelike.Dungeon.DifficultyScaling.MonsterHealth(maxHealth, includeParty: true);
            currentHealth = maxHealth;
        }

        /// <summary>
        /// IDamageable 진입점(서버 전용, 리팩터 B안 단계 1, 2026-10-08). 위치 있는 TakeDamage(정면 방어 경로)에 그대로 넘긴다.
        /// 반환: 체력이 실제로 줄었는가(적중 회복 판단용).
        /// </summary>
        public bool ServerTakeHit(in AbsorbRoguelike.Combat.HitInfo hit)
        {
            int hpBefore = currentHealth;
            TakeDamage(hit.Amount, hit.Origin, hit.Kind);
            return currentHealth < hpBefore;
        }

        /// <summary>
        /// 공격자 위치를 아는 직접 공격(플레이어 무기·흡수 스킬·뱉기) 진입점(2026-10-02). 같은 오브젝트의 행동 컴포넌트
        /// (MonsterBehaviorController.ModifyIncomingDamage — 돼지 정면 방어 등)를 먼저 거친 뒤 기존 TakeDamage로 넘긴다.
        /// 대미지·흡수·속성 규칙은 그대로이고, 위치 없는 호출(도트·방전·방 정리)은 기존 오버로드를 그대로 쓴다.
        /// </summary>
        public void TakeDamage(int amount, Vector2 attackerPosition, DamagePopupKind kind = DamagePopupKind.Normal)
        {
            if (!isServer || IsDead)
            {
                return;
            }

            if (_behavior == null)
            {
                _behavior = GetComponent<MonsterBehaviorController>();
            }

            if (_behavior != null)
            {
                amount = _behavior.ModifyIncomingDamage(attackerPosition, amount);
            }

            TakeDamage(amount, kind);
        }

        /// <param name="kind">대미지 숫자 색(결정 ④B). 기본값 Normal(흰색) — 무기·흡수 스킬·뱉기 호출부는 그대로 둔다.</param>
        public void TakeDamage(int amount, DamagePopupKind kind = DamagePopupKind.Normal)
        {
            if (!isServer || IsDead)
            {
                return; // 대미지 적용은 서버(호스트)만 한다 — ADR-003
            }

            // 쥐 갉기 '취약'(2026-09-29): 직접 명중 대미지에만 배율을 곱한다 — 화상·독 도트·전기 방전(IsDirectHit false)은 제외.
            amount = ApplyVulnerable(amount, kind);

            // 결정(2026-09-21, 2026-09-23 정정): 잡몹은 "살아있는 상태에서 흡수"하는 게 기본 설계인데, 첫 타
            // 한 방에 죽어버리면 흡수 기회 자체가 영구히 사라진다. 그래서 **첫 타격이 최대 체력 이상**일 때만
            // 0 대신 1을 남긴다. 두 번째 타격부터는 남은 체력보다 센 한 방이면 그대로 죽는다.
            // 정예몹은 죽어도 시체가 남아 흡수 기회가 사라지지 않으므로 이 보호가 필요 없다.
            bool firstHit = !_hasBeenHit;
            _hasBeenHit = true;
            if (!isElite && firstHit && amount >= currentHealth && currentHealth > 1)
            {
                ShowDamagePopup(currentHealth - 1, kind); // 표시값 = 실제로 깎인 양
                currentHealth = 1;
                Debug.Log($"[MonsterStub] {name} survives a first-hit one-shot at 1 HP (overkill protection)");
                return;
            }

            currentHealth -= amount;
            Debug.Log($"[MonsterStub] {name} took {amount} damage ({currentHealth}/{maxHealth})");

            // 죽는 타격이면 아래 NetworkServer.Destroy보다 먼저 보내야 원격 클라이언트가 RPC를 받는다.
            ShowDamagePopup(amount, kind);

            if (IsDead)
            {
                currentHealth = 0;

                if (isElite)
                {
                    // 정예몹은 파티원 전원이 각자 흡수할 수 있어야 하므로(GDD '잡몹/정예몹 계층 상세') 바로
                    // 파괴하지 않고 시체로 남긴다. 시체는 통과 가능하게 바꾸고, 전원이 흡수하면 사라진다.
                    isDefeated = true;
                    ApplyCorpseState();
                    Debug.Log($"[MonsterStub] {name} (elite) defeated — corpse ready for absorption");
                }
                else
                {
                    NetworkServer.Destroy(gameObject);
                }
            }
        }

        private StatusEffectReceiver _status;

        private int ApplyVulnerable(int amount, DamagePopupKind kind)
        {
            if (!kind.IsDirectHit())
            {
                return amount;
            }

            if (_status == null)
            {
                _status = GetComponent<StatusEffectReceiver>();
            }

            return _status != null && _status.VulnerableStacks > 0
                ? Mathf.Max(1, Mathf.RoundToInt(amount * _status.VulnerableDamageMultiplier))
                : amount;
        }

        // 대미지 숫자(결정 ④B) — 서버에서만 호출. 호스트(서버+로컬 클라이언트)는 여기서 바로 띄운다: 호스트 모드에서
        // NetworkServer.Destroy는 NetworkClient.spawned에서 즉시 빼버려서, 큐에 쌓인 RPC가 도착할 때는 대상을 못 찾아
        // 버려진다(Mirror NetworkClient.OnRPCMessage). 원격 클라이언트는 RPC가 파괴 메시지보다 먼저 도착한다.
        private void ShowDamagePopup(int amount, DamagePopupKind kind)
        {
            if (amount <= 0)
            {
                return;
            }

            Vector3 pos = PopupPosition();
            // killed: 이번 타격으로 체력이 0이 됨(서버 값) — 사망 효과음용(2026-10-14). 원격은 파괴 메시지가 체력 SyncVar보다
            // 먼저 올 수 있어 스스로 판단할 수 없으므로 기존 RPC에 인자로 실어 보낸다(새 메시지 아님).
            bool killed = currentHealth <= 0;
            if (isClient)
            {
                DamagePopup.Spawn(pos, amount, kind);
                HitFx.OnDirectHit(gameObject, kind);
                PlayDeathSound(killed);
            }

            RpcShowDamage(amount, kind, pos, killed);
        }

        private void PlayDeathSound(bool killed)
        {
            if (killed)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.MonsterDie, transform.position);
            }
        }

        [ClientRpc]
        private void RpcShowDamage(int amount, DamagePopupKind kind, Vector3 pos, bool killed)
        {
            if (isServer)
            {
                return; // 호스트는 ShowDamagePopup에서 이미 띄웠다
            }

            DamagePopup.Spawn(pos, amount, kind);
            HitFx.OnDirectHit(gameObject, kind);
            PlayDeathSound(killed);
        }

        // 몸 스프라이트 윗부분 + 12px(로컬 기준, WorldHealthBar와 같은 계산) — 체력바(top+2px, 높이 4px)와 스택 라벨(top+7px) 위.
        private Vector3 PopupPosition()
        {
            var sprite = GetComponent<SpriteRenderer>();
            float top = sprite != null && sprite.sprite != null ? sprite.sprite.bounds.max.y : 0.5f;
            return transform.TransformPoint(new Vector3(0f, top + 12f * DamagePopup.Pixel, 0f));
        }

        /// <summary>정예몹 스탯 배율 적용(결정 대기 체크리스트 D). 스폰 직후, 실제 피해를 받기 전에
        /// 호출해야 한다 — EliteMonsterMarker.OnStartServer가 이 시점에 호출한다.</summary>
        public void ApplyEliteHealthMultiplier(float multiplier)
        {
            if (!isServer)
            {
                return;
            }

            maxHealth = Mathf.RoundToInt(maxHealth * multiplier);
            currentHealth = maxHealth;
        }

        /// <summary>정예몹 시체를 이 플레이어가 흡수할 수 있는지. 한 사람은 한 시체를 한 번만 흡수한다.</summary>
        public bool TryBeginAbsorbCorpse(NetworkIdentity absorber)
        {
            return isServer && isElite && isDefeated && !_corpseAbsorbedBy.Contains(absorber.netId);
        }

        /// <summary>시체 흡수 완료 기록. 지금 접속한 플레이어 전원이 흡수했으면 시체를 없앤다
        /// (혼자 플레이하면 흡수 즉시 사라진다).</summary>
        public void MarkCorpseAbsorbed(NetworkIdentity absorber)
        {
            if (!isServer)
            {
                return;
            }

            _corpseAbsorbedBy.Add(absorber.netId);
            foreach (var player in FindObjectsByType<AbsorbRoguelike.Player.PlayerController>(FindObjectsSortMode.None))
            {
                if (!_corpseAbsorbedBy.Contains(player.netIdentity.netId))
                {
                    return;
                }
            }

            NetworkServer.Destroy(gameObject);
        }

        /// <summary>
        /// 디졸브 잔상(2026-10-15, 비주얼 3단계 V9) — 각 화면 로컬 훅. 잡몹 처치(TakeDamage → NetworkServer.Destroy)·살아있는 잡몹 흡수(Absorb)·
        /// 정예 시체 전원 흡수 후 파괴(MarkCorpseAbsorbed) 세 경로가 모두 서버의 NetworkServer.Destroy로 끝나고, 호스트·원격 모두 여기로 들어온다.
        /// 몸 그림 복사본만 부서지며 사라진다(서버 로직·SyncVar 그대로). 층 이동 일괄 파괴·세션 종료·화면 밖은 DissolveGhost가 건너뛴다.
        /// 시체 어둡게(color×0.45)·정예 틴트는 color 복사로 그대로 이어지고, 피격 흔들림 중이면 원래 위치의 몸 그림을 쓴다.
        /// </summary>
        public override void OnStopClient()
        {
            var body = GetComponent<SpriteRenderer>();
            if (body != null)
            {
                DissolveGhost.Spawn(body, DissolveGhost.EdgeColorFor(gameObject));
            }
        }

        private void OnDefeatedChanged(bool _, bool defeated)
        {
            if (defeated)
            {
                ApplyCorpseState();
            }
        }

        // 시체는 길을 막지 않도록 충돌을 트리거로 바꾸고(흡수 감지는 유지), 어둡게 표시한다.
        private void ApplyCorpseState()
        {
            if (_corpseApplied)
            {
                return; // 호스트에서는 서버 호출과 SyncVar 훅이 둘 다 올 수 있다
            }

            _corpseApplied = true;
            foreach (var col in GetComponents<Collider2D>())
            {
                col.isTrigger = true;
            }

            var body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.bodyType = RigidbodyType2D.Kinematic;
            }

            var sprite = GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sprite.color = new Color(sprite.color.r * 0.45f, sprite.color.g * 0.45f, sprite.color.b * 0.45f, sprite.color.a);
            }
        }

        /// <summary>
        /// 잡몹 흡수(살아있는 상태에서 홀드 입력으로 바로 흡수)용 진입점. 대미지로 죽는 것과 별개 경로다.
        /// 실제 특성 적용은 호출부(AbsorptionController)가 TraitData를 읽어서 처리하고, 여기서는 흡수된
        /// 몬스터를 제거하는 것만 담당한다.
        /// </summary>
        public void Absorb()
        {
            if (!isServer)
            {
                return; // 흡수 판정도 서버(호스트)만 한다 — ADR-003
            }

            NetworkServer.Destroy(gameObject);
        }

        /// <summary>
        /// 흡수 홀드를 시작할 때 호출한다(결정 대기 체크리스트 C). 같은 잡몹을 두 명 이상이 동시에
        /// 흡수할 수 없다는 GDD 규칙을 지키기 위한 잠금 — 이미 다른 플레이어가 잠근 상태면 실패한다.
        /// 같은 플레이어가 다시 호출하는 건 항상 성공(재확인)으로 취급한다.
        /// </summary>
        public bool TryLock(NetworkIdentity locker)
        {
            if (!isServer || IsDead)
            {
                return false;
            }

            if (IsLocked && lockedByNetId != locker.netId)
            {
                return false;
            }

            lockedByNetId = locker.netId;
            return true;
        }

        /// <summary>홀드 확정(흡수 성공) 또는 취소 시 즉시 잠금을 푼다. 잠근 당사자만 풀 수 있다.</summary>
        public void Unlock(NetworkIdentity locker)
        {
            if (!isServer || lockedByNetId != locker.netId)
            {
                return;
            }

            lockedByNetId = 0;
        }

        public bool IsLockedByOther(uint viewerNetId)
        {
            return IsLocked && lockedByNetId != viewerNetId;
        }
    }
}
