namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 기본 공격(스윙)의 형태 값(리팩터 B안 단계 5a). PlayerCombat.BuildAttackSpec이 '스킬 없음' 값으로 채운 뒤 현재 흡수 스킬의
    /// ModifyAttack(ref)이 덮어쓴다. Default의 값은 단계 5a 이전 '스킬 없음' 동작과 정확히 같다.
    /// </summary>
    internal struct AttackSpec
    {
        /// <summary>부채꼴 판정 반지름(칸) — 기본 attackRadius, 호랑이 tigerAttackRadius, 뱀 snakeAttackRadius.</summary>
        public float Radius;
        /// <summary>부채꼴 반각(도) — 기본 attackHalfAngle, 호랑이 tigerAttackHalfAngle, 뱀 180(원 전체).</summary>
        public float HalfAngle;
        /// <summary>스윙 1타(HitKind.Swing) 대미지 배율 — 기본 1, 호랑이 tigerHitDamageScale.</summary>
        public float DamageScale;
        /// <summary>스윙 1타(HitKind.Swing) 넉백 배율 — 기본 1, 호랑이 0.</summary>
        public float KnockbackScale;
        /// <summary>스윙 한 번에 이어지는 추가 타 수(HitKind.TigerFollowup) — 기본 0, 호랑이 2(스윙 1/3·2/3 지점).</summary>
        public int ExtraHits;
        /// <summary>베기 이펙트가 있어도 판정 부채꼴을 겹쳐 그린다(옛 HasSpecialSwingShape — 호랑이·뱀).</summary>
        public bool DrawConeOverSlash;
        /// <summary>베기 이펙트가 휘두르는 동안 한 바퀴 돈다(옛 PlayerWeaponView의 뱀 spin).</summary>
        public bool SpinWeapon;
        /// <summary>DoConeHit 넉백 배율의 기준값 — 기본 baseKnockbackScale, 소 oxKnockbackScale.</summary>
        public float KnockbackBaseScale;
        /// <summary>스윙 판정 요청 종류 — 기본 Swing, 토끼 EarWhip.</summary>
        public PlayerCombat.HitKind SwingHitKind;
        /// <summary>부채꼴·베기 프레임 대신 스킬 연출(AbsorbSkillBase.ShowAttackFx)을 쓴다 — 토끼 귀 직선.</summary>
        public bool UseSkillSwingFx;
        /// <summary>DoConeHit 명중 플래그(HitInfo.flags) — 기본 None, 쥐 RatGnaw(옛 PlayerCombat.DoConeHit의 'rat ? HitFlags.RatGnaw : None', 단계 5c).</summary>
        public AbsorbRoguelike.Combat.HitFlags HitFlags;

        public static AttackSpec Default(float radius, float halfAngle, float knockbackBaseScale)
        {
            return new AttackSpec
            {
                Radius = radius,
                HalfAngle = halfAngle,
                DamageScale = 1f,
                KnockbackScale = 1f,
                ExtraHits = 0,
                DrawConeOverSlash = false,
                SpinWeapon = false,
                KnockbackBaseScale = knockbackBaseScale,
                SwingHitKind = PlayerCombat.HitKind.Swing,
                UseSkillSwingFx = false,
                HitFlags = AbsorbRoguelike.Combat.HitFlags.None,
            };
        }
    }
}
