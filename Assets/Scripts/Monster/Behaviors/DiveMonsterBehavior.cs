using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 급강하(잡몹 패턴 2차, 2026-10-02 — 닭, 결정 ㊲A). diveTriggerRange 안에 플레이어가 들어오면 공격 시작 순간 그 플레이어 위치를
    /// 착지점으로 고정(landingPoint SyncVar, 벽 너머면 Physics2D 레이캐스트로 벽 앞까지 잘라냄) → 착지점에 원형 예고(반경 landingRadius,
    /// diveWindupDuration, 진하기 규칙 ㊶) → 예고가 끝나면 서버가 diveDuration 동안 Rigidbody2D.MovePosition으로 착지점까지 보간 이동
    /// (원격은 기존 NetworkTransform으로 보임) → 착지 순간 원 안 플레이어 피해 → landPause 정지.
    /// 연출: 몸 스프라이트가 루트에 있어 자식으로 띄울 수 없으므로, 예고 동안 각 화면이 몸 스프라이트를 복사한 자식을 로컬로 만들어
    /// 진행률(windupSerial/windupLength 기반 WindupProgress)만큼 위로 띄우고 몸 렌더러는 잠깐 끈다. 예고가 끝나면 diveDuration 동안
    /// 로컬 시간으로 내려온다(새 SyncVar 없음). 수치 전부 [PLAYTEST].
    /// </summary>
    public class DiveMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 DiveMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private DiveMonsterConfig Cfg => Config<DiveMonsterConfig>();

        protected override System.Type ConfigType => typeof(DiveMonsterConfig);

        [Header("상승 연출")]
        [SerializeField] private float liftHeight = 0.5f;

        private const float PlayerHalfSize = 0.5f; // 보스 원형 충격파 판정과 같은 여유

        [SyncVar] private Vector2 landingPoint;

        private bool _diving;
        private float _diveElapsed;
        private Vector2 _diveStart;
        private float _pauseRemaining;

        // 상승 연출(로컬)
        private SpriteRenderer _body;
        private SpriteRenderer _liftCopy;
        private bool _wasWindingUp;
        private float _descendStartLocal = -999f;

        protected override GameObject CreateTelegraphVisual()
        {
            return TelegraphUtil.CreateCircle(transform, "DiveTelegraph", telegraphColor, GetComponent<SpriteRenderer>());
        }

        // 원은 몬스터 자식이지만 월드 착지점에 고정해서 그린다.
        protected override void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            Vector3 parentScale = transform.lossyScale;
            telegraph.transform.position = new Vector3(landingPoint.x, landingPoint.y, transform.position.z);
            telegraph.transform.rotation = Quaternion.identity;
            telegraph.transform.localScale = new Vector3(Cfg.landingRadius / parentScale.x, Cfg.landingRadius / parentScale.y, 1f);
        }

        protected override float FirstWindupDuration => Cfg.diveWindupDuration;

        protected override State DecideState(Transform target)
        {
            if (target == null)
            {
                return State.Idle;
            }

            return Vector2.Distance(transform.position, target.position) <= Cfg.diveTriggerRange ? State.Attack : State.Chase;
        }

        // StartWindup(isWindingUp = true)보다 먼저 불려 착지점이 같은 동기화 묶음으로 간다.
        protected override void OnAttackStarted(Transform target)
        {
            Vector2 from = Rigidbody.position;
            Vector2 to = target != null ? (Vector2)target.position : from;
            landingPoint = ClipToWall(from, to);
        }

        private Vector2 ClipToWall(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f)
            {
                return to;
            }

            Vector2 dir = delta / distance;
            float nearest = distance;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(from, dir, distance))
            {
                Collider2D col = hit.collider;
                if (col == null || col.isTrigger || col.GetComponent<MonsterStub>() != null || col.GetComponent<PlayerController>() != null)
                {
                    continue; // 방 트리거·몬스터 몸(자기 자신 포함)·플레이어는 벽이 아니다
                }

                nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance - Cfg.wallMargin));
            }

            return from + dir * nearest;
        }

        protected override void OnWindupFinished(Transform target)
        {
            RecoveryRemaining = 0f; // 후딜 후퇴 대신 착지 정지
            _diving = true;
            _diveElapsed = 0f;
            _diveStart = Rigidbody.position;
            InAttackSequence = true;
        }

        protected override void TickAttackSequence()
        {
            Rigidbody.linearVelocity = Vector2.zero;
            if (_diving)
            {
                _diveElapsed += Time.deltaTime;
                float t = Cfg.diveDuration > 0f ? Mathf.Clamp01(_diveElapsed / Cfg.diveDuration) : 1f;
                Rigidbody.MovePosition(Vector2.Lerp(_diveStart, landingPoint, t));
                if (t >= 1f)
                {
                    _diving = false;
                    _pauseRemaining = Cfg.landPause;
                    Land();
                }

                return;
            }

            _pauseRemaining -= Time.deltaTime;
            if (_pauseRemaining <= 0f)
            {
                InAttackSequence = false; // 다음 공격 쿨다운은 여기서부터 흐른다
            }
        }

        private void Land()
        {
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                if (Vector2.Distance(landingPoint, health.transform.position) <= Cfg.landingRadius + PlayerHalfSize)
                {
                    DealDamage(health, attackDamage);
                }
            }

            Debug.Log($"[DiveMonster] {name} lands at {landingPoint} ({attackDamage})");
        }

        protected override void OnAttackSequenceCancelled()
        {
            _diving = false;
        }

        // 상승 연출 — 모든 화면에서 동기화 값(isWindingUp·windupSerial/windupLength)만으로 같은 계산.
        protected override void UpdateClientVisuals()
        {
            bool alive = !MonsterStub.IsDead && !MonsterStub.IsDefeated;
            bool winding = IsWindingUp && alive;
            if (_wasWindingUp && !winding)
            {
                _descendStartLocal = Time.time;
            }

            _wasWindingUp = winding;

            float height = 0f;
            if (winding)
            {
                height = liftHeight * WindupProgress;
            }
            else if (alive && Cfg.diveDuration > 0f && Time.time - _descendStartLocal < Cfg.diveDuration)
            {
                height = liftHeight * (1f - (Time.time - _descendStartLocal) / Cfg.diveDuration);
            }

            if (_body == null)
            {
                _body = GetComponent<SpriteRenderer>();
                if (_body == null)
                {
                    return;
                }
            }

            bool lifted = height > 0.001f;
            if (_liftCopy == null)
            {
                if (!lifted)
                {
                    return;
                }

                var go = new GameObject("DiveLiftSprite");
                go.transform.SetParent(transform, false);
                _liftCopy = go.AddComponent<SpriteRenderer>();
                _liftCopy.sortingLayerID = _body.sortingLayerID;
                _liftCopy.sortingOrder = _body.sortingOrder;
                _liftCopy.enabled = false; // 아래에서 lifted로 켜면서 몸 렌더러를 끈다
            }

            if (_liftCopy.enabled != lifted)
            {
                // 바뀔 때만 몸 렌더러를 끄고 켠다(다른 시스템의 렌더러 상태를 매 프레임 덮어쓰지 않게).
                _liftCopy.enabled = lifted;
                _body.enabled = !lifted;
            }

            if (lifted)
            {
                _liftCopy.sprite = _body.sprite;
                _liftCopy.flipX = _body.flipX;
                _liftCopy.color = _body.color;
                _liftCopy.transform.localPosition = new Vector3(0f, height, 0f);
            }
        }
    }
}
