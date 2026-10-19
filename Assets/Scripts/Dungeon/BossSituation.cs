using AbsorbRoguelike.Absorption;
using AbsorbRoguelike.Localization;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 층마다 무작위로 붙는 보스 상황 특성(GDD v2.20~2.21 ⑮A·⑯A '상황 요구 1차'). 특정 동물·속성이 유리해지는 상황을 만들어
    /// "이 층 동물 풀로 이 보스를 어떻게 공략할까"를 고민하게 한다. FloorProgression이 층 생성 때 서버에서 뽑는다.
    /// - 갑옷: 갑옷이 남아 있는 동안 받는 대미지 감소. 소 돌진이 갑옷을 크게 깎는다(BossStub).
    /// - 연타 휘두르기: 뿔 휘두르기가 여러 번 이어진다(BossPatternSwing). 돼지 반격 쿨이 짧아진다(PlayerController).
    /// - 재생: 초당 체력 회복. 화상 스택이 있으면 멈춘다(BossStub).
    /// 값은 SyncVar로 직렬화되므로 순서를 바꾸지 않는다 — 새 특성은 항상 끝에 추가한다.
    /// </summary>
    public enum BossSituation : byte
    {
        None = 0,
        Armored = 1,
        FlurrySwing = 2,
        Regenerating = 3,
    }

    /// <summary>배너·HUD·디버그 메뉴가 공통으로 쓰는 표시용 헬퍼(ElementTypeExtensions와 같은 형태).</summary>
    public static class BossSituationExtensions
    {
        /// <summary>None을 뺀 특성 3종. 층 추첨에 쓴다.</summary>
        public static readonly BossSituation[] AllSituations =
        {
            BossSituation.Armored, BossSituation.FlurrySwing, BossSituation.Regenerating
        };

        // 표시 글자는 표 키 bosssit.<enum 이름>.name / .desc / .favored(2026-10-06 언어 설정). 값은 SyncVar로 오고 각 클라이언트가 자기 언어로 표시.
        public static string DisplayName(this BossSituation situation)
        {
            return Loc.Get($"bosssit.{situation}.name");
        }

        /// <summary>배너에 붙는 한 줄 공략 힌트. None은 빈 문자열.</summary>
        public static string HintText(this BossSituation situation)
        {
            return situation == BossSituation.None ? "" : Loc.Get($"bosssit.{situation}.desc");
        }

        /// <summary>층 동물 풀 보장(결정 ⑳B)에 쓰는 유리한 동물 — 그 동물 잡몹 특성의 흡수 스킬로 찾는다(FloorProgression).
        /// 동물이 아니라 속성이 유리한 특성(재생 → 화염)은 None — 속성은 보스 선택지가 따로 준다.
        /// (2026-10-06: 예전엔 AnimalEntry.displayName 한국어 이름으로 맞췄으나, 표시 이름과 판정을 떼려고 스킬로 바꿈 — 결과는 같다.)</summary>
        public static AbsorbSkill FavoredAnimalSkill(this BossSituation situation)
        {
            switch (situation)
            {
                case BossSituation.Armored: return AbsorbSkill.OxKnockback;
                case BossSituation.FlurrySwing: return AbsorbSkill.PigCounter;
                default: return AbsorbSkill.None;
            }
        }

        /// <summary>이 특성에서 유리한 동물/속성(표시용). None은 빈 문자열.</summary>
        public static string FavoredText(this BossSituation situation)
        {
            return situation == BossSituation.None ? "" : Loc.Get($"bosssit.{situation}.favored");
        }
    }
}
