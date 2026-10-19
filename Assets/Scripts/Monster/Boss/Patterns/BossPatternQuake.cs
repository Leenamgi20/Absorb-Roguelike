using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 패턴 D "지진 강타"(리팩터 C3 — 예전 BossChargeAttack의 QuakeTelegraph 단계).
    /// 보스 중심 원형 예고 → 반경 안 플레이어 대미지 + 카메라 흔들림(모든 화면) + 원형 파편 → 짧은 회복. 쿨은 충격파 순간에 채운다.
    /// 가중치는 반경 안에 (쓰러지지 않은) 플레이어가 있으면 weightDNear, 없으면 weightDFar.
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternQuake", menuName = "Absorb/Boss/Pattern D Quake")]
    public class BossPatternQuake : BossPatternBase
    {
        [Header("패턴 D 지진 강타 (GDD: 1.2초, 반경 5 원형, 28, 쿨 12초) [PLAYTEST]")]
        [SerializeField] private float quakeTelegraphDuration = 1.2f;
        [SerializeField] private float quakeRadius = 5f;
        [SerializeField] private int quakeDamage = 28;
        [SerializeField] private float quakeCooldown = 12f;
        [SerializeField] private float quakeRecoveryDuration = 0.4f;
        [SerializeField] private float quakeShakeDuration = 0.35f;
        [SerializeField] private float quakeShakeAmplitude = 0.25f;
        [SerializeField] private Color telegraphColor = new Color(1f, 0.2f, 0.2f, 0.35f);
        [Tooltip("충격파 뒤 보스 주변 원형으로 흩뿌리는 개수. [PLAYTEST]")]
        [SerializeField] private int debrisPerQuake = 2;
        [Tooltip("quakeRadius 안에 플레이어가 있을 때 가중치")]
        [SerializeField] private float weightDNear = 2f;
        [Tooltip("quakeRadius 안에 플레이어가 없을 때 가중치")]
        [SerializeField] private float weightDFar = 0.3f;

        public override string PatternId => "D";
        public override string DisplayName => "지진 강타";
        public override float Cooldown => quakeCooldown;
        public override float GetWeight(BossBrain brain) => brain.AnyPlayerWithin(quakeRadius) ? weightDNear : weightDFar;

        public override BossPatternRuntime CreateRuntime(BossBrain brain) => new Runtime(this, brain);

        private sealed class Runtime : BossPatternRuntime
        {
            private readonly BossPatternQuake _data;
            private readonly GameObject _circle; // 반지름 1 원(quakeRadius로 스케일)
            private readonly SpriteRenderer _circleRenderer;
            private float _timer;

            public Runtime(BossPatternQuake data, BossBrain brain) : base(brain)
            {
                _data = data;
                _circle = TelegraphUtil.CreateCircle(brain.transform, "CircleTelegraph", data.telegraphColor, brain.BodySprite);
                _circleRenderer = _circle.GetComponent<SpriteRenderer>();
            }

            public override void Begin(Vector2 direction)
            {
                Brain.SetStep(0);
                _timer = _data.quakeTelegraphDuration;
            }

            public override PatternTick ServerTick(float dt)
            {
                Brain.Body.linearVelocity = Vector2.zero;
                _timer -= dt;
                if (_timer > 0f)
                {
                    return PatternTick.Continue;
                }

                Brain.HitPlayersInRadius(_data.quakeRadius, _data.quakeDamage);
                Brain.CameraShake(_data.quakeShakeDuration, _data.quakeShakeAmplitude);
                Brain.SpawnDebrisRing(Brain.Body.position, _data.debrisPerQuake);
                return PatternTick.Recovery(_data.quakeRecoveryDuration); // 쿨은 BossBrain이 지금 채운다(A 쿨은 건드리지 않음)
            }

            public override void DrawTelegraph(bool active, byte step, float stepStartLocal)
            {
                _circle.SetActive(active);
                if (!active)
                {
                    return;
                }

                // 부모(보스) 스케일이 있어도 월드 기준 반경이 맞도록 보정
                Vector3 parentScale = Brain.transform.lossyScale;
                _circle.transform.localPosition = Vector3.zero;
                _circle.transform.localScale = new Vector3(_data.quakeRadius / parentScale.x, _data.quakeRadius / parentScale.y, 1f);
                TelegraphUtil.SetProgress(_circleRenderer, TelegraphUtil.Progress(stepStartLocal, _data.quakeTelegraphDuration), _data.telegraphColor, _data.quakeTelegraphDuration);
            }
        }
    }
}
