using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>흡수 홀드 바람 줄기 하나 — 플레이어 기준 앞쪽에서 몸 쪽으로 0.22초 동안 날아오며 사라진다(SlimeBodyAnimator).</summary>
    public class AbsorbStreak : MonoBehaviour
    {
        private const float Life = 0.22f;
        private Transform _target;
        private Vector2 _offset;
        private float _elapsed;

        public void Init(Sprite sprite, Transform target, Vector2 startOffset, int sortingOrder)
        {
            _target = target;
            _offset = startOffset;
            var r = gameObject.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = sortingOrder;
            // 그림의 머리(오른쪽 끝)가 몸 쪽을 향하도록: 이동 방향 = -startOffset
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-startOffset.y, -startOffset.x) * Mathf.Rad2Deg);
            Place(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= Life || _target == null)
            {
                Destroy(gameObject);
                return;
            }

            Place(_elapsed / Life);
        }

        private void Place(float t)
        {
            if (_target != null)
            {
                transform.position = (Vector2)_target.position + _offset * (1f - t * t);
            }
        }
    }
}
