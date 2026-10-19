using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>돌진 부품 ①(소·말·개) 고유 수치(리팩터 C4, ChargeMonsterBehavior에서 이름·기본값·Tooltip 그대로 이동). 공통 수치는 <see cref="MonsterStatsConfig"/>.
    /// 연출 값(wallStunTint·barkIcon·barkIconColor·barkIconHeight)은 프리팹(ChargeMonsterBehavior)에 남는다.</summary>
    [CreateAssetMenu(menuName = "Absorb/Monster Config/Charge", fileName = "ChargeMonsterConfig")]
    public class ChargeMonsterConfig : MonsterStatsConfig
    {
        [Header("돌진 부품 ① [PLAYTEST]")]
        [Tooltip("이 거리 안에 플레이어가 있으면 멈춰서 돌진 예고를 시작한다.")]
        [SerializeField] internal float chargeTriggerRange = 3.5f;
        [SerializeField] internal float chargeSpeed = 6f;
        [Tooltip("돌진 한 번의 최대 거리(칸) = 예고 직선 길이.")]
        [SerializeField] internal float chargeDistance = 5f;
        [Tooltip("돌진 판정·예고 폭(칸).")]
        [SerializeField] internal float chargeWidth = 1f;
        [SerializeField] internal float wallStunDuration = 1.5f;
        [Tooltip("벽 없이 거리를 다 갔을 때 멈춰 있는 시간.")]
        [SerializeField] internal float noWallPause = 0.3f;
        [Tooltip("연속 돌진 횟수(1 = 한 번).")]
        [SerializeField] internal int chargeCount = 1;
        [Tooltip("두 번째 돌진부터 가장 가까운 플레이어 쪽으로 다시 조준할지.")]
        [SerializeField] internal bool reaimBetweenCharges = true;
        [Tooltip("두 번째 돌진부터의 짧은 재예고 시간.")]
        [SerializeField] internal float reaimWindupDuration = 0.4f;

        [Header("짖고 달려들기 옵션 (개, 결정 ㊸A) [PLAYTEST] — 기본 꺼짐")]
        [Tooltip("마지막 돌진이 끝난 뒤(맞혔든 아니든) 돌진 반대 방향으로 짧게 물러난다.")]
        [SerializeField] internal bool retreatAfterCharge = false;
        [SerializeField] internal float retreatDistance = 2f;
        [SerializeField] internal float retreatDuration = 0.25f;
    }
}
