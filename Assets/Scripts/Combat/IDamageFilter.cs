using UnityEngine;

namespace AbsorbRoguelike.Combat
{
    /// <summary>
    /// 플레이어가 맞기 전에 "이 공격을 무시/막는가"를 답하는 쪽의 공통 계약(리팩터 B안 단계 3, 2026-10-08).
    /// PlayerHealth는 같은 오브젝트의 구현체 목록을 차례로 물어볼 뿐, 누가 막는지 모른다(PlayerHealth → PlayerController 의존 제거).
    /// 구현체(단계 4b, 2026-10-08): PlayerDash(대시 무적 — IgnoresAllDamage만) · PlayerGuard(정면 가드 — BlocksFromDirection·OnBlockedFromDirection). PlayerController는 구현하지 않는다.
    /// 판정 순서는 PlayerHealth가 지킨다: 사망·디버그 무적·피격 무적·<see cref="IgnoresAllDamage"/> → 무시(가드 아님) /
    /// 위치를 아는 경로에서만 <see cref="BlocksFromDirection"/> → 가드로 막음 / 그다음 양털 보호막·대미지(PlayerHealth 안).
    /// 서버 전용 — PlayerHealth가 isServer를 확인한 뒤에만 묻는다.
    /// </summary>
    public interface IDamageFilter
    {
        /// <summary>모든 대미지를 통째로 무시하는가(예: 대시 무적). 가드 판정보다 먼저, 위치 없는 TakeDamage(int)에서도 묻는다.</summary>
        bool IgnoresAllDamage { get; }

        /// <summary>attackerPosition 쪽에서 온 공격을 막는가(예: 방향성 가드). 공격자 위치를 아는 경로에서만 묻는다. 막았으면 구현체가 막음 표시(가드 번쩍임)를 켤 수 있다 — 판정 결과에는 영향 없음.</summary>
        bool BlocksFromDirection(Vector2 attackerPosition);

        /// <summary>BlocksFromDirection이 true였을 때 PlayerHealth가 막음 로그 뒤에 부른다 — 막았을 때의 서버 반응(예: 돼지 반격·양털 모으기)은 구현체가 처리.</summary>
        void OnBlockedFromDirection(Vector2 attackerPosition);
    }
}
