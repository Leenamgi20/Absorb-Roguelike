using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 몬스터 그림 좌우 뒤집기(2026-10-06, 실제 아트 적용). 아트는 오른쪽을 보도록 1방향만 그리므로, 각 화면에서 위치 변화로
    /// 왼쪽으로 움직이면 flipX. 위치만 보므로 서버·클라이언트 모두 같은 결과(네트워크 동기화 불필요). 정예 외곽선
    /// (EliteMonsterMarker)은 몸통의 flipX를 그대로 따라간다. 거의 멈춰 있을 때는 마지막 방향을 유지한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class MonsterFacing : MonoBehaviour
    {
        [SerializeField] private float minDeltaX = 0.004f;
        private SpriteRenderer _renderer;
        private float _lastX;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _lastX = transform.position.x;
        }

        private void LateUpdate()
        {
            float x = transform.position.x;
            float dx = x - _lastX;
            if (Mathf.Abs(dx) >= minDeltaX)
            {
                _renderer.flipX = dx < 0f;
                _lastX = x;
            }
        }
    }
}
