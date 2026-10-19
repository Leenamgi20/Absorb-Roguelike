using AbsorbRoguelike.Localization;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Absorption
{
    /// <summary>
    /// 피격으로 벗겨진 패시브 특성(GDD "피격 시 패시브 특성(흡수체) 방출 시스템"). 플레이어 근처 바닥에 떨어져
    /// 흡수(E)로 다시 먹을 수 있고, lifetime 동안 점점 빠르게 깜빡이다가 사라진다. 커비처럼 튕겨 다니지 않는다.
    /// 누가 먼저 먹는지·소멸은 서버 권위. 모양은 캐릭터(16px)의 1/4 면적인 8×8px 테두리 블럭(아트 전 임시).
    /// </summary>
    public class DroppedTrait : NetworkBehaviour
    {
        [SerializeField] private float lifetime = 5f; // [PLAYTEST]
        [Tooltip("깜빡임 속도(초당 횟수) — 떨어진 직후 → 사라지기 직전.")]
        [SerializeField] private float startBlinkRate = 1.5f;
        [SerializeField] private float endBlinkRate = 10f;

        [SyncVar(hook = nameof(OnColorChanged))] private Color outlineColor = Color.white;

        private PassiveTraitData _trait;
        private bool _taken;
        private float _age;
        private float _blinkPhase;
        private SpriteRenderer _renderer;

        private static Sprite _outlineSprite;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = OutlineSprite();
            _renderer.color = outlineColor;
        }

        /// <summary>스폰 전에 서버에서 호출한다.</summary>
        public void Initialize(PassiveTraitData trait)
        {
            _trait = trait;
            outlineColor = ColorFor(trait != null ? trait.Skill : AbsorbSkill.None);
            _renderer.color = outlineColor;
        }

        private void OnColorChanged(Color _, Color value)
        {
            _renderer.color = value;
        }

        private void Update()
        {
            _age += Time.deltaTime;

            float t = Mathf.Clamp01(_age / lifetime);
            _blinkPhase += Mathf.Lerp(startBlinkRate, endBlinkRate, t) * Time.deltaTime;
            _renderer.enabled = Mathf.Repeat(_blinkPhase, 1f) < 0.6f;

            if (isServer && _age >= lifetime)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        /// <summary>흡수로 되찾는다. 먼저 가져간 한 명만 성공한다(다른 파티원이 주워가는 것도 허용 — GDD).</summary>
        public bool TryTake(out PassiveTraitData trait)
        {
            trait = _trait;
            if (!isServer || _taken)
            {
                return false;
            }

            _taken = true;
            NetworkServer.Destroy(gameObject);
            return true;
        }

        /// <summary>동물 짧은 이름(파티 UI 아이콘 가운데 글자 — 한국어 한 글자, 영어 두 글자). 없음은 '-'. 표 키 trait.&lt;스킬&gt;.short.</summary>
        public static string ShortNameFor(AbsorbSkill skill)
        {
            return skill == AbsorbSkill.None ? "-" : Loc.Get($"trait.{skill}.short");
        }

        /// <summary>동물별 표시 색(떨어진 패시브 테두리·파티 UI 아이콘 공용). 없음은 흰색.</summary>
        public static Color ColorFor(AbsorbSkill skill)
        {
            switch (skill)
            {
                case AbsorbSkill.TigerCombo: return new Color(1f, 0.6f, 0.15f);
                case AbsorbSkill.SnakeBind: return new Color(0.4f, 1f, 0.4f);
                case AbsorbSkill.OxKnockback: return new Color(0.85f, 0.6f, 0.4f);
                case AbsorbSkill.HorseDoubleDash: return new Color(0.75f, 0.45f, 0.2f);
                case AbsorbSkill.PigCounter: return new Color(1f, 0.6f, 0.75f);
                case AbsorbSkill.RoosterEgg: return new Color(1f, 0.92f, 0.55f); // 힐러(알 낳기) 성격에 맞춘 크림/노랑
                case AbsorbSkill.RabbitEarWhip: return new Color(0.9f, 0.9f, 1f);
                case AbsorbSkill.RatGnaw: return new Color(0.6f, 0.6f, 0.65f);     // 회색
                case AbsorbSkill.DragonBreath: return new Color(0.3f, 0.75f, 0.65f); // 청록
                case AbsorbSkill.SheepWool: return new Color(0.97f, 0.95f, 0.85f);  // 양털 아이보리
                case AbsorbSkill.MonkeyBanana: return new Color(0.95f, 0.85f, 0.2f); // 바나나 노랑
                case AbsorbSkill.DogCompanion: return new Color(0.6f, 0.4f, 0.25f);  // 갈색
                default: return Color.white;
            }
        }

        private static Sprite OutlineSprite()
        {
            if (_outlineSprite != null)
            {
                return _outlineSprite;
            }

            const int size = 8;
            var tex = new Texture2D(size, size) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    tex.SetPixel(x, y, edge ? Color.white : Color.clear);
                }
            }

            tex.Apply();
            _outlineSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            return _outlineSprite;
        }
    }
}
