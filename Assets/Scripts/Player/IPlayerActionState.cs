using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 흡수(AbsorptionController)가 읽는 플레이어 조작 상태(리팩터 B안 단계 3, 2026-10-08). AbsorptionController가
    /// PlayerController 타입을 몰라도 되게 한다(AC ↔ PC 양방향 의존 제거). 지금 구현체는 PlayerController 하나.
    /// </summary>
    public interface IPlayerActionState
    {
        /// <summary>사망·쓰러짐·부활 진행 중이라 조작(공격·흡수 등)을 막아야 하는가.</summary>
        bool IsActionLocked { get; }

        /// <summary>바라보는 방향(마우스 조준).</summary>
        Vector2 FacingDirection { get; }
    }
}
