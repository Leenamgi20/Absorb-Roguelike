using AbsorbRoguelike.Monster;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Combat
{
    /// <summary>
    /// 보스 TakeDamage의 선택 인자(isOxCharge·isRatGnaw)와 1:1 대응하는 타격 표시(리팩터 B안 단계 1, 2026-10-08).
    /// </summary>
    [System.Flags]
    public enum HitFlags
    {
        None = 0,
        OxCharge = 1 << 0, // 소 돌진 명중 — 갑옷 상황 특성에서 갑옷을 크게 깎는다
        RatGnaw = 1 << 1,  // 쥐 갉기 명중 — 갑옷을 armorBreakRatExtra만큼 더 깎는다
    }

    /// <summary>
    /// 한 번의 타격 정보(서버 전용, 리팩터 B안 단계 1). 잡몹·보스 공통 진입점 IDamageable.ServerTakeHit에 넘긴다.
    /// 기본값(Kind = Normal, Flags = None)은 기존 TakeDamage 호출의 기본 인자와 같다.
    /// </summary>
    public struct HitInfo
    {
        /// <summary>대미지량(보스 맨손 1 고정·취약·갑옷 등은 대상 쪽 TakeDamage가 그대로 처리).</summary>
        public int Amount;

        /// <summary>공격 위치 — 잡몹 정면 방어(ModifyIncomingDamage)·넉백 기준.</summary>
        public Vector2 Origin;

        /// <summary>공격한 플레이어(보스 개인 할당량용). 잡몹은 쓰지 않는다.</summary>
        public NetworkIdentity Attacker;

        /// <summary>보스 대미지 규칙용 공격 종류(Weapon이면 보스가 1 고정 + BossDeflect 팝업). 잡몹은 쓰지 않는다.</summary>
        public DamageSource Source;

        /// <summary>대미지 숫자 색. 기본 Normal.</summary>
        public DamagePopupKind Kind;

        public HitFlags Flags;

        public HitInfo(int amount, Vector2 origin, NetworkIdentity attacker, DamageSource source,
            DamagePopupKind kind = DamagePopupKind.Normal, HitFlags flags = HitFlags.None)
        {
            Amount = amount;
            Origin = origin;
            Attacker = attacker;
            Source = source;
            Kind = kind;
            Flags = flags;
        }
    }
}
