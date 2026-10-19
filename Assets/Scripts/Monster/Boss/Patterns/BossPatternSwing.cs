using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 패턴 A "뿔 휘두르기"(근접 대응, 리팩터 C3 — 예전 BossChargeAttack의 SwingTelegraph·SwingFlurry 단계).
    /// 가장 가까운 플레이어가 swingTriggerRange 안이고 쿨이 돌아왔으면 다른 패턴보다 먼저 시작한다(연속 방지 대상 아님).
    /// 부채꼴 예고 → 타격 → 파편(원형) → 짧은 회복. 상황 특성 '연타 휘두르기'(FloorProgression.CurrentBossSituation == FlurrySwing)면
    /// 첫 타 뒤 간격마다 남은 연타를 이어 가고, 특성은 타격 시점마다 다시 읽어 도중에 바뀌면 끊는다.
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternSwing", menuName = "Absorb/Boss/Pattern A Swing")]
    public class BossPatternSwing : BossPatternBase
    {
        [Header("패턴 A 뿔 휘두르기 — 근접 대응 (GDD 기준 0.5초/18/2.5초, 범위는 보스 몸집에 맞춰 조금 넓힘)")]
        [Tooltip("가장 가까운 플레이어가 이 거리 안이고 A 쿨이 돌아왔으면 다른 패턴보다 먼저 휘두른다. 판정 반경보다 넓게 잡아 다가오는 순간 반응.")]
        [SerializeField] private float swingTriggerRange = 4f;
        [SerializeField] private float swingRadius = 3f;
        [SerializeField] private float swingHalfAngle = 60f;
        [SerializeField] private float swingTelegraphDuration = 0.5f;
        [SerializeField] private int swingDamage = 18;
        [SerializeField] private float swingCooldown = 2.5f;
        [SerializeField] private float swingRecoveryDuration = 0.4f;
        [SerializeField] private Color telegraphColor = new Color(1f, 0.2f, 0.2f, 0.35f);
        [Tooltip("휘두르기(연타 포함)가 끝날 때 보스 주변에 떨어뜨리는 개수. [PLAYTEST]")]
        [SerializeField] private int debrisPerSwing = 1;

        [Header("상황 특성 — 연타 휘두르기 (GDD ⑯A, FloorProgression.CurrentBossSituation == FlurrySwing)")]
        [Tooltip("휘두르기 1회의 총 타격 수(첫 타 포함). [PLAYTEST]")]
        [SerializeField] private int flurryHitCount = 3;
        [Tooltip("연타 사이 간격(초). 돼지 반격 쿨(연타 층 0.4초)보다 길어야 매 타격마다 반격한다. [PLAYTEST]")]
        [SerializeField] private float flurryInterval = 0.45f;
        [Tooltip("연타 한 번의 대미지 = RoundToInt(swingDamage × 이 값). [PLAYTEST]")]
        [SerializeField] private float flurryDamageScale = 0.6f;

        public override string PatternId => "A";
        public override string DisplayName => "뿔 휘두르기";
        public override float Cooldown => swingCooldown;
        public override bool IsMeleePriority => true;
        public override float MeleeTriggerRange => swingTriggerRange;
        public override bool ExcludeFromRepeatGuard => true;

        public override BossPatternRuntime CreateRuntime(BossBrain brain) => new Runtime(this, brain);

        private static bool IsFlurrySituation
        {
            get
            {
                var dungeon = AbsorbRoguelike.Dungeon.FloorProgression.Instance;
                return dungeon != null && dungeon.CurrentBossSituation == AbsorbRoguelike.Dungeon.BossSituation.FlurrySwing;
            }
        }

        private int FlurryDamage => Mathf.RoundToInt(swingDamage * flurryDamageScale);

        private sealed class Runtime : BossPatternRuntime
        {
            private const byte StepTelegraph = 0;
            private const byte StepFlurry = 1; // 첫 타 뒤 남은 연타 대기(방향 고정)

            private readonly BossPatternSwing _data;
            private readonly GameObject _cone; // 반지름 1 부채꼴(swingRadius로 스케일)
            private byte _step;
            private float _timer;
            private int _flurryHitsLeft;

            public Runtime(BossPatternSwing data, BossBrain brain) : base(brain)
            {
                _data = data;
                _cone = TelegraphUtil.CreateCone(brain.transform, "SwingTelegraph", data.swingHalfAngle, data.telegraphColor, brain.BodySprite);
            }

            public override void Begin(Vector2 direction)
            {
                Brain.SetDirection(direction, false);
                _step = StepTelegraph;
                Brain.SetStep(_step);
                _timer = _data.swingTelegraphDuration;
            }

            public override PatternTick ServerTick(float dt)
            {
                Brain.Body.linearVelocity = Vector2.zero;
                _timer -= dt;
                if (_timer > 0f)
                {
                    return PatternTick.Continue;
                }

                if (_step == StepTelegraph)
                {
                    if (IsFlurrySituation && _data.flurryHitCount > 1)
                    {
                        // 연타: 첫 타는 즉시, 남은 타격은 StepFlurry에서 간격마다(방향은 Direction 고정).
                        Hit(_data.FlurryDamage);
                        _flurryHitsLeft = _data.flurryHitCount - 1;
                        _step = StepFlurry;
                        Brain.SetStep(_step);
                        _timer = _data.flurryInterval;
                        return PatternTick.Continue;
                    }

                    Hit(_data.swingDamage);
                    return EndSwing();
                }

                // 특성은 타격 시점마다 다시 읽는다 — 디버그 메뉴로 도중에 바꾸면 남은 연타를 끊는다.
                if (!IsFlurrySituation)
                {
                    return EndSwing();
                }

                Hit(_data.FlurryDamage);
                _flurryHitsLeft--;
                if (_flurryHitsLeft <= 0)
                {
                    return EndSwing();
                }

                _timer = _data.flurryInterval;
                return PatternTick.Continue;
            }

            private void Hit(int damage)
            {
                Brain.HitPlayersInCone(Brain.Direction, _data.swingRadius, _data.swingHalfAngle, damage);
            }

            // 파편(연타 중간에 끊겨도 한 번) → 짧은 회복. 쿨은 BossBrain이 Recovery 결과를 받을 때 채운다.
            private PatternTick EndSwing()
            {
                _flurryHitsLeft = 0;
                Brain.SpawnDebrisRing(Brain.Body.position, _data.debrisPerSwing);
                return PatternTick.Recovery(_data.swingRecoveryDuration);
            }

            public override void DrawTelegraph(bool active, byte step, float stepStartLocal)
            {
                _cone.SetActive(active); // 연타 동안 계속 표시
                if (!active)
                {
                    return;
                }

                // 부모(보스) 스케일이 있어도 월드 기준 반경이 맞도록 보정
                Vector3 parentScale = Brain.transform.lossyScale;
                Vector2 dir = Brain.Direction;
                _cone.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                _cone.transform.localScale = new Vector3(_data.swingRadius / parentScale.x, _data.swingRadius / parentScale.y, 1f);
                if (step == StepTelegraph)
                {
                    TelegraphUtil.SetProgress(_cone, TelegraphUtil.Progress(stepStartLocal, _data.swingTelegraphDuration), _data.telegraphColor, _data.swingTelegraphDuration);
                }
                else
                {
                    // 연타: 연타 간격마다 다시 연하게 시작해 다음 타격 직전에 진해진다(로컬 시간 기준 근사).
                    float sinceFlurry = Mathf.Repeat(Time.time - stepStartLocal, _data.flurryInterval);
                    TelegraphUtil.SetProgress(_cone, sinceFlurry / _data.flurryInterval, _data.telegraphColor, _data.flurryInterval);
                }
            }
        }
    }
}
