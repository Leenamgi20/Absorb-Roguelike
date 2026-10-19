using AbsorbRoguelike.Monster;

namespace AbsorbRoguelike.Combat
{
    /// <summary>
    /// 둔화·경직을 받을 수 있는 적의 움직임 담당(잡몹 MonsterBehaviorController·보스 BossBrain) 공통 계약(리팩터 B안 단계 2, 2026-10-08).
    /// 상태이상(StatusEffectReceiver)이 잡몹인지 보스인지 몰라도 되게 한다. 한 오브젝트에 구현체는 하나만 붙는다.
    /// 넉백은 잡몹만 받으므로(보스는 밀리지 않음) 여기 넣지 않는다. 서버 전용 — 구현체가 isServer·처치 여부를 확인한다.
    /// </summary>
    public interface IStatusTarget
    {
        /// <summary>이동속도를 multiplier배로 duration초 동안 낮춘다. 출처(source)별 슬롯, 가장 강한 둔화 하나만 적용.</summary>
        void ApplySlow(float multiplier, float duration, SlowSource source);

        /// <summary>duration초 동안 멈춘다(경직·마비). 이미 멈춰 있으면 더 긴 쪽 유지.</summary>
        void ApplyStun(float duration);
    }
}
