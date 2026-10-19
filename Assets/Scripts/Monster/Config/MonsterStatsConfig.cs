using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 잡몹 공통 게임플레이 수치(리팩터 C4, 2026-10-12 — 예전 MonsterBehaviorController·MonsterStub 인스펙터 값). 에셋: Assets/Data/Monsters/&lt;동물&gt;.asset
    /// (일반·정예 프리팹이 같은 에셋을 공유 — 정예 배율은 EliteConfig). 이름·타입·기본값·Tooltip은 옮기기 전 필드 그대로.
    /// 고유 수치가 있는 행동은 하위 클래스(ChargeMonsterConfig 등)를, 없는 행동(뱀 = 기본 MonsterBehaviorController)은 이 타입을 그대로 쓴다.
    /// 런타임에 바꾸지 않는다(공유 에셋) — 정예 공격력처럼 바뀌는 값은 컴포넌트가 복사해 들고 있다(MonsterBehaviorController.attackDamage).
    /// 예고 색 등 연출 값은 프리팹(MonsterBehaviorController)에 남는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Basic", fileName = "MonsterStatsConfig")]
    public class MonsterStatsConfig : ScriptableObject
    {
        [Header("체력")]
        [Tooltip("스폰 때 MonsterStub.maxHealth(SyncVar)에 넣는 값. 정예면 그 뒤 EliteConfig.healthMultiplier를 곱한다.")]
        [SerializeField] internal int maxHealth = 30;

        [Header("공통 탐지/이동")]
        [SerializeField] internal float detectRange = 4f;
        [Tooltip("한 번 플레이어를 발견했거나 맞은 뒤의 추적 거리(칸) — 방이 넓어 몬스터를 찾으러 다니던 문제(2026-10-07). [PLAYTEST]")]
        [SerializeField] internal float aggroRange = 16f;
        [Tooltip("처음 발견 거리 배율(프리팹의 detectRange에 곱함, 2026-10-07: 4칸 → 7칸 수준). [PLAYTEST]")]
        [SerializeField] internal float detectRangeScale = 1.75f;
        [SerializeField] internal float attackRange = 1f;
        [SerializeField] internal float moveSpeed = 1.5f;

        [Header("공통 공격")]
        [SerializeField] internal float attackCooldown = 1.5f;
        [Tooltip("기본 공격력. 정예 배율은 컴포넌트의 복사본에 곱한다(이 에셋 값은 그대로).")]
        [SerializeField] internal int attackDamage = 8;

        [Header("공격 예고 (2026-09-23, 수치 전부 [PLAYTEST])")]
        [Tooltip("사거리에 들어온 뒤 방향을 고정하고 범위를 보여주는 시간. 이 시간이 끝날 때 범위 안에 있어야 맞는다.")]
        [SerializeField] internal float windupDuration = 0.5f;
        [Tooltip("공격 후 제자리에 멈춰 있는 시간(달라붙기 완화 + 반격 틈).")]
        [SerializeField] internal float recoveryDuration = 0.4f;
        [SerializeField] internal float attackAreaLength = 2f;
        [SerializeField] internal float attackAreaWidth = 1f;

        [Header("거리 확보 (2026-09-23 달라붙기 개선, 수치 전부 [PLAYTEST])")]
        [Tooltip("플레이어에게 맞았을 때 밀려나는 거리(칸).")]
        [SerializeField] internal float knockbackDistance = 0.8f;
        [SerializeField] internal float knockbackDuration = 0.15f;
        [Tooltip("공격 후딜 동안 뒤로 물러나는 속도(이동 속도 대비 배율).")]
        [SerializeField] internal float recoveryRetreatSpeedMultiplier = 0.5f;

        [Header("명중 효과 — 부품 ⑥ (2026-10-02, 수치 전부 [PLAYTEST])")]
        [Tooltip("명중 시 플레이어 이동속도 배율(예: 0.6). 0이면 둔화 없음.")]
        [SerializeField] internal float onHitSlowMultiplier = 0f;
        [Tooltip("피격 둔화 지속 시간(초). 0이면 둔화 없음.")]
        [SerializeField] internal float onHitSlowDuration = 0f;
        [Tooltip("명중 시 플레이어를 밀어내는 거리(칸). 0이면 넉백 없음.")]
        [SerializeField] internal float onHitKnockbackDistance = 0f;
        [Tooltip("피격 넉백이 걸리는 시간(초).")]
        [SerializeField] internal float onHitKnockbackTime = 0.15f;
    }
}
