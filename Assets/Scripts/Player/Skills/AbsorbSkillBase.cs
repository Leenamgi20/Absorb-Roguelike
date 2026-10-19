using System.Collections;
using AbsorbRoguelike.Monster;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 동물 흡수 스킬 1종(리팩터 B안 단계 5a, 설계 3.2 '동물 스킬 구조'). MonoBehaviour가 아닌 일반 C# 클래스 —
    /// 잡몹 MonsterBehaviorController처럼 '골격(PlayerCombat·PlayerDash·PlayerGuard) + 훅 덮어쓰기' 방식이다.
    /// 기본 구현은 모두 '스킬 없음' 동작(아무것도 안 함). AbsorbSkillHost가 만들고 보관한다.
    /// 5a에서 호출되는 훅: ModifyAttack·TryReplaceAttack·AllowsHit·HandlesServerHit·ServerPerformSkillHit·GetHitSessionDuration·
    /// TryGetHitFx·ServerOnConeHitMonster·ShowAttackFx·LateTickLocal·LateTickVisual.
    /// 5b에서 호출되기 시작한 훅: ModifyDash·OnGuardBlocked·OnUnequip(next), 5b에서 추가한 훅: TickLocalDash·OnDashStart·OnDashEnd·
    /// ServerOnDashCommand·ModifyGuardBlockAngle(말·닭·돼지·양).
    /// 5c에서 호출되기 시작한 훅: OnHitEnemy(쥐), 5c에서 추가한 훅: LocksAttackWhileMoving·ResumesAttackWhenStopped·SkipsAttackRecovery·
    /// WeaponFollowsFacingWhileAttacking·GetMinHitInterval·TryGetFollowingHitFx·ServerOnAttackCommand·TickServer·OnStopServer(쥐·용·원숭이·개).
    /// 5b까지 빈 시그니처였던 Tick(float dt)은 호출부가 없어 TickServer(bool equipped)로 바꿨다(개 동료 — 장착 여부가 필요).
    /// OnEquip은 Host.Equip이 부르지만 덮어쓰는 스킬이 없다.
    /// </summary>
    internal abstract class AbsorbSkillBase
    {
        protected readonly AbsorbSkillContext ctx;
        protected readonly AbsorbSkillConfig config;

        public AbsorbRoguelike.Absorption.AbsorbSkill Kind { get; }

        protected AbsorbSkillBase(AbsorbSkillContext ctx, AbsorbSkillConfig config, AbsorbRoguelike.Absorption.AbsorbSkill kind)
        {
            this.ctx = ctx;
            this.config = config;
            Kind = kind;
        }

        // ── 장착 ──────────────────────────────────────────────────────────────────────────────────────
        public virtual void OnEquip() { }

        /// <summary>이 스킬이 Current에서 빠질 때(Host.Equip — 같은 스킬 재장착 포함). next = 새로 장착하는 스킬 값(단계 5b: 양털 리셋 조건에 쓴다).
        /// 서버(OnServerTraitChanged)·원격 클라(OnSyncedSkillChanged) 양쪽에서 불리므로 서버 전용 처리는 ctx.IsServer로 거른다.</summary>
        public virtual void OnUnequip(AbsorbRoguelike.Absorption.AbsorbSkill next) { }

        // ── 대시(5b) ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>대시 파라미터(PlayerDash.GetDashParams — 로컬 대시 시작과 서버 ServerPerformHit(DashPath) 재계산 양쪽). 말·닭 경로 대미지.</summary>
        public virtual void ModifyDash(ref PlayerDash.DashParams dash) { }

        /// <summary>로컬 플레이어 Update 로컬 분기 끝(옛 UpdateHorseDashCharges 자리, 용 브레스 재개·대시 쿨타임 감소 앞).
        /// Host가 모든 스킬 객체에 장착 여부와 함께 부른다(옛 코드가 말이 아닐 때도 매 프레임 리셋 분기를 돌았으므로).</summary>
        public virtual void TickLocalDash(bool equipped) { }

        /// <summary>대시가 실제로 시작될 때(TryStartDash — 대시 중·쿨타임 검사 통과 뒤, 공격 끊기·알 요청 앞). 현재 스킬만. 말 연속 대시 횟수.</summary>
        public virtual void OnDashStart(ref PlayerDash.DashParams dash) { }

        /// <summary>대시 이동이 끝날 때(DashRoutine — IsDashing=false·통과 해제 직후, 옛 '말: 이어쓰기 창 열기' 자리). 현재 스킬과 무관하게
        /// 모든 스킬 객체에 부른다(옛 코드는 대시 도중 스킬이 바뀌어도 시작 때 값(DeferCooldown)으로 창을 열었다).</summary>
        public virtual void OnDashEnd(PlayerDash.DashParams dash) { }

        /// <summary>서버: 로컬 플레이어가 대시마다 보내는 CmdLayEgg 본문(현재 스킬만). 닭 알 낳기.</summary>
        public virtual void ServerOnDashCommand(Vector2 position) { }

        // ── 가드(5b) ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>가드 무적 부채꼴 폭(서버 판정 IsAttackBlockedByGuard·가드 부채꼴 표시 공용). 돼지 확장.</summary>
        public virtual void ModifyGuardBlockAngle(ref float angle) { }

        /// <summary>서버: 가드로 공격을 막아낸 순간(PlayerGuard.OnGuardBlockedServer, 현재 스킬만). 돼지 반격·양털.</summary>
        public virtual void OnGuardBlocked(Vector2 attackerPos) { }

        // ── 명중·서버 틱(5c) ─────────────────────────────────────────────────────────────────────────
        /// <summary>서버: 플레이어 명중 경로 공통 후처리(PlayerCombat.OnEnemyHitServer — LastHitTarget/LastHitTime 기록 뒤, 현재 스킬만). 쥐 취약 스택.</summary>
        public virtual void OnHitEnemy(GameObject target) { }

        /// <summary>서버 매 프레임(PlayerController.Update의 isServer 첫 줄 — 옛 ServerUpdateDogCompanion 자리, 원격 플레이어 오브젝트 포함).
        /// Host가 모든 스킬 객체에 장착 여부와 함께 부른다(옛 코드가 개가 아닐 때도 매 프레임 '있으면 제거' 분기를 돌았으므로). 개 동료.</summary>
        public virtual void TickServer(bool equipped) { }

        /// <summary>서버 정지(PlayerController.OnStopServer — 옛 본문 자리). 모든 스킬 객체에. 개 동료 제거.</summary>
        public virtual void OnStopServer() { }

        /// <summary>서버: 로컬 플레이어가 공격마다 보내는 CmdThrowBanana 본문(현재 스킬만, 5b ServerOnDashCommand와 같은 패턴). 원숭이 바나나.</summary>
        public virtual void ServerOnAttackCommand(Vector2 direction) { }

        // ── 공격 루프 성질(5c) ───────────────────────────────────────────────────────────────────────
        /// <summary>이동 입력 중에는 공격(스킬 공격·대검 휘두르기·뱉기 모두)을 막는다 — 옛 PlayerCombat.IsDragonMoveLocked의 '용' 조건. 용.</summary>
        public virtual bool LocksAttackWhileMoving => false;

        /// <summary>공격 버튼을 누른 채 멈추면 공격 루프를 다시 시작한다 — 옛 PlayerCombat.TryResumeDragonBreath의 '용' 조건. 용.</summary>
        public virtual bool ResumesAttackWhenStopped => false;

        /// <summary>공격 루프에서 스윙 사이 재사용 대기(attackRecoveryDelay)를 건너뛴다 — 옛 AttackRoutine의 '용 아님' 예외. 용(틱 간격만으로 이어 뿜음).</summary>
        public virtual bool SkipsAttackRecovery => false;

        /// <summary>공격 중에도 무기가 마우스 방향을 따라 돈다 — 옛 PlayerWeaponView.UpdateWeaponFacing의 '용 아님' 예외. 용(휘두르기 연출 없음).</summary>
        public virtual bool WeaponFollowsFacingWhileAttacking => false;

        // ── 공격(5a) ──────────────────────────────────────────────────────────────────────────────────
        /// <summary>스윙 형태를 바꾼다(반경·반각·대미지/넉백 배율·추가 타·연출). spec은 '스킬 없음' 값으로 채워져 온다.</summary>
        public virtual void ModifyAttack(ref AttackSpec spec) { }

        /// <summary>기본 휘두르기 전체를 다른 공격 코루틴으로 바꾼다(소 돌진). true면 PerformSingleSwing이 routine을 끝까지 돌린 뒤
        /// IsAttacking=false로 끝낸다.</summary>
        public virtual bool TryReplaceAttack(Vector2 facing, out IEnumerator routine)
        {
            routine = null;
            return false;
        }

        /// <summary>원격 판정 요청 검증(ValidateHitRequest)의 '스킬에 맞는 종류인가'. 기본 = 대검 스윙만 허용.</summary>
        public virtual bool AllowsHit(PlayerCombat.HitKind kind) => kind == PlayerCombat.HitKind.Swing;

        /// <summary>이 스킬이 서버 판정을 맡는 판정 종류인가(호랑이 TigerFollowup·토끼 EarWhip·소 OxCharge·용 DragonTick(5c)).
        /// 판정 시점의 현재 스킬과 무관하게 이 종류를 처리한다 — 단계 5a 이전에도 이 세 종류는 서버 판정 때 현재 스킬을 다시 보지 않았다
        /// (호스트가 공격 도중 피격으로 특성을 잃어도 이미 시작한 3연타·돌진은 같은 값으로 판정).</summary>
        public virtual bool HandlesServerHit(PlayerCombat.HitKind kind) => false;

        /// <summary>서버 판정 1회(HandlesServerHit가 true인 종류만). 묶음 검증·갱신(step·Followups 증가)은 PlayerCombat이 끝낸 뒤 온다.
        /// 반환 = 살아있는 적의 체력을 실제로 깎았는가(적중 회복 판단).</summary>
        public virtual bool ServerPerformSkillHit(PlayerCombat.HitKind kind, Vector2 facing, PlayerCombat.HitSession session) => false;

        /// <summary>같은 묶음(step)이 이어지는 판정 종류의 지속 시간(초) — 소 돌진 oxChargeDuration. 검증의 묶음 창·최소 간격에 쓴다.</summary>
        public virtual float GetHitSessionDuration(PlayerCombat.HitKind kind) => 0f;

        /// <summary>판정 종류별 새 묶음 사이의 정상 주기(초) — 옛 PlayerCombat.MinSessionInterval의 DragonTick(dragonBreathTickInterval). 맡지 않으면 false.</summary>
        public virtual bool TryGetMinHitInterval(PlayerCombat.HitKind kind, out float interval)
        {
            interval = 0f;
            return false;
        }

        /// <summary>서버가 다른 화면에 보낼 '방향을 따라 도는' 부채꼴 연출 값(반경·반각·시간·색) — 옛 ServerBroadcastHitFx DragonTick의 BreathCone.
        /// 용 브레스. 없으면 false.</summary>
        public virtual bool TryGetFollowingHitFx(PlayerCombat.HitKind kind, out float radius, out float halfAngle, out float duration, out Color color)
        {
            radius = halfAngle = duration = 0f;
            color = default;
            return false;
        }

        /// <summary>서버가 다른 화면에 보낼 부채꼴 연출 값(반경·반각·시간) — 소 돌진. 없으면 false.</summary>
        public virtual bool TryGetHitFx(PlayerCombat.HitKind kind, out float radius, out float halfAngle, out float duration)
        {
            radius = halfAngle = duration = 0f;
            return false;
        }

        /// <summary>DoConeHit(서버)가 잡몹(behavior != null)을 처리한 직후(넉백 뒤, 생존 무관) — 뱀 둔화.</summary>
        public virtual void ServerOnConeHitMonster(MonsterBehaviorController behavior) { }

        // ── 연출(판정과 무관) ─────────────────────────────────────────────────────────────────────────
        /// <summary>스킬 고유 스윙 연출 시작(로컬 스윙·원격 RpcAttackFx) — 토끼 귀 직선.</summary>
        public virtual void ShowAttackFx(Vector2 facing) { }

        /// <summary>로컬 플레이어 LateUpdate(가드 표시 다음) — equipped = 지금 장착한 스킬인가. 토끼 사거리 링.</summary>
        public virtual void LateTickLocal(bool equipped) { }

        /// <summary>모든 오브젝트 LateUpdate(링 다음) — 진행 중인 스킬 연출 타이머. 장착 여부와 무관(재생 중 스킬이 바뀌어도 끝까지 돈다).</summary>
        public virtual void LateTickVisual(float dt) { }
    }
}
