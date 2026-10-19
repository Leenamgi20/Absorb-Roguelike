using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 흡수 스킬 훅에 넘기는 플레이어 컴포넌트 참조 묶음(리팩터 B안 단계 5a). AbsorbSkillHost가 Awake에서 한 번 만들어 모든 스킬 객체에
    /// 같은 인스턴스를 넘긴다. 스킬은 필요한 것만 쓴다. 참조만 들고 있고 상태는 없다.
    /// </summary>
    internal sealed class AbsorbSkillContext
    {
        public readonly PlayerController Controller;
        public readonly PlayerCombat Combat;
        public readonly PlayerGuard Guard;
        public readonly PlayerDash Dash;
        public readonly PlayerStats Stats;
        public readonly PlayerMotor Motor;
        public readonly PlayerWeaponView WeaponView;
        public readonly PlayerHealth Health;
        public readonly Rigidbody2D Rigidbody;
        public readonly Transform Transform;
        public readonly NetworkIdentity Identity;
        /// <summary>메인 능력(현재 속성) — 용 브레스 색(DragonBreathTint, 단계 5c). 없으면 null.</summary>
        public readonly AbsorbRoguelike.Abilities.MainAbilityController MainAbility;

        public AbsorbSkillContext(GameObject owner)
        {
            Controller = owner.GetComponent<PlayerController>();
            Combat = owner.GetComponent<PlayerCombat>();
            Guard = owner.GetComponent<PlayerGuard>();
            Dash = owner.GetComponent<PlayerDash>();
            Stats = owner.GetComponent<PlayerStats>();
            Motor = owner.GetComponent<PlayerMotor>();
            WeaponView = owner.GetComponent<PlayerWeaponView>();
            Health = owner.GetComponent<PlayerHealth>();
            Rigidbody = owner.GetComponent<Rigidbody2D>();
            Transform = owner.transform;
            Identity = owner.GetComponent<NetworkIdentity>();
            MainAbility = owner.GetComponent<AbsorbRoguelike.Abilities.MainAbilityController>();
        }

        /// <summary>서버인가(NetworkIdentity.isServer — 기존 PlayerCombat의 isServer 가드와 같은 값).</summary>
        public bool IsServer => Identity != null && Identity.isServer;
    }
}
