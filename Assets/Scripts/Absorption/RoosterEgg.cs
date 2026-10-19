using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Absorption
{
    /// <summary>
    /// 닭 흡수 스킬(2026-09-28 개편, 유저 플레이 피드백): 대시할 때마다 대시 시작 위치에 낳는 알. 낳은 본인·아군이
    /// 닿으면 체력을 RoosterSkillConfig.eggHealAmount만큼 회복하고 사라진다(힐러 역할). 본인도 먹을 수 있다(결정 ⑰B — 솔로에서도 쓰이게,
    /// 대신 회복량을 낮춤). 낳은 직후 발밑 알은 ownerPickupDelay 동안 본인에게 반응하지 않는다.
    /// 판정·소멸은 서버 권위, 깜빡임은 클라이언트별 연출. 연결 소유 없이 서버가 스폰하므로 층 이동 정리
    /// (FloorProgression.AdvanceFloor)와 다시 시작(ServerChangeScene)에서 같이 치워진다.
    /// 외형은 아트 전 임시 — 코드로 만든 작은 크림색 타원.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class RoosterEgg : NetworkBehaviour
    {
        [SerializeField] private float lifetime = 10f; // [PLAYTEST] 알 수명(초)
        [SerializeField] private float blinkDuration = 3f; // [PLAYTEST] 사라지기 전 깜빡이는 시간(초)
        [SerializeField] private float blinkRate = 6f; // [PLAYTEST] 깜빡임 속도(초당 횟수)
        [Tooltip("낳은 직후 발밑 알을 바로 먹지 않도록 이 시간 동안은 낳은 본인에게 반응하지 않는다.")]
        [SerializeField] private float ownerPickupDelay = 0.5f; // [PLAYTEST]

        [SyncVar] public uint ownerNetId;

        private SpriteRenderer _renderer;
        private Collider2D _collider;
        private readonly Collider2D[] _overlapBuffer = new Collider2D[8];
        private static readonly ContactFilter2D OverlapFilter = new ContactFilter2D { useTriggers = false };
        private float _spawnTime;
        private bool _taken;
        private int _healAmount; // 서버: RoosterSkillConfig.eggHealAmount(2026-10-14 프리팹에서 옮김, SetHealAmount로 받음)

        /// <summary>서버: 낳은 쪽(RoosterEggSkill)이 NetworkServer.Spawn 전에 회복량을 넘긴다.</summary>
        public void SetHealAmount(int amount) => _healAmount = amount;

        private static Sprite _eggSprite;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = EggSprite();
            _renderer.sortingOrder = 6;
            _collider = GetComponent<Collider2D>();
            _spawnTime = Time.time;
        }

        private void Update()
        {
            float age = Time.time - _spawnTime;

            // 마지막 blinkDuration 동안 클라이언트별로 알파 토글(스폰 시각 기준).
            Color color = _renderer.color;
            color.a = age >= lifetime - blinkDuration && Mathf.Repeat(age * blinkRate, 1f) >= 0.5f ? 0.25f : 1f;
            _renderer.color = color;

            if (isServer && age >= lifetime)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            if (isServer)
            {
                CheckOverlapServer();
            }
        }

        [ServerCallback]
        private void OnTriggerEnter2D(Collider2D other)
        {
            TryPickup(other);
        }

        // 본인 발밑 대기시간이 끝난 경우, 겹친 채 맞아서 체력이 깎인 경우처럼 이미 겹친 채 조건이 풀리는 경우를 위해
        // 서버가 매 프레임 겹침을 직접 확인한다. OnTriggerStay2D는 멈춰 선 플레이어 Rigidbody2D가 잠들면 안 불려서 쓰지 않는다.
        private void CheckOverlapServer()
        {
            if (_collider == null)
            {
                return;
            }

            int count = _collider.Overlap(OverlapFilter, _overlapBuffer);
            for (int i = 0; i < count && !_taken; i++)
            {
                TryPickup(_overlapBuffer[i]);
            }
        }

        private void TryPickup(Collider2D other)
        {
            if (_taken)
            {
                return;
            }

            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (health == null || health.IsDead || health.CurrentHealth >= health.MaxHealth)
            {
                return; // 체력이 가득 찬 아군은 알을 낭비하지 않고 지나간다
            }

            bool isOwner = health.netId == ownerNetId;
            if (isOwner && Time.time - _spawnTime < ownerPickupDelay)
            {
                return;
            }

            _taken = true;
            health.Heal(_healAmount);
            NetworkServer.Destroy(gameObject);
        }

        private static Sprite EggSprite()
        {
            if (_eggSprite != null)
            {
                return _eggSprite;
            }

            const int width = 6, height = 8;
            var tex = new Texture2D(width, height) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var cream = new Color(1f, 0.97f, 0.85f);
            var yolk = new Color(1f, 0.85f, 0.35f);
            float cx = (width - 1) * 0.5f, cy = (height - 1) * 0.5f;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float dx = (x - cx) / (width * 0.5f), dy = (y - cy) / (height * 0.5f);
                    float d = dx * dx + dy * dy;
                    tex.SetPixel(x, y, d > 1f ? Color.clear : d > 0.55f ? yolk : cream); // 노란 테두리의 흰 타원
                }
            }

            tex.Apply();
            _eggSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 16f);
            return _eggSprite;
        }
    }
}
