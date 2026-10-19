using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 투사체 부품 ④(원숭이): 공통 패턴(MonsterBehaviorController)은 그대로 쓰되, "공격 범위 안에서 어떻게
    /// 움직이는가"와 "공격을 어떻게 하는가"만 바꿔치기한다. 플레이어와 거리를 유지하려 하다가(keepDistance보다
    /// 가까워지면 물러남) 사거리 안이면 그 자리에서 발사 방향 직선 예고(진하기 규칙 ㊶) 후 투사체를 쏜다.
    /// 2026-10-02: 즉시 명중 TODO 제거 — 서버가 MonsterProjectile을 NetworkServer.Spawn해 날려 보내고, 피하면 안 맞는다.
    /// 감지/추적/공격 쿨타임 같은 공통 로직은 베이스 클래스가 그대로 담당한다.
    /// </summary>
    public class RangedMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 RangedMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private RangedMonsterConfig Cfg => Config<RangedMonsterConfig>();

        protected override System.Type ConfigType => typeof(RangedMonsterConfig);

        [Header("투사체 부품 ④ [PLAYTEST]")]
        [Tooltip("MonsterProjectile 프리팹(NetworkIdentity 포함, NetworkManager spawnPrefabs에 등록돼 있어야 한다).")]
        [SerializeField] private GameObject projectilePrefab;
        [Tooltip("발사 직선 예고 폭(칸). 길이는 투사체 사거리.")]
        [SerializeField] private float projectileTelegraphWidth = 0.4f;

        protected override void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            float length = Stats.attackAreaLength;
            if (projectilePrefab != null)
            {
                var projectile = projectilePrefab.GetComponent<MonsterProjectile>();
                if (projectile != null)
                {
                    length = projectile.Range + Cfg.muzzleOffset;
                }
            }

            TelegraphUtil.PlaceLine(telegraph, direction, length, projectileTelegraphWidth);
        }

        protected override void ChaseTarget(Transform target)
        {
            float distance = Vector2.Distance(transform.position, target.position);

            if (distance < Cfg.keepDistance)
            {
                Vector2 awayDirection = ((Vector2)(transform.position - target.position)).normalized;
                Rigidbody.linearVelocity = awayDirection * CurrentMoveSpeed; // 둔화(뱀·냉기)가 물러날 때도 먹도록
                return;
            }

            base.ChaseTarget(target);
        }

        // 예고가 끝나면 대상 위치와 상관없이 예고했던 방향으로 발사한다(예고를 보고 비켜서면 피함).
        protected override void OnWindupFinished(Transform target)
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[RangedMonsterBehavior] {name} has no projectilePrefab — attack skipped");
                return;
            }

            Vector2 dir = AttackDirection;
            Vector3 spawnPos = transform.position + (Vector3)(dir * Cfg.muzzleOffset);
            GameObject go = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            var projectile = go.GetComponent<MonsterProjectile>();
            if (projectile != null)
            {
                projectile.Initialize(dir, attackDamage);
            }

            NetworkServer.Spawn(go);
            Debug.Log($"[RangedMonsterBehavior] {name} fires a projectile ({attackDamage})");
        }
    }
}
