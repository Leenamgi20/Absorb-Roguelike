namespace AbsorbRoguelike.Audio
{
    /// <summary>
    /// 효과음 id 40개(2026-10-14). 값 = docs/03_아트/사운드_제작_목록.md 5장의 파일 이름(확장자 제외).
    /// 같은 id에 _1/_2/_3 변형 파일이 있으면 SfxLibrary가 한 항목으로 묶고 재생 때 무작위로 고른다.
    /// 재생 위치 표: docs/04_구현위키/효과음.md.
    /// </summary>
    public static class SfxId
    {
        // 플레이어
        public const string SwordSwing = "sfx_sword_swing";
        public const string Hit = "sfx_hit";
        public const string HitStrong = "sfx_hit_strong";
        public const string Guard = "sfx_guard";
        public const string Dash = "sfx_dash";
        public const string PlayerHurt = "sfx_player_hurt";
        public const string PlayerDown = "sfx_player_down";
        public const string ReviveLoop = "sfx_revive_loop";
        public const string ReviveDone = "sfx_revive_done";

        // 흡수
        public const string AbsorbHold = "sfx_absorb_hold";
        public const string AbsorbSuccess = "sfx_absorb_success";
        public const string TraitEquip = "sfx_trait_equip";
        public const string TraitLost = "sfx_trait_lost";
        public const string DebrisGrab = "sfx_debris_grab";
        public const string DebrisSpit = "sfx_debris_spit";

        // 속성이 걸릴 때
        public const string ElemFire = "sfx_elem_fire";
        public const string ElemIce = "sfx_elem_ice";
        public const string ElemElectric = "sfx_elem_electric";
        public const string ElemPoison = "sfx_elem_poison";
        public const string ElemEarth = "sfx_elem_earth";

        // 몬스터
        public const string MonsterHurt = "sfx_monster_hurt";
        public const string MonsterDie = "sfx_monster_die";
        public const string MonsterWarn = "sfx_monster_warn";
        public const string DogBark = "sfx_dog_bark";
        public const string DragonBreath = "sfx_dragon_breath";
        public const string ShieldBlock = "sfx_shield_block";

        // 보스
        public const string BossRoar = "sfx_boss_roar";
        public const string BossCharge = "sfx_boss_charge";
        public const string BossWallHit = "sfx_boss_wall_hit";
        public const string BossSlam = "sfx_boss_slam";
        public const string BossPhase = "sfx_boss_phase";
        public const string BossDown = "sfx_boss_down";

        // 던전
        public const string DoorLock = "sfx_door_lock";
        public const string RoomClear = "sfx_room_clear";
        public const string PortalOpen = "sfx_portal_open";
        public const string FloorEnter = "sfx_floor_enter";

        // UI
        public const string UiHover = "sfx_ui_hover";
        public const string UiClick = "sfx_ui_click";
        public const string UiOpen = "sfx_ui_open";
        public const string UiSelect = "sfx_ui_select";

        /// <summary>전체 id(목록 문서 순서).</summary>
        public static readonly string[] All =
        {
            SwordSwing, Hit, HitStrong, Guard, Dash, PlayerHurt, PlayerDown, ReviveLoop, ReviveDone,
            AbsorbHold, AbsorbSuccess, TraitEquip, TraitLost, DebrisGrab, DebrisSpit,
            ElemFire, ElemIce, ElemElectric, ElemPoison, ElemEarth,
            MonsterHurt, MonsterDie, MonsterWarn, DogBark, DragonBreath, ShieldBlock,
            BossRoar, BossCharge, BossWallHit, BossSlam, BossPhase, BossDown,
            DoorLock, RoomClear, PortalOpen, FloorEnter,
            UiHover, UiClick, UiOpen, UiSelect,
        };

        /// <summary>반복(루프) 소리 — 임포트 때 이음매 유지 설정, 재생은 Sfx.StartLoop.</summary>
        public static bool IsLoop(string id) => id == AbsorbHold || id == ReviveLoop;
    }
}
