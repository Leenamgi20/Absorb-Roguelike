using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 패턴 E "포효 대지분쇄"(폭주 전용, 리팩터 C3 — 예전 BossChargeAttack의 RoarTelegraph 단계).
    /// 반경 원형 긴 경고(깜빡임) → 반경 안 대미지 + 강한 카메라 흔들림 + 원형 파편 → 무방비(BossBrain Stagger 재사용, 파랑 틴트·딜 타임).
    /// 쿨은 무방비가 끝날 때 채우고, GDD 쿨 값이 이미 폭주 중 기준이라 세트 항목에서 폭주 배율을 끈다.
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternRoar", menuName = "Absorb/Boss/Pattern E Roar")]
    public class BossPatternRoar : BossPatternBase
    {
        [Header("패턴 E 포효 대지분쇄 — 폭주 전용 (GDD: 1.6초, 반경 3.5 원형, 45, 폭주 중 쿨 16초) [PLAYTEST]")]
        [SerializeField] private float roarTelegraphDuration = 1.6f;
        [SerializeField] private float roarRadius = 3.5f;
        [SerializeField] private int roarDamage = 45;
        [SerializeField] private float roarCooldown = 16f;
        [Tooltip("충격파 뒤 무방비(Stagger 재사용, 파랑 틴트·딜 타임).")]
        [SerializeField] private float roarStaggerDuration = 2.5f;
        [SerializeField] private float roarShakeDuration = 0.6f;
        [SerializeField] private float roarShakeAmplitude = 0.5f;
        [SerializeField] private Color roarTelegraphColor = new Color(0.8f, 0f, 0f, 0.5f);
        [Tooltip("긴 경고 깜빡임 속도(초당 횟수).")]
        [SerializeField] private float roarBlinkRate = 4f;
        [Tooltip("무방비 시작 때 보스 주변 원형으로 흩뿌리는 개수. [PLAYTEST]")]
        [SerializeField] private int debrisPerRoar = 2;
        [Tooltip("가중 무작위 가중치(쿨이 돌아온 후보 중). [PLAYTEST]")]
        [SerializeField] private float weightE = 1.5f;

        public override string PatternId => "E";
        public override string DisplayName => "포효 대지분쇄";
        public override float Cooldown => roarCooldown;
        public override float GetWeight(BossBrain brain) => weightE;

        public override BossPatternRuntime CreateRuntime(BossBrain brain) => new Runtime(this, brain);

        private sealed class Runtime : BossPatternRuntime
        {
            private readonly BossPatternRoar _data;
            private readonly GameObject _circle; // 반지름 1 원(roarRadius로 스케일)

            public override string StepSfxId(byte step) => step == 0 ? AbsorbRoguelike.Audio.SfxId.BossRoar : null; // 포효 시작(2026-10-14, 충격 순간은 BossBrain 흔들림 RPC의 강타음)
            private readonly SpriteRenderer _circleRenderer;
            private float _timer;

            public Runtime(BossPatternRoar data, BossBrain brain) : base(brain)
            {
                _data = data;
                _circle = TelegraphUtil.CreateCircle(brain.transform, "CircleTelegraph", data.roarTelegraphColor, brain.BodySprite);
                _circleRenderer = _circle.GetComponent<SpriteRenderer>();
            }

            public override void Begin(Vector2 direction)
            {
                Brain.SetStep(0);
                _timer = _data.roarTelegraphDuration;
            }

            public override PatternTick ServerTick(float dt)
            {
                Brain.Body.linearVelocity = Vector2.zero;
                _timer -= dt;
                if (_timer > 0f)
                {
                    return PatternTick.Continue;
                }

                Brain.HitPlayersInRadius(_data.roarRadius, _data.roarDamage);
                Brain.CameraShake(_data.roarShakeDuration, _data.roarShakeAmplitude);
                Brain.SpawnDebrisRing(Brain.Body.position, _data.debrisPerRoar);
                return PatternTick.Stagger(_data.roarStaggerDuration); // 끝나면 E 쿨만 채운다
            }

            public override void DrawTelegraph(bool active, byte step, float stepStartLocal)
            {
                _circle.SetActive(active);
                if (!active)
                {
                    return;
                }

                Vector3 parentScale = Brain.transform.lossyScale;
                _circle.transform.localPosition = Vector3.zero;
                _circle.transform.localScale = new Vector3(_data.roarRadius / parentScale.x, _data.roarRadius / parentScale.y, 1f);

                // 긴 경고: 더 진한 색(roarTelegraphColor) + 진하기 규칙, 마지막 번쩍 구간 전까지는 깜빡임(알파 0.6~1배)을 얹는다.
                float progress = TelegraphUtil.Progress(stepStartLocal, _data.roarTelegraphDuration);
                Color c = TelegraphUtil.ProgressColor(progress, _data.roarTelegraphColor, _data.roarTelegraphDuration);
                if (progress < 1f - TelegraphUtil.FinalWindow / _data.roarTelegraphDuration)
                {
                    c.a *= 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * _data.roarBlinkRate * Mathf.PI * 2f));
                }

                _circleRenderer.color = c;
            }
        }
    }
}
