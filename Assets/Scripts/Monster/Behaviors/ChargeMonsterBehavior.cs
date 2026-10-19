using System.Collections.Generic;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 돌진 부품 ①(잡몹 패턴 1차, 2026-10-02 — 소·말). 돌진 사거리 안에 플레이어가 들어오면 그 방향으로 직선 예고(길이 = 돌진 거리)
    /// → 예고가 끝나면 고정 방향으로 chargeSpeed 돌진(서버, Rigidbody2D 속도 — 원격은 NetworkTransform ServerToClient로 보임)
    /// → 경로상 플레이어는 돌진 1회당 1번만 맞는다 → 벽에 부딪히면 wallStunDuration 경직(파란 오버레이, isWallStunned SyncVar),
    /// 벽 없이 거리를 다 가면 짧게 정지. chargeCount ≥ 2면 중간 돌진 뒤 경직 없이 (reaim이면 가장 가까운 플레이어 쪽으로) 짧은 재예고 후 재돌진,
    /// 마지막 돌진만 벽 경직을 받는다(보스 패턴 C와 같은 규칙). 수치 전부 [PLAYTEST].
    /// 개(잡몹 패턴 2차 재작업, 결정 ㊸A)용 옵션 — 기본값은 꺼짐이라 소·말은 그대로: barkIcon(예고 동안 머리 위 느낌표, 각 화면이
    /// IsWindingUp 동기화 값으로 로컬 표시), retreatAfterCharge(마지막 돌진이 끝나 정지/경직한 뒤 돌진 반대 방향으로 짧게 후퇴 —
    /// 대미지 없음, 벽에 막히면 그 자리에서 멈춤 — 후퇴가 끝나야 다음 공격 쿨다운이 흐른다).
    /// </summary>
    public class ChargeMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 ChargeMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private ChargeMonsterConfig Cfg => Config<ChargeMonsterConfig>();

        protected override System.Type ConfigType => typeof(ChargeMonsterConfig);

        // 개(barkIcon 켜진 돌진)는 공통 예고음 대신 짖는 소리 하나만(2026-10-14).
        protected override string WarnSfxId => barkIcon ? AbsorbRoguelike.Audio.SfxId.DogBark : base.WarnSfxId;

        [Header("돌진 부품 ① [PLAYTEST]")]
        [SerializeField] private Color wallStunTint = new Color(0.45f, 0.55f, 1f, 0.55f);

        [Header("짖고 달려들기 옵션 (개, 결정 ㊸A) [PLAYTEST] — 기본 꺼짐")]
        [Tooltip("예고 동안 머리 위에 느낌표를 띄운다.")]
        [SerializeField] private bool barkIcon = false;
        [SerializeField] private Color barkIconColor = new Color(1f, 0.85f, 0.2f, 1f);
        [Tooltip("느낌표 높이(몸 중심 기준).")]
        [SerializeField] private float barkIconHeight = 0.8f;

        [SyncVar] private bool isWallStunned;

        private readonly HashSet<PlayerHealth> _hitThisCharge = new HashSet<PlayerHealth>();
        private int _chargeIndex;
        private bool _charging;
        private Vector2 _chargeStart;
        private float _chargeTimeLeft; // 다른 몬스터 등에 막혀 거리를 못 채울 때의 안전 타임아웃
        private float _pauseRemaining;
        private bool _retreating;
        private bool _retreatDone;
        private float _retreatRemaining;
        private GameObject _barkIcon;
        private SpriteRenderer _stunOverlay;
        private SpriteRenderer _body;

        protected override GameObject CreateTelegraphVisual()
        {
            return TelegraphUtil.CreateLine(transform, "ChargeTelegraph", telegraphColor, GetComponent<SpriteRenderer>());
        }

        protected override void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            TelegraphUtil.PlaceLine(telegraph, direction, Cfg.chargeDistance, Cfg.chargeWidth);
        }

        protected override State DecideState(Transform target)
        {
            if (target == null)
            {
                return State.Idle;
            }

            return Vector2.Distance(transform.position, target.position) <= Cfg.chargeTriggerRange ? State.Attack : State.Chase;
        }

        protected override void OnAttackStarted(Transform target)
        {
            _chargeIndex = 0;
            _retreatDone = false;
        }

        protected override void OnWindupFinished(Transform target)
        {
            // 예고가 끝나면 대상 위치와 상관없이 고정 방향으로 돌진한다(후딜은 돌진 뒤 정지가 대신한다).
            RecoveryRemaining = 0f;
            _charging = true;
            _chargeStart = Rigidbody.position;
            _chargeTimeLeft = Cfg.chargeDistance / Mathf.Max(0.1f, Cfg.chargeSpeed) * 2f + 0.5f;
            _hitThisCharge.Clear();
            InAttackSequence = true;
        }

        protected override void TickAttackSequence()
        {
            if (_charging)
            {
                Rigidbody.linearVelocity = AttackDirection * (Cfg.chargeSpeed * SlowFactor);
                HitPlayersInPath();
                _chargeTimeLeft -= Time.deltaTime;
                if (Vector2.Distance(_chargeStart, Rigidbody.position) >= Cfg.chargeDistance || _chargeTimeLeft <= 0f)
                {
                    EndChargeSegment(false);
                }

                return;
            }

            if (_retreating)
            {
                // 후퇴(개 옵션): 대미지 판정 없음. 벽에 막히면 OnCollision에서 그 자리에서 끝낸다.
                Rigidbody.linearVelocity = -AttackDirection * (Cfg.retreatDistance / Mathf.Max(0.01f, Cfg.retreatDuration) * SlowFactor);
                _retreatRemaining -= Time.deltaTime;
                if (_retreatRemaining <= 0f)
                {
                    EndRetreat();
                }

                return;
            }

            Rigidbody.linearVelocity = Vector2.zero;
            _pauseRemaining -= Time.deltaTime;
            if (_pauseRemaining <= 0f)
            {
                isWallStunned = false;
                if (Cfg.retreatAfterCharge && !_retreatDone && Cfg.retreatDistance > 0f && Cfg.retreatDuration > 0f)
                {
                    _retreating = true;
                    _retreatRemaining = Cfg.retreatDuration;
                    return;
                }

                InAttackSequence = false; // 다음 공격 쿨다운은 여기서부터 흐른다
            }
        }

        private void EndRetreat()
        {
            _retreating = false;
            _retreatDone = true;
            Rigidbody.linearVelocity = Vector2.zero;
            InAttackSequence = false; // 다음 공격 쿨다운은 여기서부터 흐른다
        }

        protected override void OnAttackSequenceCancelled()
        {
            _charging = false;
            _retreating = false;
            isWallStunned = false;
        }

        private void HitPlayersInPath()
        {
            float hitRadius = Cfg.chargeWidth * 0.5f + 0.5f; // 돌진 폭 절반 + 플레이어 반 칸(보스 돌진과 같은 방식)
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                if (_hitThisCharge.Contains(health))
                {
                    continue;
                }

                if (Vector2.Distance(Rigidbody.position, health.transform.position) <= hitRadius)
                {
                    _hitThisCharge.Add(health); // 막혔어도(가드 등) 이번 돌진에서는 다시 판정하지 않는다
                    DealDamage(health, attackDamage);
                }
            }
        }

        private void EndChargeSegment(bool hitWall)
        {
            _charging = false;
            Rigidbody.linearVelocity = Vector2.zero;

            if (_chargeIndex < Cfg.chargeCount - 1)
            {
                // 중간 돌진: 경직 없이 바로 짧은 재예고(재조준이면 가장 가까운 플레이어 쪽, 없으면 같은 방향).
                _chargeIndex++;
                Transform target = Cfg.reaimBetweenCharges ? FindNearestPlayerWithin(Stats.detectRange + Cfg.chargeDistance) : null;
                Vector2 dir = target != null ? ((Vector2)target.position - Rigidbody.position).normalized : AttackDirection;
                StartWindup(target, dir, Cfg.reaimWindupDuration);
                return;
            }

            if (hitWall)
            {
                isWallStunned = true;
                _pauseRemaining = Cfg.wallStunDuration;
                Debug.Log($"[ChargeMonster] {name} hit a wall — stunned for {Cfg.wallStunDuration}s");
            }
            else
            {
                _pauseRemaining = Cfg.noWallPause;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            TryWallHit(collision);
            TryRetreatBlocked(collision);
        }

        // 벽에 붙은 채 재조준 방향이 다시 벽 쪽이면 Enter가 또 오지 않는다 — 돌진 방향으로 밀고 있는 접촉도 벽 충돌로 본다(보스 C와 같은 처리).
        private void OnCollisionStay2D(Collision2D collision)
        {
            if (collision.contactCount > 0 && Vector2.Dot(collision.GetContact(0).normal, AttackDirection) < -0.5f)
            {
                TryWallHit(collision);
            }

            TryRetreatBlocked(collision);
        }

        // 후퇴 중 후퇴 방향(돌진 반대)을 막는 벽에 닿으면 그 자리에서 후퇴를 끝낸다(플레이어·몬스터 몸에 막히면 후퇴 시간이 다 되면 끝).
        private void TryRetreatBlocked(Collision2D collision)
        {
            if (!isServer || !_retreating || collision.contactCount == 0)
            {
                return;
            }

            if (collision.collider.GetComponent<PlayerController>() != null || collision.collider.GetComponent<MonsterStub>() != null)
            {
                return;
            }

            if (Vector2.Dot(collision.GetContact(0).normal, -AttackDirection) < -0.5f)
            {
                EndRetreat();
            }
        }

        private void TryWallHit(Collision2D collision)
        {
            if (!isServer || !_charging)
            {
                return;
            }

            // 플레이어·다른 몬스터 몸은 벽이 아니다(플레이어는 경로 판정으로 처리, 몬스터에 막히면 타임아웃으로 끝).
            if (collision.collider.GetComponent<PlayerController>() != null || collision.collider.GetComponent<MonsterStub>() != null)
            {
                return;
            }

            EndChargeSegment(true);
        }

        // 벽 경직 표시: 몸 색을 직접 바꾸지 않고(정예 틴트·상태이상 오버레이와 충돌 방지) 몸 스프라이트를 복사한 자식 오버레이를 덮는다.
        protected override void UpdateClientVisuals()
        {
            UpdateBarkIcon();

            bool show = isWallStunned && !MonsterStub.IsDead && !MonsterStub.IsDefeated;
            if (_stunOverlay == null)
            {
                if (!show)
                {
                    return;
                }

                _body = GetComponent<SpriteRenderer>();
                var go = new GameObject("WallStunOverlay");
                go.transform.SetParent(transform, false);
                _stunOverlay = go.AddComponent<SpriteRenderer>();
                _stunOverlay.color = wallStunTint;
                if (_body != null)
                {
                    _stunOverlay.sortingLayerID = _body.sortingLayerID;
                    _stunOverlay.sortingOrder = _body.sortingOrder + 2;
                }
            }

            _stunOverlay.enabled = show;
            if (show && _body != null)
            {
                _stunOverlay.sprite = _body.sprite;
                _stunOverlay.flipX = _body.flipX;
            }
        }

        // 느낌표(개 옵션): 4×10 픽셀 텍스처(막대 6줄 + 빈 2줄 + 점 2줄)를 몸 위에 띄운다. 예고 중(IsWindingUp 동기화 값)에만 — 모든 화면.
        private void UpdateBarkIcon()
        {
            bool show = barkIcon && IsWindingUp && !MonsterStub.IsDead && !MonsterStub.IsDefeated;
            if (_barkIcon == null)
            {
                if (!show)
                {
                    return;
                }

                const int w = 4;
                const int h = 10;
                var tex = new Texture2D(w, h) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        bool column = x == 1 || x == 2;
                        bool filled = column && (y <= 1 || y >= 4); // 아래 2줄 = 점, 그 위 2줄 = 빈칸, 위 6줄 = 막대
                        tex.SetPixel(x, y, filled ? Color.white : Color.clear);
                    }
                }

                tex.Apply();
                var body = GetComponent<SpriteRenderer>();
                var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 16f);
                _barkIcon = TelegraphUtil.CreateObject(transform, "BarkIcon", sprite, barkIconColor, body);
                if (body != null)
                {
                    _barkIcon.GetComponent<SpriteRenderer>().sortingOrder = body.sortingOrder + 3;
                }

                _barkIcon.transform.localPosition = new Vector3(0f, barkIconHeight, 0f);
            }

            _barkIcon.SetActive(show);
        }
    }
}
