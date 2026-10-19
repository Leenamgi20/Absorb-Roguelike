using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 시각 전용 스프라이트 프레임 반복(2026-10-06, 보스 방 화로 불꽃). 로컬 시간으로만 돌아 네트워크 동기화는 하지 않는다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteFlipbook : MonoBehaviour
    {
        private Sprite[] _frames;
        private float _frameTime = 0.25f;
        private int _offset;
        private SpriteRenderer _renderer;

        public void Init(Sprite[] frames, float frameTime, int offset)
        {
            _frames = frames;
            _frameTime = Mathf.Max(0.05f, frameTime);
            _offset = offset;
            _renderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            if (_frames == null || _frames.Length == 0 || _renderer == null)
            {
                return;
            }

            int index = ((int)(Time.time / _frameTime) + _offset) % _frames.Length;
            _renderer.sprite = _frames[index];
        }
    }
}
