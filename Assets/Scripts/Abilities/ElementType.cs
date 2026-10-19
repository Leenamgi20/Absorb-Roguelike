using AbsorbRoguelike.Localization;
using UnityEngine;

namespace AbsorbRoguelike.Abilities
{
    /// <summary>
    /// 메인 능력(무기×속성)의 속성 축. GDD 5속성(화염/냉기/전기/독/대지)과 스테이지 5개가 1:1로 대응한다.
    /// 부가 효과(화상·둔화·연쇄·중독·경직)는 StatusEffectReceiver가 명중마다 처리한다 — 무기는 항상 단일
    /// 속성만 유지하며(보스 처치 시에만 흡수/교체), 융합이나 중첩 강화는 없다(GDD v1.0에서 폐기).
    /// 값은 직렬화(인스펙터·프리팹)에 쓰이므로 순서를 바꾸지 않는다 — 새 속성은 항상 끝에 추가한다.
    /// </summary>
    public enum ElementType
    {
        None = 0,
        Fire = 1,
        Ice = 2,
        Electric = 3,
        Poison = 4,
        Earth = 5,
    }

    /// <summary>HUD·보스 선택 UI·승리 화면·상태이상 오버레이가 공통으로 쓰는 표시용 헬퍼.</summary>
    public static class ElementTypeExtensions
    {
        /// <summary>None을 뺀 GDD 5속성. 층 속성 배정·보스 ② 선택지 추첨에 쓴다.</summary>
        public static readonly ElementType[] AllElements =
        {
            ElementType.Fire, ElementType.Ice, ElementType.Electric, ElementType.Poison, ElementType.Earth
        };

        /// <summary>표시 이름(현재 언어) — 표 키 element.&lt;enum 이름&gt;(예: element.Electric = 전기 / Lightning).</summary>
        public static string DisplayName(this ElementType element)
        {
            return Loc.Get(LocKey(element));
        }

        /// <summary>표 키. 네트워크로 보낼 문구(Loc.Pack)에 Loc.KeyArg(LocKey)로 넣으면 받는 쪽 언어로 표시된다.</summary>
        public static string LocKey(this ElementType element)
        {
            return "element." + element;
        }

        /// <summary>상태이상 오버레이 색(알파는 오버레이 쪽에서 따로 정한다). None은 투명.</summary>
        public static Color StatusTintColor(this ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color(1f, 0.5f, 0.1f);      // 주황
                case ElementType.Ice: return new Color(0.5f, 0.85f, 1f);      // 하늘
                case ElementType.Electric: return new Color(1f, 0.95f, 0.2f); // 노랑
                case ElementType.Poison: return new Color(0.65f, 0.3f, 0.9f); // 보라
                case ElementType.Earth: return new Color(0.6f, 0.4f, 0.2f);   // 갈색
                default: return Color.clear;
            }
        }
    }
}
