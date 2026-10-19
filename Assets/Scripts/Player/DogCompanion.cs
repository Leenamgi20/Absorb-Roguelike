using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 개 흡수 스킬 '충견'(GDD v2.33, 2026-09-29) — 개 특성을 든 플레이어를 따라다니는 동료 1마리.
    /// 서버 권위: 이동·대상 선택·물기 전부 서버가 하고, 위치는 프리팹의 NetworkTransform이 클라이언트로 옮긴다.
    /// - 대상: 소유자와 (협동 중이면) 팀원 전원의 PlayerController.Combat.LastHitTarget(PlayerCombat) 중 targetMemory(3초) 안에 맞은 가장 최근 대상.
    ///   그런 대상이 없으면 소유자에게서 autoTargetRange(6) 안의 가장 가까운 적(2026-10-12 — 직접 안 때려도 문다).
    ///   소유자에게서 너무 멀면(leashDistance) 쫓지 않는다.
    /// - 물기: 대상에 biteRange 안으로 붙으면 biteInterval(1초)마다 1회, 기본공격 × DogSkillConfig.dogBiteDamageScale —
    ///   소유자의 PlayerCombat.ServerApplySkillHit로 흡수 스킬 판정(보스 DamageSource.AbsorbSkill)·속성 적용. 넉백 없음.
    ///   적중 회복(ICD)은 주지 않는다 — 회복은 직접 휘두른 공격에만(도트와 같은 원칙). 개 물기는 최근 타격 기록에 남기지 않는다.
    /// - 대상이 없으면 소유자 옆(followOffset)으로 돌아온다. 너무 멀어지면(teleportDistance, 층 이동 등) 소유자 옆으로 순간이동.
    /// - 몸 충돌 없음: 프리팹에 콜라이더·Rigidbody가 없어 몬스터·벽과 부딪히지 않고, 공격 판정(OverlapCircle)에도 잡히지 않는다.
    /// 생애주기(스폰·제거)는 PlayerController.ServerUpdateDogCompanion. 수치는 전부 [PLAYTEST].
    /// 그림(2026-10-07): 사각 블럭 대신 플레이어 개 형태와 같은 `Art/Characters/Tal/tal_dog.png`를 작게(0.55배) — 적 개와 구분되게 슬라임 개.
    /// </summary>
    public class DogCompanion : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;          // [PLAYTEST] 플레이어(4.5)보다 조금 빠르게
        [SerializeField] private Vector2 followOffset = new Vector2(0.8f, -0.3f);
        [SerializeField] private float teleportDistance = 10f;  // [PLAYTEST]
        [SerializeField] private float leashDistance = 9f;      // [PLAYTEST] 소유자에게서 이보다 먼 대상은 쫓지 않는다
        [SerializeField] private float targetMemory = 3f;       // [PLAYTEST] 최근 몇 초 안에 맞은 적을 쫓는지
        [SerializeField] private float biteRange = 0.6f;        // [PLAYTEST] 대상 콜라이더 최근접점까지 거리
        [SerializeField] private float biteInterval = 1f;       // [PLAYTEST] 초당 1회
        [SerializeField] private float retargetInterval = 0.2f;
        [SerializeField] private float autoTargetRange = 6f;    // [PLAYTEST] 최근 타격 대상이 없으면 소유자에게서 이 거리 안의 가장 가까운 적을 문다(2026-10-12)
        [SerializeField] private LayerMask enemyLayers = 1 << 8; // Monster 레이어(잡몹·보스 모두 8)
        [Header("연출 (2026-10-16 — 따라올 때 끊김·물기 모션)")]
        [SerializeField] private float followSmoothTime = 0.15f; // 소유자 옆으로 부드럽게 감속(예전엔 멈췄다 달리기를 반복해 끊겨 보였음)
        [SerializeField] private float lungeDistance = 0.35f;     // 물 때 대상 쪽으로 튀어나가는 거리(칸)
        [SerializeField] private float lungeDuration = 0.2f;      // 튀어나갔다 돌아오는 시간(초)

        [SyncVar] private uint ownerNetId;

        private PlayerController _owner;
        private GameObject _target;
        private float _nextBiteTime;
        private float _biteDamageScale = 1f; // 서버: DogSkillConfig.dogBiteDamageScale(2026-10-14 프리팹에서 옮김, Initialize로 받음)
        private float _retargetTimer;
        private Vector2 _followVelocity;
        private float _lungeElapsed = -1f; // < 0 = 물기 모션 아님
        private Vector2 _lungeOrigin;
        private Vector2 _lungeDir;

        public uint OwnerNetId => ownerNetId;

        /// <summary>서버: NetworkServer.Spawn 전에 호출.</summary>
        public void Initialize(PlayerController owner, float biteDamageScale)
        {
            _owner = owner;
            _biteDamageScale = biteDamageScale;
            ownerNetId = owner != null ? owner.netId : 0;
        }

        private void Update()
        {
            if (!isServer)
            {
                return;
            }

            if (_owner == null)
            {
                NetworkServer.Destroy(gameObject); // 소유자가 나갔다
                return;
            }

            float dt = Time.deltaTime;
            Vector2 pos = transform.position;
            Vector2 ownerPos = _owner.transform.position;
            if (Vector2.Distance(pos, ownerPos) > teleportDistance)
            {
                transform.position = ownerPos + followOffset;
                _target = null;
                _lungeElapsed = -1f;
                _followVelocity = Vector2.zero;
                return;
            }

            // 물기 모션: 대상 쪽으로 튀어나갔다가(사인 곡선) 제자리로. 위치는 NetworkTransform으로 모든 화면에 보인다.
            if (_lungeElapsed >= 0f)
            {
                _lungeElapsed += dt;
                float k = Mathf.Clamp01(_lungeElapsed / lungeDuration);
                transform.position = _lungeOrigin + _lungeDir * (lungeDistance * Mathf.Sin(k * Mathf.PI));
                if (k >= 1f)
                {
                    _lungeElapsed = -1f;
                }

                return;
            }

            _retargetTimer -= dt;
            if (_retargetTimer <= 0f || (_target != null && !IsAlive(_target)))
            {
                _retargetTimer = retargetInterval;
                _target = FindTarget(ownerPos);
            }

            Vector2 goal;
            if (_target != null)
            {
                Collider2D col = _target.GetComponent<Collider2D>();
                Vector2 closest = col != null ? col.ClosestPoint(pos) : (Vector2)_target.transform.position;
                if (Vector2.Distance(pos, closest) <= biteRange)
                {
                    goal = pos; // 붙었으면 멈춰서 문다
                    if (Time.time >= _nextBiteTime)
                    {
                        _nextBiteTime = Time.time + biteInterval;
                        _owner.Combat.ServerApplySkillHit(_target, _biteDamageScale, pos, 0f, false);
                        _lungeOrigin = pos;
                        _lungeDir = (closest - pos).sqrMagnitude > 0.0001f ? (closest - pos).normalized : Vector2.right;
                        _lungeElapsed = 0f;
                        RpcBite(_target);
                    }
                }
                else
                {
                    goal = closest;
                }

                _followVelocity = Vector2.zero;
                transform.position = Vector2.MoveTowards(pos, goal, moveSpeed * dt);
                return;
            }

            // 따라가기: 멈췄다-달리기 대신 부드럽게 감속하며 붙는다(속도 상한 moveSpeed). 좌우 뒤집기는 MonsterFacing이 위치 변화로.
            // (followStopDistance로 멈추면 다시 출발할 때 속도가 0부터라 또 끊겨 보인다 — SmoothDamp가 알아서 수렴하게 둔다)
            goal = ownerPos + followOffset;
            transform.position = Vector2.SmoothDamp(pos, goal, ref _followVelocity, followSmoothTime, moveSpeed, dt);
        }

        // 물기 연출(각 화면): 대상 하얀 번쩍임 + 작은 스파크. 대미지 숫자는 기존 경로(스킬 판정)가 띄운다.
        [ClientRpc]
        private void RpcBite(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            AbsorbRoguelike.UI.HitWhiteFlash.Flash(target, shake: false);
            AbsorbRoguelike.UI.HitSparkPlayer.Spawn(target.transform.position + (Vector3)(Random.insideUnitCircle * 0.12f), "base", 0.6f);
        }

        // 소유자 + 팀원 전원의 최근 타격 기록 중 targetMemory 안의 가장 최근 살아있는 대상(소유자 leash 안).
        private GameObject FindTarget(Vector2 ownerPos)
        {
            GameObject best = null;
            float bestTime = float.NegativeInfinity;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                GameObject candidate = player.Combat.LastHitTarget;
                if (candidate == null || Time.time - player.Combat.LastHitTime > targetMemory || player.Combat.LastHitTime <= bestTime || !IsAlive(candidate))
                {
                    continue;
                }

                if (Vector2.Distance(ownerPos, candidate.transform.position) > leashDistance)
                {
                    continue;
                }

                best = candidate;
                bestTime = player.Combat.LastHitTime;
            }

            return best != null ? best : FindNearestEnemy(ownerPos);
        }

        // 최근 타격 대상이 없을 때(2026-10-12 사용자 요청 — 한 번 때리고 안 때리면 개가 거의 안 물었음): 소유자에게서 autoTargetRange 안의
        // 가장 가까운 살아 있는 적. 몬스터는 방에 들어가야 생성되므로 아직 안 연 방의 적은 잡히지 않는다.
        private GameObject FindNearestEnemy(Vector2 ownerPos)
        {
            GameObject nearest = null;
            float nearestSqr = float.MaxValue;
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(ownerPos, autoTargetRange, enemyLayers))
            {
                GameObject candidate = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
                if (!IsAlive(candidate))
                {
                    continue;
                }

                float sqr = ((Vector2)candidate.transform.position - ownerPos).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        private static bool IsAlive(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            var monster = target.GetComponent<MonsterStub>();
            if (monster != null)
            {
                return !monster.IsDead;
            }

            var boss = target.GetComponent<BossStub>();
            return boss != null && !boss.IsDefeated;
        }
    }
}
