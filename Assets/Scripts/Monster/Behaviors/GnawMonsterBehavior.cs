using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 갉아먹기(잡몹 패턴 2차 재작업, 2026-10-02 — 쥐, 결정 ㊷B). 붙어서 짧은 예고 → 작은 물기를 빠르게 반복한다(판정은 기본 근접 그대로 서버).
    /// 물기가 체력을 실제로 깎았을 때(DealDamage가 true — 가드·대시 무적·피격 무적·양털 보호막이면 안 쌓임) 그 플레이어에게 '갉힌 자국' +1,
    /// maxStacks째가 되는 물기는 대미지 × burstMultiplier를 넣고 자국을 0으로 되돌린다. 대상과 resetDistance 이상 벌어지거나
    /// 마지막 물기 후 resetTime초가 지나면 0, 대상이 바뀌면 이전 대상 자국은 0(자국은 이 쥐 한 마리 기준).
    /// 주기·사거리·예고는 기본 필드(attackRange·windupDuration·recoveryDuration·attackCooldown·attackAreaLength/Width)를 프리팹에서 짧게 둔다 —
    /// 1회 주기 = max(attackCooldown, windupDuration + recoveryDuration)가 플레이어 피격 무적(0.5초)보다 길어야 자국이 무적에 막히지 않는다.
    /// 표시: gnawStacks·gnawTargetNetId만 동기화하고, 각 화면이 대상 플레이어 머리 위에 자국 점(1~maxStacks개)을 로컬로 그린다.
    /// 수치 전부 [PLAYTEST].
    /// </summary>
    public class GnawMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 GnawMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private GnawMonsterConfig Cfg => Config<GnawMonsterConfig>();

        protected override System.Type ConfigType => typeof(GnawMonsterConfig);

        [Header("갉힌 자국 표시 [PLAYTEST]")]
        [SerializeField] private Color markColor = new Color(0.85f, 0.25f, 0.2f, 1f);
        [SerializeField] private float markRadius = 0.08f;
        [SerializeField] private float markSpacing = 0.22f;
        [Tooltip("대상 플레이어 중심에서 자국 줄까지의 높이.")]
        [SerializeField] private float markHeight = 0.75f;

        [SyncVar] private int gnawStacks;
        [SyncVar] private uint gnawTargetNetId;

        private PlayerHealth _stackTarget; // 서버 전용
        private float _lastBiteTime;
        private GameObject[] _marks;

        // 리팩터 C4: 수치를 config로 옮기며 손댔으므로 NetworkBehaviour.OnValidate를 가리던 CS0114도 정리(override + base 호출).
        protected override void OnValidate()
        {
            base.OnValidate();
            if (Stats == null)
            {
                return; // 에셋 연결 전(누락 경고는 Awake의 LogError가 맡는다)
            }

            float cycle = Mathf.Max(Stats.attackCooldown, Stats.windupDuration + Stats.recoveryDuration);
            if (cycle < 0.55f)
            {
                Debug.LogWarning($"[GnawMonster] {name}: 물기 주기 {cycle:0.00}s — 플레이어 피격 무적(0.5s)에 막혀 자국이 안 쌓일 수 있음");
            }
        }

        protected override void PerformAttack(Transform target)
        {
            if (!IsInArea(target.position, AttackDirection, Stats.attackAreaLength, Stats.attackAreaWidth, out _))
            {
                return;
            }

            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (health == null)
            {
                return;
            }

            if (health != _stackTarget)
            {
                ClearStacks(); // 대상이 바뀌면 이전 대상 자국은 0
                _stackTarget = health;
            }

            bool burst = gnawStacks + 1 >= Cfg.maxStacks;
            int damage = burst ? Mathf.RoundToInt(attackDamage * Cfg.burstMultiplier) : attackDamage;
            if (!DealDamage(health, damage))
            {
                return; // 막힌 물기는 자국에 영향 없음
            }

            _lastBiteTime = Time.time;
            gnawStacks = burst ? 0 : gnawStacks + 1;
            gnawTargetNetId = gnawStacks > 0 ? health.netId : 0u;
            Debug.Log($"[GnawMonster] {name} bites {target.name} for {damage}{(burst ? " (burst)" : string.Empty)} — marks {gnawStacks}/{Cfg.maxStacks}");
        }

        private void ClearStacks()
        {
            _stackTarget = null;
            gnawStacks = 0;
            gnawTargetNetId = 0u;
        }

        private void TickStackReset()
        {
            if (gnawStacks <= 0)
            {
                return;
            }

            bool lost = MonsterStub.IsDead || MonsterStub.IsDefeated || _stackTarget == null || _stackTarget.IsDead
                || Vector2.Distance(transform.position, _stackTarget.transform.position) >= Cfg.resetDistance
                || Time.time - _lastBiteTime > Cfg.resetTime;
            if (lost)
            {
                ClearStacks();
            }
        }

        protected override void UpdateClientVisuals()
        {
            if (isServer)
            {
                TickStackReset();
            }

            Transform target = null;
            int count = Mathf.Min(gnawStacks, Cfg.maxStacks);
            if (count > 0 && !MonsterStub.IsDead && !MonsterStub.IsDefeated && gnawTargetNetId != 0u
                && (NetworkClient.spawned.TryGetValue(gnawTargetNetId, out NetworkIdentity identity)
                    || NetworkServer.spawned.TryGetValue(gnawTargetNetId, out identity))
                && identity != null)
            {
                target = identity.transform;
            }

            if (target == null)
            {
                count = 0;
            }

            if (_marks == null)
            {
                if (count == 0)
                {
                    return;
                }

                _marks = new GameObject[Mathf.Max(1, Cfg.maxStacks)];
                var sortReference = target.GetComponent<SpriteRenderer>();
                for (int i = 0; i < _marks.Length; i++)
                {
                    _marks[i] = TelegraphUtil.CreateCircle(null, "GnawMark", markColor, null);
                    _marks[i].transform.localScale = Vector3.one * markRadius;
                    var sr = _marks[i].GetComponent<SpriteRenderer>();
                    if (sortReference != null)
                    {
                        sr.sortingLayerID = sortReference.sortingLayerID;
                        sr.sortingOrder = sortReference.sortingOrder + 5;
                    }
                }
            }

            Vector3 origin = target != null ? target.position + new Vector3(-(count - 1) * markSpacing * 0.5f, markHeight, 0f) : Vector3.zero;
            for (int i = 0; i < _marks.Length; i++)
            {
                bool show = i < count;
                _marks[i].SetActive(show);
                if (show)
                {
                    _marks[i].transform.position = origin + new Vector3(i * markSpacing, 0f, 0f);
                }
            }
        }

        // 자국 점은 몬스터 자식이 아니라(대상 위치를 따라가야 해서) 직접 정리한다.
        private void OnDestroy()
        {
            if (_marks == null)
            {
                return;
            }

            foreach (GameObject mark in _marks)
            {
                if (mark != null)
                {
                    Destroy(mark);
                }
            }

            _marks = null;
        }

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            Gizmos.color = new Color(0.85f, 0.25f, 0.2f);
            if (Cfg != null)
            {
                Gizmos.DrawWireSphere(transform.position, Cfg.resetDistance);
            }
        }
    }
}
