using System;
using AbsorbRoguelike.Monster;
using UnityEngine;

namespace AbsorbRoguelike.Combat
{
    /// <summary>잡몹 넉백 방식 — 기존 판정 함수마다 다른 규칙을 그대로 보존한다.</summary>
    public enum KnockbackMode
    {
        None,
        /// <summary>배율, 값이 0보다 클 때만(기본공격 부채꼴·귀 채찍).</summary>
        Scale,
        /// <summary>배율, 0 확인 없이 항상 호출(소 돌진 — 기존 동작 그대로).</summary>
        ScaleAlways,
        /// <summary>절대 거리(칸) ÷ 몬스터별 KnockbackDistance, 둘 다 0보다 클 때만(대시 경로·바나나·개 물기).</summary>
        AbsoluteDistance,
    }

    /// <summary>판정 함수별 차이를 담는 옵션. 각 호출부에서 모든 값을 명시한다.</summary>
    public struct HitOptions
    {
        /// <summary>속성 적용 횟수(잡몹은 살아 있을 때, 보스는 ApplyElementToBoss일 때).</summary>
        public int ElementApplications;

        /// <summary>보스에게 속성을 적용할지(기본공격 부채꼴은 흡수 스킬이 있을 때만).</summary>
        public bool ApplyElementToBoss;

        /// <summary>최근 타격 기록(OnEnemyHitServer)을 남길지 — 개 물기만 false.</summary>
        public bool RecordHit;

        public KnockbackMode Knockback;

        /// <summary>Scale·ScaleAlways면 배율, AbsoluteDistance면 거리(칸).</summary>
        public float KnockbackValue;
    }

    public enum HitTargetKind
    {
        None,
        Monster,
        Boss,
    }

    public struct HitResult
    {
        public HitTargetKind TargetKind;

        /// <summary>이번 타격으로 체력이 실제로 줄었는가.</summary>
        public bool Damaged;

        /// <summary>잡몹 대상일 때의 행동 컴포넌트(없으면 null) — 뱀 둔화 등 호출부 후처리용.</summary>
        public MonsterBehaviorController Behavior;
    }

    /// <summary>
    /// 플레이어 → 적 대미지 공통 처리(서버 전용, 리팩터 B안 단계 1, 2026-10-08). 기존 DoConeHit·DoEarWhipHit·OxChargeHitOnce·
    /// HitCircleOnce·ServerApplySkillHit 안의 잡몹/보스 블록을 순서 그대로 옮겼다. 동작 변경 없음.
    /// 잡몹: ServerTakeHit → 살아 있으면 속성 N회 → 기록 → onAliveBeforeKnockback → 넉백(죽었어도).
    /// 보스: ServerTakeHit → 속성(조건부, 생존 무관) → 처치 안 됐으면 기록. 넉백 없음.
    /// 스킬 전용 후처리(뱀 둔화·보스 경직)는 호출부에 남는다.
    /// </summary>
    public static class HitResolver
    {
        /// <param name="applyElement">속성 적용(대상, isBoss) — PlayerController.ApplyElementOnHit 감싼 것.</param>
        /// <param name="recordHit">최근 타격 기록 — PlayerController.OnEnemyHitServer.</param>
        /// <param name="onAliveBeforeKnockback">잡몹이 살아 있을 때 기록 직후·넉백 전에 부른다(귀 채찍 끝부분 경직). 인자 behavior는 null일 수 있다.</param>
        public static HitResult Resolve(GameObject targetObject, in HitInfo hit, in HitOptions options,
            Action<GameObject, bool> applyElement, Action<GameObject> recordHit,
            Action<MonsterBehaviorController> onAliveBeforeKnockback = null)
        {
            var result = new HitResult();
            if (targetObject == null)
            {
                return result;
            }

            MonsterStub monster = targetObject.GetComponent<MonsterStub>();
            if (monster != null)
            {
                IDamageable damageable = monster;
                result.TargetKind = HitTargetKind.Monster;
                result.Damaged = damageable.ServerTakeHit(hit);
                var behavior = monster.GetComponent<MonsterBehaviorController>();
                result.Behavior = behavior;
                if (!damageable.IsDead)
                {
                    for (int i = 0; i < options.ElementApplications; i++)
                    {
                        applyElement?.Invoke(monster.gameObject, false); // 한 방에 죽어 Destroy 예정인 대상 제외
                    }

                    if (options.RecordHit)
                    {
                        recordHit?.Invoke(monster.gameObject);
                    }

                    onAliveBeforeKnockback?.Invoke(behavior);
                }

                if (behavior != null)
                {
                    ApplyKnockback(behavior, hit.Origin, options); // 죽었어도 적용(기존 동작)
                }

                return result;
            }

            BossStub boss = targetObject.GetComponent<BossStub>();
            if (boss != null)
            {
                IDamageable damageable = boss;
                result.TargetKind = HitTargetKind.Boss;
                result.Damaged = damageable.ServerTakeHit(hit);
                if (options.ApplyElementToBoss)
                {
                    for (int i = 0; i < options.ElementApplications; i++)
                    {
                        applyElement?.Invoke(boss.gameObject, true); // 이번 타로 쓰러져도 호출(기존 동작)
                    }
                }

                if (options.RecordHit && !damageable.IsDead)
                {
                    recordHit?.Invoke(boss.gameObject);
                }
            }

            return result;
        }

        private static void ApplyKnockback(MonsterBehaviorController behavior, Vector2 origin, in HitOptions options)
        {
            switch (options.Knockback)
            {
                case KnockbackMode.Scale:
                    if (options.KnockbackValue > 0f)
                    {
                        behavior.ApplyKnockback(origin, options.KnockbackValue);
                    }

                    break;
                case KnockbackMode.ScaleAlways:
                    behavior.ApplyKnockback(origin, options.KnockbackValue);
                    break;
                case KnockbackMode.AbsoluteDistance:
                    if (options.KnockbackValue > 0f && behavior.KnockbackDistance > 0f)
                    {
                        behavior.ApplyKnockback(origin, options.KnockbackValue / behavior.KnockbackDistance);
                    }

                    break;
            }
        }
    }
}
