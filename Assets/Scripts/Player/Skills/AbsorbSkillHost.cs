using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 현재 흡수 스킬 객체 보관(리팩터 B안 단계 5a, 5b에서 말·닭·돼지·양, 5c에서 쥐·용·원숭이·개 추가 — 12종 전부, 설계 3.2). Player.prefab 루트에 붙는다.
    ///
    /// 종류 = MonoBehaviour(설계 3.2의 'Host만 NetworkBehaviour'와 다름): 5a는 Command·ClientRpc·SyncVar를 새로 만들지 않으므로
    /// MonoBehaviour로 시작한다 → Player.prefab의 Mirror NetworkBehaviour 목록·인덱스가 단계 4b와 같다(컴포넌트 순서 변화 없음).
    /// 5b도 새 Command·Rpc·SyncVar가 없다 — 닭 CmdLayEgg은 PlayerDash, 돼지 TargetShowPigCounter는 PlayerGuard, 털 SyncVar sheepWool은
    /// PlayerController, TargetShowSheepBurst는 PlayerCombat에 그대로 두고 스킬 객체는 본문만 맡는다.
    /// 5c도 MonoBehaviour 유지 — CmdThrowBanana는 PlayerCombat에 남김(본문만 MonkeyBananaSkill.ServerOnAttackCommand로 위임).
    /// 이유: Mirror NetworkBehaviour 목록·인덱스 불변, 새 Command 불필요(개 스폰·제거는 서버 Update에서, 용 판정은 기존 CmdPerformHit로 충분).
    ///
    /// 장착: PlayerController가 _absorbSkill을 바꾸는 두 자리(서버 OnServerTraitChanged, 원격 클라 OnSyncedSkillChanged)에서 Equip을 부른다.
    /// 스킬 객체: 12종(5a 호랑이·뱀·소·토끼, 5b 말·닭·돼지·양, 5c 쥐·용·원숭이·개)은 Awake에서 한 번씩 만들어 두고 Equip은 Current만 바꾼다. 스킬 객체는 수치를 config에서
    /// 읽기만 하고 판정 상태가 없어 다시 만드는 것과 결과가 같다. 미리 만드는 이유: ① 서버 판정 종류(TigerFollowup·EarWhip·OxCharge, 5c DragonTick)를
    /// 판정 시점 스킬과 무관하게 처리하던 기존 동작(FindServerHitOwner), ② 귀 직선 연출이 스킬이 바뀌어도/원격에서 동기화가 늦어도
    /// 끝까지 재생되던 기존 동작(렌더러를 스킬 객체가 계속 들고 있음). 5b의 말 연속 대시 상태·닭 알 목록·돼지 반격 쿨타임도 옛 코드에서
    /// 컴포넌트 필드라 스킬 교체 뒤에도 유지됐다 — 미리 만든 객체가 들고 있어 같다. 5c의 용 속성 시각·원숭이 바나나 시각·개 동료 참조도 같은 이유로 객체 필드.
    /// None·config 누락이면 Current = null → '스킬 없음'(대검 기본 공격) 경로(config 누락은 Awake 에러 로그로 드러난다).
    /// 자체 Update류는 없다 — PlayerController.LateUpdate가 LateTickLocal·LateTickVisual을 기존 귀 링·귀 이펙트 자리에서,
    /// PlayerController.Update 로컬 분기가 TickLocalDash를 옛 UpdateHorseDashCharges 자리에서, Update의 isServer 첫 줄이 TickServer를
    /// 옛 ServerUpdateDogCompanion 자리에서, PlayerController.OnStopServer가 StopServer를 부른다.
    /// </summary>
    public class AbsorbSkillHost : MonoBehaviour
    {
        [Header("흡수 스킬 수치 에셋 (Assets/Data/AbsorbSkills/, 단계 5a)")]
        [SerializeField] private TigerSkillConfig tigerConfig;
        [SerializeField] private SnakeSkillConfig snakeConfig;
        [SerializeField] private OxSkillConfig oxConfig;
        [SerializeField] private RabbitSkillConfig rabbitConfig;

        [Header("흡수 스킬 수치 에셋 (Assets/Data/AbsorbSkills/, 단계 5b)")]
        [SerializeField] private HorseSkillConfig horseConfig;
        [SerializeField] private RoosterSkillConfig roosterConfig;
        [SerializeField] private PigSkillConfig pigConfig;
        [SerializeField] private SheepSkillConfig sheepConfig;

        [Header("흡수 스킬 수치 에셋 (Assets/Data/AbsorbSkills/, 단계 5c)")]
        [SerializeField] private RatSkillConfig ratConfig;
        [SerializeField] private DragonSkillConfig dragonConfig;
        [SerializeField] private MonkeySkillConfig monkeyConfig;
        [SerializeField] private DogSkillConfig dogConfig;

        private AbsorbSkillContext _ctx;
        private List<AbsorbSkillBase> _skills;

        /// <summary>현재 장착한 스킬 객체. None·config 누락이면 null(→ 대검 기본 공격, Swing 판정만 허용).</summary>
        internal AbsorbSkillBase Current { get; private set; }

        /// <summary>마지막으로 Equip한 스킬 값(config 누락 여부와 무관).</summary>
        public AbsorbRoguelike.Absorption.AbsorbSkill CurrentKind { get; private set; }

        private void Awake()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (_skills != null)
            {
                return;
            }

            _ctx = new AbsorbSkillContext(gameObject);
            _skills = new List<AbsorbSkillBase>(12);
            AddSkill(tigerConfig, "tigerConfig", c => new TigerComboSkill(_ctx, c));
            AddSkill(snakeConfig, "snakeConfig", c => new SnakeBindSkill(_ctx, c));
            AddSkill(oxConfig, "oxConfig", c => new OxKnockbackSkill(_ctx, c));
            AddSkill(rabbitConfig, "rabbitConfig", c => new RabbitEarWhipSkill(_ctx, c));
            AddSkill(horseConfig, "horseConfig", c => new HorseDoubleDashSkill(_ctx, c));   // 단계 5b
            AddSkill(roosterConfig, "roosterConfig", c => new RoosterEggSkill(_ctx, c));
            AddSkill(pigConfig, "pigConfig", c => new PigCounterSkill(_ctx, c));
            AddSkill(sheepConfig, "sheepConfig", c => new SheepWoolSkill(_ctx, c));
            AddSkill(ratConfig, "ratConfig", c => new RatGnawSkill(_ctx, c));             // 단계 5c
            AddSkill(dragonConfig, "dragonConfig", c => new DragonBreathSkill(_ctx, c));
            AddSkill(monkeyConfig, "monkeyConfig", c => new MonkeyBananaSkill(_ctx, c));
            AddSkill(dogConfig, "dogConfig", c => new DogCompanionSkill(_ctx, c));
        }

        private void AddSkill<T>(T cfg, string fieldName, System.Func<T, AbsorbSkillBase> create) where T : AbsorbSkillConfig
        {
            if (cfg == null)
            {
                Debug.LogError($"[AbsorbSkillHost] {name}: {fieldName} 참조가 비어 있다 — Player.prefab의 AbsorbSkillHost에 Assets/Data/AbsorbSkills 에셋을 연결해야 한다. 이 스킬은 기본 공격으로 동작한다.", this);
                return;
            }

            _skills.Add(create(cfg));
        }

        /// <summary>흡수 스킬 교체. 이전 스킬 OnUnequip(next) → Current 교체 → 새 스킬 OnEquip.
        /// 5b: 양 OnUnequip이 옛 OnServerTraitChanged의 '서버이고 양이 아니면 털 0'을 맡는다(같은 함수 안, SetPassiveTrait 앞 — 순서 동일).</summary>
        public void Equip(AbsorbRoguelike.Absorption.AbsorbSkill skill)
        {
            EnsureInit();
            Current?.OnUnequip(skill);
            CurrentKind = skill;
            Current = Find(skill);
            Current?.OnEquip();
        }

        /// <summary>종류가 같은 스킬 객체(단계 5c부터 12종 모두 — 5b까지의 '객체화된 것만' switch는 삭제). None·config 누락이면 null.</summary>
        private AbsorbSkillBase Find(AbsorbRoguelike.Absorption.AbsorbSkill skill)
        {
            if (skill == AbsorbRoguelike.Absorption.AbsorbSkill.None)
            {
                return null;
            }

            foreach (AbsorbSkillBase s in _skills)
            {
                if (s.Kind == skill)
                {
                    return s;
                }
            }

            return null; // config 누락(Awake에서 에러 로그)
        }

        /// <summary>서버 판정 종류(TigerFollowup·EarWhip·OxCharge·DragonTick)를 맡는 스킬 객체 — 현재 스킬과 무관. 없으면 null.</summary>
        internal AbsorbSkillBase FindServerHitOwner(PlayerCombat.HitKind kind)
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                if (s.HandlesServerHit(kind))
                {
                    return s;
                }
            }

            return null;
        }

        /// <summary>종류로 스킬 객체 찾기(현재 장착과 무관). config 누락이면 null. PlayerDash.HorseDashesLeft 등 외부 값 위임용(단계 5b).</summary>
        internal T Get<T>() where T : AbsorbSkillBase
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                if (s is T t)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>양 config(HUD용 SheepWoolMax·원격 소유자 TargetShowSheepBurst가 읽는다, 단계 5b). 비어 있으면 null.</summary>
        internal SheepSkillConfig SheepConfig => sheepConfig;

        /// <summary>로컬 플레이어 Update 로컬 분기 끝의 옛 UpdateHorseDashCharges 자리(단계 5b) — 장착 여부를 알려 주며 모든 스킬 객체에 부른다
        /// (말이 아니면 매 프레임 이어쓰기 창 정리·횟수 리셋 — 옛 동작과 같음).</summary>
        internal void TickLocalDash()
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                s.TickLocalDash(s == Current);
            }
        }

        /// <summary>서버 매 프레임 — PlayerController.Update의 isServer 첫 줄(옛 ServerUpdateDogCompanion 자리, 원격 플레이어 오브젝트 포함, 단계 5c).
        /// 장착 여부를 알려 주며 모든 스킬 객체에 부른다(개가 아니면 남은 동료 제거 — 옛 매 프레임 판정과 같음).</summary>
        internal void TickServer()
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                s.TickServer(s == Current);
            }
        }

        /// <summary>서버 정지 — PlayerController.OnStopServer(옛 본문 자리, 단계 5c). 모든 스킬 객체에(개 동료 제거).</summary>
        internal void StopServer()
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                s.OnStopServer();
            }
        }

        /// <summary>서버: PlayerCombat.CmdThrowBanana 본문 위임(단계 5c) — 현재 스킬만(옛 'Skill != MonkeyBanana면 무시'와 같음).</summary>
        internal void ServerOnAttackCommand(Vector2 direction)
        {
            Current?.ServerOnAttackCommand(direction);
        }

        /// <summary>PlayerDash.DashRoutine 대시 이동 끝(옛 '말: 이어쓰기 창 열기' 자리, 단계 5b) — 현재 스킬과 무관하게 모든 스킬 객체에.</summary>
        internal void NotifyDashEnd(PlayerDash.DashParams dash)
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                s.OnDashEnd(dash);
            }
        }

        /// <summary>로컬 플레이어 LateUpdate의 옛 UpdateEarWhipRing 자리 — 장착 여부를 알려 주며 모든 스킬 객체에 부른다(장착 해제 시 링 숨김).</summary>
        internal void LateTickLocal()
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                s.LateTickLocal(s == Current);
            }
        }

        /// <summary>모든 오브젝트 LateUpdate의 옛 UpdateEarWhipVfx 자리 — 장착 여부와 무관하게 진행 중인 연출을 돌린다.</summary>
        internal void LateTickVisual(float dt)
        {
            EnsureInit();
            foreach (AbsorbSkillBase s in _skills)
            {
                s.LateTickVisual(dt);
            }
        }
    }
}
