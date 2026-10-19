using AbsorbRoguelike.Abilities;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 무기 속성 오라(2026-10-07, 사용자 결정 A — 동물은 몸(탈)이, 속성은 무기가 보여 준다). 지금 든 속성(MainAbilityController.CurrentElement,
    /// 모든 화면 동기화)에 따라:
    /// - 칼 뒤에 1px 빛 테두리(`Fx/weapon_sword_glow`)를 속성 색으로 깔고 천천히 숨쉬듯 밝기를 바꾼다.
    /// - 칼날 면(`Fx/weapon_sword_blade`)에 반투명 속성 색을 덧칠한다(단색 셰이더 `Fx/SpriteSolidColor`).
    /// - 칼날 여기저기서 속성 조각(`Fx/mote_*` — 불씨·눈송이·스파크·거품·돌가루)이 피어올라 사라진다.
    /// - 베기 이펙트(PlayerController가 만드는 자식 "SlashFx")를 속성 색으로 물들인다.
    /// 속성이 없으면 전부 끈다. 휘두르는 동안 무기 그림이 숨겨지면 테두리·덧칠도 같이 숨는다. 순수 표시 전용.
    /// 그림: `Art_Source/fx/draw_weapon_aura.py`.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class WeaponElementAura : MonoBehaviour
    {
        [SerializeField] private float moteInterval = 0.16f;
        [SerializeField] private float slashTintAmount = 0.5f;

        private MainAbilityController _ability;
        private SpriteRenderer _weapon;
        private SpriteRenderer _glow;
        private SpriteRenderer _blade;
        private SpriteRenderer _slash;
        private float _moteTimer;
        private float _seed;

        private void Start()
        {
            _ability = GetComponent<MainAbilityController>();
            var controller = GetComponent<PlayerController>();
            Transform weapon = controller != null ? controller.WeaponTransform : null;
            _weapon = weapon != null ? weapon.GetComponent<SpriteRenderer>() : null;
            if (_weapon == null)
            {
                enabled = false;
                return;
            }

            _seed = Random.value * 10f;
            _glow = CreateLayer(weapon, "WeaponGlow", Resources.Load<Sprite>("Fx/weapon_sword_glow"), null);
            UI.VisualFx.ApplyGlow(_glow, UI.GlowKind.WeaponAura); // 오라 테두리 발광(블룸). 덧칠 레이어(_blade)는 단색 셰이더 그대로
            var shader = Resources.Load<Shader>("Fx/SpriteSolidColor");
            _blade = CreateLayer(weapon, "WeaponBladeTint", Resources.Load<Sprite>("Fx/weapon_sword_blade"),
                shader != null ? new Material(shader) : null);
        }

        private static SpriteRenderer CreateLayer(Transform parent, string name, Sprite sprite, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            if (material != null)
            {
                r.sharedMaterial = material;
            }

            r.enabled = false;
            return r;
        }

        private void LateUpdate()
        {
            ElementType element = _ability != null ? _ability.CurrentElement : ElementType.None;
            bool on = element != ElementType.None;
            Color c = on ? element.StatusTintColor() : Color.white;
            bool weaponShown = _weapon.enabled;

            float pulse = 0.55f + 0.3f * Mathf.Sin((Time.time + _seed) * 4f);
            SyncLayer(_glow, on && weaponShown, new Color(c.r, c.g, c.b, pulse), -1);
            SyncLayer(_blade, on && weaponShown, new Color(c.r, c.g, c.b, 0.35f), 1);

            // 베기 이펙트 물들이기(없으면 찾아 둔다 — 첫 스윙 때 생긴다)
            if (_slash == null)
            {
                Transform t = transform.Find("SlashFx");
                _slash = t != null ? t.GetComponent<SpriteRenderer>() : null;
            }

            if (_slash != null)
            {
                _slash.color = on ? Color.Lerp(Color.white, c, slashTintAmount) : Color.white;
            }

            if (!on)
            {
                return;
            }

            _moteTimer -= Time.deltaTime;
            if (_moteTimer <= 0f)
            {
                _moteTimer = moteInterval * Random.Range(0.7f, 1.3f);
                SpawnMote(element);
            }
        }

        private void SyncLayer(SpriteRenderer r, bool show, Color color, int orderOffset)
        {
            if (r == null || r.sprite == null)
            {
                return;
            }

            r.enabled = show;
            if (!show)
            {
                return;
            }

            r.color = color;
            r.sortingLayerID = _weapon.sortingLayerID;
            r.sortingOrder = _weapon.sortingOrder + orderOffset;
            r.flipX = _weapon.flipX;
            r.flipY = _weapon.flipY;
        }

        // 칼날 어딘가(휘두르는 중이면 몸 근처)에서 조각 하나가 피어오른다.
        private void SpawnMote(ElementType element)
        {
            Sprite sprite = Resources.Load<Sprite>("Fx/mote_" + MoteName(element));
            if (sprite == null)
            {
                return;
            }

            Vector3 pos = _weapon.enabled
                ? _weapon.transform.TransformPoint(new Vector3(Random.Range(-2f, 2f) / 32f, Random.Range(0.25f, 1.1f), 0f))
                : transform.position + (Vector3)(Random.insideUnitCircle * 0.6f);
            var go = new GameObject("WeaponMote");
            go.transform.position = pos;
            go.AddComponent<WeaponMote>().Init(sprite, _weapon.sortingOrder + 2);
            UI.VisualFx.ApplyGlow(go.GetComponent<SpriteRenderer>(), UI.GlowKind.WeaponAura); // 속성 조각 발광
        }

        private static string MoteName(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return "fire";
                case ElementType.Ice: return "ice";
                case ElementType.Electric: return "electric";
                case ElementType.Poison: return "poison";
                default: return "earth";
            }
        }
    }
}
