using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 패턴이 남기는 파편(GDD "보스 대미지 규칙 — 파편 흡수·뱉기", 커비의 별 오마주). 흡수(E)로 머금고
    /// 공격 버튼으로 뱉으면 보스에게 큰 대미지를 준다. 일정 시간이 지나면 사라진다. 판정/소멸은 서버 권위.
    /// </summary>
    public class BossDebris : NetworkBehaviour
    {
        [SerializeField] private float lifetime = 8f; // [PLAYTEST]

        private float _age;
        private bool _taken;

        private void Update()
        {
            if (!isServer)
            {
                return;
            }

            _age += Time.deltaTime;
            if (_age >= lifetime)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        /// <summary>흡수로 가져간다. 먼저 가져간 한 명만 성공한다.</summary>
        public bool TryTake()
        {
            if (!isServer || _taken)
            {
                return false;
            }

            _taken = true;
            NetworkServer.Destroy(gameObject);
            return true;
        }
    }
}
