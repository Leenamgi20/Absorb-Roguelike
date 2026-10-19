using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 원숭이 흡수 스킬 '바나나 던지기'(GDD v2.33, 결정 ㉘-2 c, 2026-09-29). SpitProjectile과 같은 구조 — 스폰 때 받은 방향으로
    /// 똑바로 날아가며(모든 클라이언트가 같은 직선을 각자 그린다), 적중 판정은 서버만 한다.
    /// - 사거리 range(약 6칸) = speed × 수명. 첫 명중(적) 또는 벽(트리거가 아닌 콜라이더)에서 사라진다.
    /// - 명중 처리는 던진 플레이어의 PlayerCombat.ServerOnBananaHit(PlayerController.Combat)에 맡긴다 — 기본공격 70%, 흡수 스킬 판정(보스 DamageSource.AbsorbSkill),
    ///   속성 적용, 잡몹만 knockbackDistance(약 0.8칸) 미끄러지듯 밀림(보스는 밀지 않음), 적중 회복은 체력이 실제로 깎였을 때만.
    /// 수치는 전부 [PLAYTEST].
    /// </summary>
    public class BananaProjectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 10f;              // [PLAYTEST]
        [SerializeField] private float range = 6f;               // [PLAYTEST] 칸
        [SerializeField] private float hitRadius = 0.3f;         // [PLAYTEST]
        [SerializeField] private float knockbackDistance = 0.8f; // [PLAYTEST] 잡몹 미끄러짐 거리(칸)
        [SerializeField] private float spinDegreesPerSecond = 720f; // 연출용 회전

        [SyncVar] private Vector2 direction;

        private PlayerController _owner;
        private float _damageScale = 1f; // 서버: MonkeySkillConfig.bananaDamageScale(2026-10-14 프리팹에서 옮김, Initialize로 받음)
        private float _traveled;

        public float Range => range;

        /// <summary>NetworkServer.Spawn 전에 호출해야 방향이 스폰 정보에 같이 실려 클라이언트로 간다.</summary>
        public void Initialize(Vector2 dir, PlayerController owner, float damageScale)
        {
            direction = dir.normalized;
            _owner = owner;
            _damageScale = damageScale;
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
            if (_traveled >= range || _owner == null)
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
                if (col.GetComponent<PlayerController>() != null)
                {
                    continue; // 플레이어(본인·팀원)는 통과
                }

                MonsterStub monster = col.GetComponent<MonsterStub>();
                BossStub boss = col.GetComponent<BossStub>();
                bool liveEnemy = (monster != null && !monster.IsDead) || (boss != null && !boss.IsDefeated);
                if (liveEnemy)
                {
                    // 넉백 기준점을 날아온 쪽 뒤로 잡아 맞은 적이 비행 방향으로 미끄러지게 한다.
                    Vector2 from = (Vector2)transform.position - direction;
                    _owner.Combat.ServerOnBananaHit(col.gameObject, from, _damageScale, knockbackDistance);
                    NetworkServer.Destroy(gameObject);
                    return;
                }

                if (col.isTrigger)
                {
                    continue; // 방 트리거·정예 시체·떨어진 패시브 같은 트리거는 통과
                }

                NetworkServer.Destroy(gameObject); // 벽
                return;
            }
        }
    }
}
