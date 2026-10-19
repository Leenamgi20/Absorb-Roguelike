namespace AbsorbRoguelike.Combat
{
    /// <summary>
    /// 플레이어 공격을 받는 대상(잡몹 MonsterStub·보스 BossStub) 공통 계약(리팩터 B안 단계 1, 2026-10-08).
    /// 서버 전용 — 클라이언트에서 부르면 대상 쪽 TakeDamage가 아무것도 하지 않아 false가 된다.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>타격을 적용한다. 반환: 이번 타격으로 체력이 실제로 줄었는가(적중 회복 dealtHit 판단과 같은 의미).</summary>
        bool ServerTakeHit(in HitInfo hit);

        /// <summary>잡몹은 체력 0 이하, 보스는 처치됨(IsDefeated).</summary>
        bool IsDead { get; }

        int CurrentHealth { get; }
    }
}
