using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 정면 방어 부품 ⑤(잡몹 패턴 1차, 2026-10-02 — 돼지). 공격자 방향과 몬스터 정면(facing) 사이 각이 shieldHalfAngle 이내면
    /// 받는 대미지 × frontDamageMultiplier(MonsterStub.TakeDamage(amount, attackerPosition) → ModifyIncomingDamage, 위치 없는 도트·방전은 대상 아님).
    /// 막은 순간(반격 재사용 대기 counterCooldown) 공격자 쪽으로 짧은 예고(counterWindupDuration)의 반격 1회를 바로 시작한다.
    /// 표시: 정면 쪽 막대(방패)를 각 화면이 로컬 생성해 facing SyncVar 기준으로 돌리고, 막을 때마다 blockSerial SyncVar가 1 올라
    /// 모든 화면에서 짧게 번쩍인다. 정면 = 예고 중이면 공격 방향, 아니면 가장 가까운 플레이어 쪽(서버가 각도 변화가 클 때만 갱신).
    /// 2026-10-07 공략 틈(사용자 결정 1+3): ① 방패는 초당 shieldTurnSpeed도까지만 돈다 — 플레이어가 돌아 들어가면(공격 거리에서
    /// 약 200°/초) 방패가 늦게 따라와 옆·등을 칠 수 있다. ② 반격을 휘두른 직후 shieldDownDuration 동안 방패를 내린다(정면 감소 없음,
    /// 반투명 회색으로 처진 표시 — shieldDown SyncVar). 각 화면은 받은 정면 쪽으로 같은 속도로 부드럽게 돌려 그린다.
    /// 수치 전부 [PLAYTEST].
    /// </summary>
    public class ShieldMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 ShieldMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private ShieldMonsterConfig Cfg => Config<ShieldMonsterConfig>();

        protected override System.Type ConfigType => typeof(ShieldMonsterConfig);

        [Header("정면 방어 부품 ⑤ [PLAYTEST]")]
        [Tooltip("정면 갱신 최소 각도 변화(동기화량 절약).")]
        [SerializeField] private float facingSyncMinAngle = 8f;

        [Header("방패 표시")]
        [SerializeField] private Color shieldColor = new Color(0.75f, 0.6f, 0.35f, 0.9f);
        [SerializeField] private Color shieldFlashColor = new Color(1f, 1f, 0.85f, 1f);
        [SerializeField] private float shieldFlashDuration = 0.15f;
        [SerializeField] private float shieldOffset = 0.5f;
        [SerializeField] private Vector2 shieldSize = new Vector2(0.15f, 0.8f);
        [SerializeField] private Color shieldDownColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);

        [SyncVar] private Vector2 facing = Vector2.down;
        [SyncVar] private byte blockSerial;
        [SyncVar] private bool shieldDown;

        private Vector2 _serverFacing = Vector2.down; // 서버가 회전 속도 제한으로 돌리는 실제 정면(판정용) — facing은 이걸 띄엄띄엄 동기화
        private Vector2 _shownFacing = Vector2.down;  // 각 화면 표시용(받은 facing 쪽으로 같은 속도로 회전)
        private bool _counterActive;
        private float _shieldDownUntil;

        private float _counterReadyTime;
        private GameObject _shield;
        private SpriteRenderer _shieldRenderer;
        private byte _seenBlockSerial;
        private float _flashRemaining;

        protected override void Awake()
        {
            base.Awake();
            _shield = TelegraphUtil.CreateRect(transform, "FrontShield", new Vector2(0.5f, 0.5f), shieldColor, GetComponent<SpriteRenderer>());
            _shieldRenderer = _shield.GetComponent<SpriteRenderer>();
            _shieldRenderer.sortingOrder += 2; // 몸 앞에 그린다(CreateRect 기본은 몸 뒤 -1)
        }

        public override int ModifyIncomingDamage(Vector2 attackerPosition, int damage)
        {
            if (!isServer || MonsterStub.IsDead)
            {
                return damage;
            }

            Vector2 toAttacker = attackerPosition - (Vector2)transform.position;
            if (shieldDown || toAttacker.sqrMagnitude < 0.0001f || Vector2.Angle(_serverFacing, toAttacker) > Cfg.shieldHalfAngle)
            {
                return damage;
            }

            unchecked
            {
                blockSerial++;
            }

            TryStartCounter(attackerPosition);
            return Mathf.Max(1, Mathf.RoundToInt(damage * Cfg.frontDamageMultiplier));
        }

        private void TryStartCounter(Vector2 attackerPosition)
        {
            if (Time.time < _counterReadyTime || IsWindingUp || InAttackSequence)
            {
                return;
            }

            _counterReadyTime = Time.time + Cfg.counterCooldown;
            Transform target = null;
            float best = float.MaxValue;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (IsDownedOrDead(player))
                {
                    continue; // 쓰러진 플레이어에게는 반격하지 않는다
                }

                float d = Vector2.Distance(player.transform.position, attackerPosition);
                if (d < best)
                {
                    best = d;
                    target = player.transform;
                }
            }

            RecoveryRemaining = 0f;
            _counterActive = true;
            StartWindup(target, attackerPosition - (Vector2)transform.position, Cfg.counterWindupDuration);
            Debug.Log($"[ShieldMonster] {name} blocked from the front — counter");
        }

        protected override void UpdateClientVisuals()
        {
            if (isServer && !MonsterStub.IsDead)
            {
                UpdateFacingServer();
            }

            if (blockSerial != _seenBlockSerial)
            {
                _seenBlockSerial = blockSerial;
                _flashRemaining = shieldFlashDuration;
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.ShieldBlock, transform.position); // 방패 번쩍임과 같은 순간(각 화면 1회)
            }

            bool show = !MonsterStub.IsDead && !MonsterStub.IsDefeated;
            _shield.SetActive(show);
            if (!show)
            {
                return;
            }

            Vector2 target = facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector2.down;
            _shownFacing = RotateTowards(_shownFacing, target, Cfg.shieldTurnSpeed * 1.5f * Time.deltaTime); // 동기화 계단을 메우려 조금 빠르게
            Vector2 dir = _shownFacing;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _shield.transform.localPosition = dir * shieldOffset;
            _shield.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            _shield.transform.localScale = new Vector3(shieldSize.x, shieldSize.y, 1f);

            if (shieldDown)
            {
                _shieldRenderer.color = shieldDownColor;
                _shield.transform.localPosition = dir * (shieldOffset * 0.6f) + Vector2.down * 0.2f; // 처진 방패
            }
            else if (_flashRemaining > 0f)
            {
                _flashRemaining -= Time.deltaTime;
                _shieldRenderer.color = shieldFlashColor;
            }
            else
            {
                _shieldRenderer.color = shieldColor;
            }
        }

        private void UpdateFacingServer()
        {
            // 반격(예고 + 휘두르기)이 끝나는 순간부터 방패를 내린다.
            if (_counterActive && !IsWindingUp && !InAttackSequence)
            {
                _counterActive = false;
                _shieldDownUntil = Time.time + Cfg.shieldDownDuration;
            }

            bool down = Time.time < _shieldDownUntil;
            if (down != shieldDown)
            {
                shieldDown = down;
            }

            Vector2 desired;
            if (IsWindingUp || InAttackSequence)
            {
                desired = AttackDirection;
            }
            else
            {
                Transform target = FindNearestPlayer();
                if (target == null)
                {
                    return;
                }

                desired = (Vector2)target.position - (Vector2)transform.position;
            }

            if (desired.sqrMagnitude > 0.0001f)
            {
                _serverFacing = RotateTowards(_serverFacing, desired.normalized, Cfg.shieldTurnSpeed * Time.deltaTime);
            }

            if (Vector2.Angle(facing, _serverFacing) >= facingSyncMinAngle)
            {
                facing = _serverFacing;
            }
        }

        private static Vector2 RotateTowards(Vector2 from, Vector2 to, float maxDegrees)
        {
            if (from.sqrMagnitude < 0.0001f)
            {
                return to;
            }

            float delta = Mathf.Clamp(Vector2.SignedAngle(from, to), -maxDegrees, maxDegrees);
            return (Vector2)(Quaternion.Euler(0f, 0f, delta) * from.normalized);
        }
    }
}
