using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Abilities
{
    /// <summary>
    /// 메인 능력(무기×속성) 축의 최소 골격. GDD 설계: 보스를 처치할 때만 속성이 바뀐다 — 아직 속성이
    /// 없으면 새로 흡수하고, 이미 있으면 다른 속성으로 교체한다(원하면 유지도 가능 — 그 경우 그냥
    /// ApplyElement를 호출하지 않으면 된다). 융합이나 같은 속성 중첩 강화는 없다(GDD v1.0에서 폐기).
    /// 무기 자체는 런 시작 시 고정되므로(지금은 대검 하나뿐) 여기서 다루지 않는다.
    ///
    /// 지금 단계에서는 "보스를 처치하면 속성 선택지를 준다"는 실제 트리거가 아직 없다(보스 자체가
    /// 없음). 그래서 이 컴포넌트는 데이터 모델 + 대미지 반영 + 디버그용 테스트 훅까지만 담당하고,
    /// 실제 보스 처치 시 ApplyElement를 호출하는 연결은 보스 시스템을 만들 때 붙인다.
    /// </summary>
    public class MainAbilityController : NetworkBehaviour
    {
        [Header("속성 보유 시 고정 대미지 보너스")]
        [SerializeField] private int elementDamageBonus = 3; // [PLAYTEST] GDD: 속성 = 속성 대미지(이 고정 보너스) + 부가 효과. 부가 효과는 StatusEffectReceiver가 명중마다 처리
                                                             // 2026-09-23 플레이 피드백(결정 ⑤A)으로 8→3

        // 서버가 바꾸고 모든 클라이언트가 읽는다(2인 검증 준비 — 원격 본인 HUD·보스 선택 패널·용 브레스 색이 같은 값을 보게).
        [SyncVar] private ElementType currentElement = ElementType.None;

        public ElementType CurrentElement
        {
            get => currentElement;
            private set => currentElement = value;
        }

        /// <summary>
        /// 속성이 바뀌어 대미지 보너스가 정해졌을 때(서버 전용 — ApplyElement의 isServer 가드 안에서만 발생). 인자 = 고정 보너스(속성 없음 = 0).
        /// PlayerController가 Awake에서 구독해 PlayerStats에 넣는다(리팩터 B안 단계 3 — 옛 PlayerController.ApplyMainAbilityDamageBonus 직접 호출).
        /// </summary>
        public event System.Action<int> ServerElementDamageBonusChanged;

        /// <summary>
        /// 보스를 처치하고 속성을 얻을 때 호출한다. 속성이 없으면 그대로 흡수하고, 이미 있으면 새
        /// 속성으로 교체한다(융합/중첩 없음 — 항상 단일 속성만 유지).
        /// </summary>
        public void ApplyElement(ElementType newElement)
        {
            if (!isServer)
            {
                return; // 메인 능력 변경도 서버(호스트) 권위 (ADR-003)
            }

            ElementType previous = CurrentElement;
            CurrentElement = newElement;
            Debug.Log($"[MainAbility] {name} 속성 {(previous == ElementType.None ? "흡수" : "교체")}: {previous.DisplayName()} -> {CurrentElement.DisplayName()}");

            ApplyDamageBonus();
        }

        private void ApplyDamageBonus()
        {
            int bonus = CurrentElement != ElementType.None ? elementDamageBonus : 0;

            ServerElementDamageBonusChanged?.Invoke(bonus);
        }

        [ContextMenu("Debug: Apply Fire")]
        private void DebugApplyFire() => ApplyElement(ElementType.Fire);

        [ContextMenu("Debug: Apply Ice")]
        private void DebugApplyIce() => ApplyElement(ElementType.Ice);

        // QA용: 보스를 잡지 않고도 인스펙터 우클릭 메뉴로 5속성을 전부 시험할 수 있게 한다(플레이 중, 호스트).
        [ContextMenu("Debug: Apply Electric")]
        private void DebugApplyElectric() => ApplyElement(ElementType.Electric);

        [ContextMenu("Debug: Apply Poison")]
        private void DebugApplyPoison() => ApplyElement(ElementType.Poison);

        [ContextMenu("Debug: Apply Earth")]
        private void DebugApplyEarth() => ApplyElement(ElementType.Earth);

        [ContextMenu("Debug: Clear Element (None)")]
        private void DebugClearElement() => ApplyElement(ElementType.None);
    }
}
