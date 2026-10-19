using System.Collections.Generic;
using AbsorbRoguelike.Player;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 패턴 C "연속 돌진"(리팩터 C3 — 예전 BossChargeAttack의 RushAim·RushCharge 단계).
    /// B 판정으로 rushCount번 돌진, 2타부터는 가장 가까운 플레이어 쪽으로 짧게 재조준한다. 중간 돌진은 경직·파편 없이 곧바로 재조준,
    /// 마지막 돌진만 벽이면 큰 경직, 아니면 짧은 경직(둘 다 파편). 쿨은 경직이 끝날 때 채운다.
    /// 돌진 길이·폭·속도·경직 시간은 B와 같은 값을 이 에셋에도 둔다(예전엔 B와 한 필드를 같이 썼음 — 값은 그대로 옮김).
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternRush", menuName = "Absorb/Boss/Pattern C Rush")]
    public class BossPatternRush : BossPatternBase
    {
        [Header("패턴 C 연속 돌진 (GDD: 첫 타 0.8초, 재조준 0.4초, B 판정 ×3, 각 25, 쿨 14초) [PLAYTEST]")]
        [SerializeField] private int rushCount = 3;
        [SerializeField] private float rushFirstTelegraphDuration = 0.8f;
        [SerializeField] private float rushReaimDuration = 0.4f;
        [SerializeField] private int rushDamage = 25;
        [SerializeField] private float rushCooldown = 14f;
        [Tooltip("마지막 돌진이 끝날 때 파편 개수(벽이든 아니든, 중간 돌진은 0). [PLAYTEST]")]
        [SerializeField] private int debrisPerRushWall = 2;
        [Tooltip("가중 무작위 가중치(쿨이 돌아온 후보 중). [PLAYTEST]")]
        [SerializeField] private float weightC = 1f;

        [Header("돌진 판정 (B와 같은 값)")]
        [SerializeField] private float chargeLength = 7f;
        [SerializeField] private float chargeWidth = 2f;
        [SerializeField] private float chargeSpeed = 14f;
        [SerializeField] private float wallStaggerDuration = 2f;
        [SerializeField] private float recoveryDuration = 0.5f;
        [SerializeField] private Color telegraphColor = new Color(1f, 0.2f, 0.2f, 0.35f);

        public override string PatternId => "C";
        public override string DisplayName => "연속 돌진";
        public override float Cooldown => rushCooldown;
        public override float GetWeight(BossBrain brain) => weightC;

        public override BossPatternRuntime CreateRuntime(BossBrain brain) => new Runtime(this, brain);

        private sealed class Runtime : BossPatternRuntime
        {
            private const byte StepFirstAim = 0;
            private const byte StepCharge = 1;
            private const byte StepReaim = 2; // 돌진 직후 짧은 재조준(예고 길이 rushReaimDuration)

            public override string StepSfxId(byte step) => step == StepCharge ? AbsorbRoguelike.Audio.SfxId.BossCharge : null; // 매 돌진 시작(2026-10-14)

            private readonly BossPatternRush _data;
            private readonly GameObject _line;
            private readonly HashSet<PlayerHealth> _hitThisCharge = new HashSet<PlayerHealth>();
            private byte _step;
            private float _timer;
            private int _rushIndex;
            private Vector2 _chargeStart;

            public Runtime(BossPatternRush data, BossBrain brain) : base(brain)
            {
                _data = data;
                _line = BossPatternCharge.CreateLineTelegraph(brain, data.telegraphColor);
            }

            public override void Begin(Vector2 direction)
            {
                Brain.SetDirection(direction, true);
                _rushIndex = 0;
                SetStep(StepFirstAim);
                _timer = _data.rushFirstTelegraphDuration;
            }

            private void SetStep(byte step)
            {
                _step = step;
                Brain.SetStep(step);
            }

            public override PatternTick ServerTick(float dt)
            {
                if (_step != StepCharge)
                {
                    Brain.Body.linearVelocity = Vector2.zero;
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        SetStep(StepCharge);
                        _chargeStart = Brain.Body.position;
                        _hitThisCharge.Clear(); // 돌진마다 비운다 — 3번 모두 맞을 수 있다
                    }

                    return PatternTick.Continue;
                }

                if (BossPatternCharge.TickCharge(Brain, _chargeStart, _data.chargeSpeed, _data.chargeLength, _data.chargeWidth, _data.rushDamage, _hitThisCharge))
                {
                    return EndSegment(false);
                }

                return PatternTick.Continue;
            }

            // 돌진 한 번이 끝났을 때(거리 도달 또는 벽). 중간 돌진은 경직·파편 없이 곧바로 재조준, 마지막만 경직으로 끝낸다.
            private PatternTick EndSegment(bool hitWall)
            {
                Brain.Body.linearVelocity = Vector2.zero;
                if (_rushIndex < _data.rushCount - 1)
                {
                    _rushIndex++;
                    Transform target = Brain.FindNearestPlayer();
                    if (target != null)
                    {
                        Brain.SetDirection(((Vector2)target.position - Brain.Body.position).normalized, true); // 없으면 이전 방향 유지
                    }

                    SetStep(StepReaim);
                    _timer = _data.rushReaimDuration;
                    return PatternTick.Continue;
                }

                Brain.SpawnDebrisLine(Brain.Body.position, -Brain.Direction, _data.debrisPerRushWall); // 벽 없이 끝나도 파편
                if (hitWall)
                {
                    Debug.Log($"[BossCharge] {Brain.name} rush final hit a wall — staggered for {_data.wallStaggerDuration}s");
                    Brain.NotifyWallHit(); // 벽 충돌 효과음(모든 화면)
                    return PatternTick.Stagger(_data.wallStaggerDuration);
                }

                return PatternTick.Stagger(_data.recoveryDuration);
            }

            public override PatternTick OnServerCollisionEnter(Collision2D collision)
            {
                return _step == StepCharge ? EndSegment(true) : PatternTick.Continue;
            }

            // 중간 돌진이 벽에 붙은 채 끝나면 재조준 방향이 다시 벽 쪽일 때 Enter가 또 오지 않아 벽을 계속 밀 수 있다 —
            // 돌진 방향으로 밀고 있는 접촉(법선이 돌진 반대쪽)이면 벽 충돌로 처리한다.
            public override PatternTick OnServerCollisionStay(Collision2D collision)
            {
                if (_step == StepCharge && Vector2.Dot(collision.GetContact(0).normal, Brain.Direction) < -0.5f)
                {
                    return EndSegment(true);
                }

                return PatternTick.Continue;
            }

            public override void DrawTelegraph(bool active, byte step, float stepStartLocal)
            {
                bool show = active && step != StepCharge; // 첫 예고·재조준 모두 같은 직선 표시
                _line.SetActive(show);
                if (show)
                {
                    float duration = step == StepReaim ? _data.rushReaimDuration : _data.rushFirstTelegraphDuration;
                    BossPatternCharge.DrawLineTelegraph(_line, Brain, stepStartLocal, duration, _data.chargeLength, _data.chargeWidth, _data.telegraphColor);
                }
            }
        }
    }
}
