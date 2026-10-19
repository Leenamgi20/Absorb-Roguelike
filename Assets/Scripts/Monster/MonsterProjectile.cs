using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 몬스터 전용 투사체(투사체 부품 ④, 2026-10-02 — 원숭이 바나나). 플레이어 쪽 BananaProjectile과 같은 동기화 구조:
    /// 스폰 때 받은 방향(SyncVar)으로 모든 화면이 각자 직선을 그리고, 적중 판정·소멸은 서버만 한다.
    /// - 플레이어에게 닿으면 PlayerHealth.TryTakeDamage(대미지, 날아온 쪽 위치) 1회 후 소멸(가드 방향 판정이 날아온 방향 기준으로 적용).
    /// - 벽(트리거가 아닌 콜라이더, 몬스터 몸 제외)에 닿거나 사거리를 다 가면 소멸.
    /// 수치 전부 [PLAYTEST].
    /// </summary>
    public class MonsterProjectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 7f;          // [PLAYTEST]
        [SerializeField] private float range = 6f;          // [PLAYTEST] 칸
        [SerializeField] private float hitRadius = 0.3f;    // [PLAYTEST]
        [SerializeField] private float spinDegreesPerSecond = 540f; // 연출용 회전

        [SyncVar] private Vector2 direction;

        private int _damage;
        private float _traveled;

        public float Speed => speed;
        public float Range => range;

        /// <summary>NetworkServer.Spawn 전에 호출해야 방향이 스폰 정보에 같이 실려 클라이언트로 간다.</summary>
        public void Initialize(Vector2 dir, int damage)
        {
            direction = dir.normalized;
            _damage = damage;
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;
            transform.position += (Vector3)(direction * step);
            transform.Rotate(0f, 0f, spinDegreesPerSecond * Time.deltaTime);

            if (!isServer)
            {
                return;
            }

            _traveled += step;
            if (_traveled >= range)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            CheckHits();
        }

        private void CheckHits()
        {
            foreach (Collider2D col in Physics2D.OverlapCircleAll(transform.position, hitRadius))
            {
                PlayerHealth health = col.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    if (health.IsDead)
                    {
                        continue;
                    }

                    // 날아온 쪽 뒤를 공격자 위치로 넘겨 가드 정면 판정이 비행 방향 기준이 되게 한다. 막혀도(가드·대시) 소멸.
                    // 층 대미지 배율은 명중 시점에 한 번만(발사 측 RangedMonsterBehavior는 원래 공격력을 넘긴다, 2026-10-13).
                    health.TryTakeDamage(AbsorbRoguelike.Dungeon.DifficultyScaling.MonsterDamage(_damage), (Vector2)transform.position - direction);
                    NetworkServer.Destroy(gameObject);
                    return;
                }

                if (col.isTrigger || col.GetComponent<MonsterStub>() != null)
                {
                    continue; // 방 트리거·시체·자기 콜라이더·다른 몬스터 몸은 통과
                }

                NetworkServer.Destroy(gameObject); // 벽
                return;
            }
        }
    }
}
