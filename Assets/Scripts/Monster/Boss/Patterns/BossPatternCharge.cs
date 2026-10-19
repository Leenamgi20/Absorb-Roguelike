using System.Collections.Generic;
using AbsorbRoguelike.Player;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 패턴 B "강화 박치기 돌진"(리팩터 C3 — 예전 BossChargeAttack의 Telegraph·Charge 단계).
    /// 직선 예고 → 직선 돌진(경로 판정, 돌진 1회당 한 번) → 벽에 부딪히면 큰 경직(딜 타임) + 파편, 거리를 다 가면 짧은 경직 + 파편.
    /// 쿨은 경직이 끝날 때 채운다. 돌진 이동·판정과 직선 예고 그리기는 패턴 C(BossPatternRush)도 같이 쓴다(아래 정적 함수).
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternCharge", menuName = "Absorb/Boss/Pattern B Charge")]
    public class BossPatternCharge : BossPatternBase
    {
        [Header("패턴 B 강화 박치기 돌진 [PLAYTEST]")]
        [SerializeField] private float telegraphDuration = 0.8f;
        [SerializeField] private float chargeLength = 7f;
        [SerializeField] private float chargeWidth = 2f;
        [SerializeField] private float chargeSpeed = 14f;
        [SerializeField] private int damage = 30;
        [SerializeField] private float wallStaggerDuration = 2f;
        [Tooltip("벽에 안 부딪히고 거리를 다 갔을 때 짧은 경직.")]
        [SerializeField] private float recoveryDuration = 0.5f;
        [SerializeField] private Color telegraphColor = new Color(1f, 0.2f, 0.2f, 0.35f);
        [Tooltip("돌진이 끝날 때 파편 개수(벽에 부딪혀 경직이든 거리를 다 가서 멈추든). [PLAYTEST]")]
        [SerializeField] private int debrisPerImpact = 2;
        [SerializeField] private float cooldown = 8f;
        [Tooltip("가중 무작위 가중치(쿨이 돌아온 후보 중). [PLAYTEST]")]
        [SerializeField] private float weightB = 1f;

        public override string PatternId => "B";
        public override string DisplayName => "강화 박치기 돌진";
        public override float Cooldown => cooldown;
        public override float GetWeight(BossBrain brain) => weightB;

        public override BossPatternRuntime CreateRuntime(BossBrain brain) => new Runtime(this, brain);

        // ── B·C 공용 ───────────────────────────────────────────

        /// <summary>직선 돌진 예고 오브젝트(중심 피벗 1×1, 길이·폭으로 스케일).</summary>
        internal static GameObject CreateLineTelegraph(BossBrain brain, Color color)
        {
            return TelegraphUtil.CreateRect(brain.transform, "ChargeTelegraph", new Vector2(0.5f, 0.5f), color, brain.BodySprite);
        }

        /// <summary>서버: 돌진 한 프레임 — Direction으로 (속도 × 둔화) 이동, 경로 판정. 시작점에서 length 이상 갔으면 true.</summary>
        internal static bool TickCharge(BossBrain brain, Vector2 start, float speed, float length, float width, int hitDamage, HashSet<PlayerHealth> alreadyHit)
        {
            brain.Body.linearVelocity = brain.Direction * (speed * brain.SpeedMultiplier);
            brain.HitPlayersInPath(width, hitDamage, alreadyHit);
            return Vector2.Distance(start, brain.Body.position) >= length;
        }

        /// <summary>모든 화면: 직선 예고를 Direction 쪽으로 길이·폭에 맞춰 놓고 진하기(duration 기준)를 정한다.</summary>
        internal static void DrawLineTelegraph(GameObject line, BossBrain brain, float stepStartLocal, float duration, float length, float width, Color color)
        {
            Vector2 dir = brain.Direction;
            TelegraphUtil.SetProgress(line, TelegraphUtil.Progress(stepStartLocal, duration), color, duration);
            line.transform.position = brain.transform.position + (Vector3)(dir * (length * 0.5f));
            line.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            line.transform.localScale = new Vector3(length, width, 1f);
        }

        private sealed class Runtime : BossPatternRuntime
        {
            private const byte StepTelegraph = 0;
            private const byte StepCharge = 1;

            public override string StepSfxId(byte step) => step == StepCharge ? AbsorbRoguelike.Audio.SfxId.BossCharge : null; // 돌진 시작(2026-10-14)

            private readonly BossPatternCharge _data;
            private readonly GameObject _line;
            private readonly HashSet<PlayerHealth> _hitThisCharge = new HashSet<PlayerHealth>();
            private byte _step;
            private float _timer;
            private Vector2 _chargeStart;

            public Runtime(BossPatternCharge data, BossBrain brain) : base(brain)
            {
                _data = data;
                _line = CreateLineTelegraph(brain, data.telegraphColor);
            }

            public override void Begin(Vector2 direction)
            {
                Brain.SetDirection(direction, true);
                _step = StepTelegraph;
                Brain.SetStep(_step);
                _timer = _data.telegraphDuration;
            }

            public override PatternTick ServerTick(float dt)
            {
                if (_step == StepTelegraph)
                {
                    Brain.Body.linearVelocity = Vector2.zero;
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _step = StepCharge;
                        Brain.SetStep(_step);
                        _chargeStart = Brain.Body.position;
                        _hitThisCharge.Clear();
                    }

                    return PatternTick.Continue;
                }

                if (TickCharge(Brain, _chargeStart, _data.chargeSpeed, _data.chargeLength, _data.chargeWidth, _data.damage, _hitThisCharge))
                {
                    Brain.SpawnDebrisLine(Brain.Body.position, -Brain.Direction, _data.debrisPerImpact); // 벽 없이 끝나도 파편
                    return PatternTick.Stagger(_data.recoveryDuration);
                }

                return PatternTick.Continue;
            }

            public override PatternTick OnServerCollisionEnter(Collision2D collision)
            {
                if (_step != StepCharge)
                {
                    return PatternTick.Continue;
                }

                Debug.Log($"[BossCharge] {Brain.name} hit a wall — staggered for {_data.wallStaggerDuration}s");
                Brain.NotifyWallHit(); // 벽 충돌 효과음(모든 화면)
                Brain.SpawnDebrisLine(Brain.Body.position, -Brain.Direction, _data.debrisPerImpact);
                return PatternTick.Stagger(_data.wallStaggerDuration);
            }

            public override void DrawTelegraph(bool active, byte step, float stepStartLocal)
            {
                bool show = active && step == StepTelegraph;
                _line.SetActive(show);
                if (show)
                {
                    DrawLineTelegraph(_line, Brain, stepStartLocal, _data.telegraphDuration, _data.chargeLength, _data.chargeWidth, _data.telegraphColor);
                }
            }
        }
    }
}
