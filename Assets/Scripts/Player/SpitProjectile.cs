using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 머금은 파편을 뱉은 투사체. 스폰 시 받은 방향으로 똑바로 날아가며(모든 클라이언트가 같은 직선을 각자
    /// 그린다), 적중 판정은 서버만 한다. 보스에게는 DamageSource.Spit으로 큰 대미지(보스 대미지 규칙),
    /// 잡몹에게도 같은 대미지. 벽에 닿거나 수명이 끝나면 사라진다.
    /// </summary>
    public class SpitProjectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 12f; // [PLAYTEST]
        [SerializeField] private float lifetime = 1f; // [PLAYTEST] 사거리 = speed × lifetime
        [SerializeField] private float hitRadius = 0.3f;
        [SerializeField] private int bossDamage = 20; // [PLAYTEST] 2026-10-07 30 → 20(파편이 패턴마다 나오도록 바뀌어서)
        [SerializeField] private int monsterDamage = 20; // [PLAYTEST] 2026-10-07 30 → 20

        [SyncVar] private Vector2 direction;

        private NetworkIdentity _owner;
        private float _age;

        public float Range => speed * lifetime;

        /// <summary>파편 뱉기 효과음(2026-10-14): 스폰이 각 화면(호스트 포함)에 한 번씩 도착하는 순간.</summary>
        public override void OnStartClient()
        {
            AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.DebrisSpit, transform.position);
        }

        /// <summary>NetworkServer.Spawn 전에 호출해야 방향이 스폰 정보에 같이 실려 클라이언트로 간다.</summary>
        public void Initialize(Vector2 dir, NetworkIdentity owner)
        {
            direction = dir.normalized;
            _owner = owner;
        }

        private void Update()
        {
            transform.position += (Vector3)(direction * (speed * Time.deltaTime));

            if (!isServer)
            {
                return;
            }

            _age += Time.deltaTime;
            if (_age >= lifetime)
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
                if (col.isTrigger || col.GetComponent<PlayerController>() != null)
                {
                    continue; // 방 트리거·시체·파편 같은 트리거와 플레이어는 통과
                }

                BossStub boss = col.GetComponent<BossStub>();
                MonsterStub monster = col.GetComponent<MonsterStub>();
                if (boss != null)
                {
                    boss.TakeDamage(bossDamage, _owner, DamageSource.Spit);
                }
                else if (monster != null)
                {
                    monster.TakeDamage(monsterDamage, (Vector2)transform.position); // 공격 출처 — 정면 방어 몬스터 판정용
                }

                NetworkServer.Destroy(gameObject); // 적이든 벽이든 처음 닿은 곳에서 사라진다
                return;
            }
        }
    }
}
