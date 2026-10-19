using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 패턴 하나의 수치(ScriptableObject, 리팩터 C3 2026-10-12). 에셋은 Assets/Data/Boss/.
    /// 에셋에는 수치와 상태 없는 질문(쿨·가중치·근접 우선 여부)만 두고, 진행 중 상태(타이머·단계·맞은 플레이어 등)는
    /// <see cref="CreateRuntime"/>가 보스마다 만드는 <see cref="BossPatternRuntime"/>에만 둔다 — 에셋 하나를 여러 보스가 같이 써도 섞이지 않게.
    /// 어떤 보스가 어느 페이즈에 이 패턴을 쓰는지는 <see cref="BossPatternSet"/>이 정한다.
    /// </summary>
    public abstract class BossPatternBase : ScriptableObject
    {
        /// <summary>디버그 메뉴·로그용 식별자(챕터 1 보스 GDD 기호 "A"~"E"). 로그 "[BossCharge] … pattern A"가 예전과 같게 남는다.</summary>
        public abstract string PatternId { get; }

        /// <summary>사람이 읽는 이름(디버그용).</summary>
        public abstract string DisplayName { get; }

        /// <summary>패턴이 끝날 때 채우는 쿨(초). 폭주 배율은 세트 항목의 applyEnrageMultiplier에 따라 BossBrain이 곱한다.</summary>
        public abstract float Cooldown { get; }

        /// <summary>true면 가중 무작위 후보가 아니라, 쿨이 돌아왔고 가장 가까운 플레이어가 <see cref="MeleeTriggerRange"/> 안이면 먼저 시작한다(A).</summary>
        public virtual bool IsMeleePriority => false;

        public virtual float MeleeTriggerRange => 0f;

        /// <summary>true면 시작해도 '직전 패턴'으로 기록하지 않는다(연속 방지 대상 아님, A).</summary>
        public virtual bool ExcludeFromRepeatGuard => false;

        /// <summary>가중 무작위 가중치. 0 이하면 후보에서 빠진다. D처럼 상황에 따라 바뀌면 재정의한다(서버).</summary>
        public virtual float GetWeight(BossBrain brain) => 0f;

        /// <summary>보스 하나 전용 런타임 객체를 만든다(BossBrain.Awake, 서버·클라이언트 모두 — 예고 표시도 런타임 객체가 한다).</summary>
        public abstract BossPatternRuntime CreateRuntime(BossBrain brain);
    }

    /// <summary>
    /// 패턴의 보스별 런타임 객체(일반 C# 객체). 서버에서는 <see cref="Begin"/> → 매 프레임 <see cref="ServerTick"/>(정지 중엔 불리지 않음),
    /// 모든 화면에서는 매 프레임 <see cref="DrawTelegraph"/>로 동기화된 단계·방향을 보고 예고를 그린다.
    /// 상태 전환(회복·경직)은 결과(<see cref="PatternTick"/>)로 돌려주고 BossBrain이 처리한다 — 쿨을 채우는 시점도 그 결과로 정해진다
    /// (Recovery = 바로 채움(A·D), Stagger = 경직이 끝날 때 채움(B·C·E)).
    /// </summary>
    public abstract class BossPatternRuntime
    {
        protected readonly BossBrain Brain;

        protected BossPatternRuntime(BossBrain brain)
        {
            Brain = brain;
        }

        /// <summary>서버: 패턴 시작. direction은 가장 가까운 플레이어 쪽(없으면 BossBrain 대체 방향). 단계 0으로 시작한다.</summary>
        public abstract void Begin(Vector2 direction);

        /// <summary>서버: 한 프레임 진행.</summary>
        public abstract PatternTick ServerTick(float dt);

        /// <summary>서버: 이 패턴 진행 중 플레이어가 아닌 것과 부딪힘(Enter). 기본은 무시.</summary>
        public virtual PatternTick OnServerCollisionEnter(Collision2D collision) => PatternTick.Continue;

        /// <summary>서버: 이 패턴 진행 중 플레이어가 아닌 것과 계속 닿아 있음(Stay, 접점 1개 이상). 기본은 무시.</summary>
        public virtual PatternTick OnServerCollisionStay(Collision2D collision) => PatternTick.Continue;

        /// <summary>
        /// 모든 화면: 예고 표시. active = 지금 이 패턴이 진행 중인지(동기화된 패턴 인덱스로 판단), step = 동기화된 패턴 안 단계,
        /// stepStartLocal = 이 화면이 그 단계를 처음 본 로컬 Time.time(예고 진하기 결정 ㊶ — 원격 클라도 SyncVar 도착 시점부터 같은 길이로 진해짐).
        /// 방향은 Brain.Direction. active가 false면 자기 예고 오브젝트를 끈다.
        /// </summary>
        public abstract void DrawTelegraph(bool active, byte step, float stepStartLocal);

        /// <summary>모든 화면: 동기화된 단계가 step으로 바뀐 순간 낼 효과음 id(없으면 null). BossBrain.TrackStepStart가 부른다(2026-10-14).</summary>
        public virtual string StepSfxId(byte step) => null;
    }

    /// <summary>패턴 한 프레임의 결과. Continue = 계속, Recovery = 짧은 회복 뒤 Idle(쿨 즉시), Stagger = 경직(파랑 틴트·딜 타임) 뒤 Idle(쿨은 끝날 때).</summary>
    public readonly struct PatternTick
    {
        public enum Kind : byte
        {
            Continue,
            Recovery,
            Stagger
        }

        public readonly Kind Result;
        public readonly float Duration;

        private PatternTick(Kind result, float duration)
        {
            Result = result;
            Duration = duration;
        }

        public static PatternTick Continue => new PatternTick(Kind.Continue, 0f);

        public static PatternTick Recovery(float duration) => new PatternTick(Kind.Recovery, duration);

        public static PatternTick Stagger(float duration) => new PatternTick(Kind.Stagger, duration);
    }
}
