using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>무기 속성 조각 하나 — 0.6초 동안 살짝 흔들리며 위로 떠오르고 마지막에 흐려진다(WeaponElementAura).</summary>
    public class WeaponMote : MonoBehaviour
    {
        private const float Life = 0.6f;
        private const float Rise = 0.4f;
        private SpriteRenderer _renderer;
        private Vector3 _start;
        private float _elapsed;
        private float _sway;

        public void Init(Sprite sprite, int sortingOrder)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.sortingOrder = sortingOrder;
            _start = transform.position;
            _sway = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / Life;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = _start + new Vector3(Mathf.Sin(_sway + t * 6f) * 0.05f, Rise * t, 0f);
            _renderer.color = new Color(1f, 1f, 1f, t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f);
        }
    }
}
