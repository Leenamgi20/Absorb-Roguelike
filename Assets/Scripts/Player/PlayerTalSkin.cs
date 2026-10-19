using AbsorbRoguelike.Absorption;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 탈 쓴 슬라임 그림(2026-10-06): 지금 흡수한 동물(AbsorptionController.SyncedSkill, 모든 화면에 동기화된 값)에 맞춰
    /// 몸 스프라이트를 그 동물 탈 그림으로 바꾸고, 특성이 없으면 기본 슬라임으로 되돌린다. 연출 전용이라 판정과 무관하다.
    /// 값은 매 프레임 읽기만 한다(SyncVar 훅을 하나 더 두지 않아 늦게 접속한 화면도 바로 맞는다). 가드 색 틴트는 그대로 위에 입혀진다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerTalSkin : MonoBehaviour
    {
        [SerializeField] private Sprite baseSprite;
        [Tooltip("AbsorbSkill 순서(정수값)와 같은 자리: 0 None(비움) 1 호랑이 2 뱀 3 소 4 말 5 돼지 6 닭 7 토끼 8 쥐 9 용 10 양 11 원숭이 12 개.")]
        [SerializeField] private Sprite[] skillSprites = new Sprite[13];

        private SpriteRenderer _renderer;
        private AbsorptionController _absorption;
        private AbsorbSkill _shown = (AbsorbSkill)(-1);

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _absorption = GetComponent<AbsorptionController>();
            if (baseSprite == null)
            {
                baseSprite = _renderer.sprite;
            }
        }

        private void LateUpdate()
        {
            AbsorbSkill skill = _absorption != null ? _absorption.SyncedSkill : AbsorbSkill.None;
            if (skill == _shown)
            {
                return;
            }

            _shown = skill;
            int index = (int)skill;
            Sprite tal = skillSprites != null && index > 0 && index < skillSprites.Length ? skillSprites[index] : null;
            _renderer.sprite = tal != null ? tal : baseSprite;
        }
    }
}
