using AbsorbRoguelike.Player;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 제자리 브레스(잡몹 패턴 2차, 2026-10-02 — 용). 이동 중에는 공격을 시작하지 않는다: 브레스 사거리(breathTriggerRange)에 들어오면
    /// 멈추고 settleDuration 동안 서 있은 뒤에만 부채꼴 예고(반경 breathRadius, 전체 각도 breathAngle)를 breathWindupDuration 동안 띄운다
    /// (진하기 규칙 ㊶ — windupSerial/windupLength 그대로). 예고가 끝난 순간 부채꼴 안(거리 + 각도, 보스 휘두르기와 같은 판정)에 있는
    /// 모든 플레이어가 맞는다(서버). 예고 모양은 각 화면이 attackDirection SyncVar 기준으로 로컬에서 그린다. 수치 전부 [PLAYTEST].
    /// </summary>
    public class BreathMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 BreathMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private BreathMonsterConfig Cfg => Config<BreathMonsterConfig>();

        protected override System.Type ConfigType => typeof(BreathMonsterConfig);

        private const float PlayerHalfSize = 0.4f; // 보스 휘두르기 부채꼴 판정과 같은 여유
        private float _settled;

        protected override GameObject CreateTelegraphVisual()
        {
            return TelegraphUtil.CreateCone(transform, "BreathTelegraph", Cfg.breathAngle * 0.5f, telegraphColor, GetComponent<SpriteRenderer>());
        }

        protected override void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            Vector3 parentScale = transform.lossyScale;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            telegraph.transform.localPosition = Vector3.zero;
            telegraph.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            telegraph.transform.localScale = new Vector3(Cfg.breathRadius / parentScale.x, Cfg.breathRadius / parentScale.y, 1f);
        }

        protected override float FirstWindupDuration => Cfg.breathWindupDuration;

        // 브레스는 예고가 끝나는 순간 한 번에 판정한다(지속 없음) → 그 순간 1초 단발 브레스 소리(2026-10-14).
        protected override string WindupEndSfxId => AbsorbRoguelike.Audio.SfxId.DragonBreath;

        // 사거리 밖이면 쫓아가고(정지 타이머 초기화), 안이면 멈춰 서서(Idle = 속도 0) settleDuration이 지난 뒤에만 공격 상태로 넘어간다.
        protected override State DecideState(Transform target)
        {
            if (target == null)
            {
                _settled = 0f;
                return State.Idle;
            }

            if (Vector2.Distance(transform.position, target.position) > Cfg.breathTriggerRange)
            {
                _settled = 0f;
                return State.Chase;
            }

            _settled += Time.deltaTime;
            return _settled >= Cfg.settleDuration ? State.Attack : State.Idle;
        }

        protected override void OnAttackStarted(Transform target)
        {
            _settled = 0f; // 후딜 후퇴(이동) 뒤에는 다시 멈춰 서야 다음 브레스
        }

        protected override void OnWindupFinished(Transform target)
        {
            Vector2 origin = transform.position;
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                Vector2 offset = (Vector2)health.transform.position - origin;
                if (offset.magnitude > Cfg.breathRadius + PlayerHalfSize)
                {
                    continue;
                }

                if (offset.magnitude <= 1f || Vector2.Angle(AttackDirection, offset) <= Cfg.breathAngle * 0.5f)
                {
                    DealDamage(health, attackDamage);
                }
            }

            Debug.Log($"[BreathMonster] {name} breath ({attackDamage})");
        }
    }
}
