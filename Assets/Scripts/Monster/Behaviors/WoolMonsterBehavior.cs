using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster.Behaviors
{
    /// <summary>
    /// 털 쌓기 → 털 폭발(잡몹 패턴 2차, 2026-10-02 — 양). 공격자 위치가 있는 피격(MonsterStub.TakeDamage(amount, attackerPosition) →
    /// ModifyIncomingDamage, 도트·방전처럼 위치 없는 대미지는 제외)마다 털 +1(최대 woolMax, 대미지는 그대로). 털이 가득 차면 진행 중이던
    /// 공격은 끊지 않고, 다음 공격을 기본 근접 대신 털 폭발로 한다: 몸 중심 원형 예고(반경 burstRadius, burstWindupDuration, 진하기 규칙 ㊶)
    /// → 원 안 플레이어 피해 → 털 0. 털이 덜 찼으면 기본 근접 그대로.
    /// 동기화: wool(byte)만 새로 둔다. 털 폭발 예고를 시작하면 서버가 wool을 woolMax+1로 올려 "폭발 예고 중" 표시로 쓴다(털 수 표시는
    /// woolMax로 자름) — 가득 찬 순간 진행 중이던 기본 공격 예고는 그대로 직사각형으로 보인다. 머리 위 'N/3' 표시는 각 화면이 로컬 TextMesh로 만들어 wool 값만 반영, 사망 시 숨김. 수치 전부 [PLAYTEST].
    /// </summary>
    public class WoolMonsterBehavior : MonsterBehaviorController
    {
        // 리팩터 C4: 고유 게임플레이 수치는 WoolMonsterConfig 에셋(Assets/Data/Monsters/<동물>.asset), 공통 수치는 Stats(MonsterStatsConfig).
        private WoolMonsterConfig Cfg => Config<WoolMonsterConfig>();

        protected override System.Type ConfigType => typeof(WoolMonsterConfig);

        [Header("털 표시")]
        [SerializeField] private Vector2 woolLabelOffset = new Vector2(0f, 1.05f);
        [SerializeField] private Color woolLabelColor = new Color(1f, 1f, 0.85f, 1f);
        [SerializeField] private Color woolFullColor = new Color(1f, 0.55f, 0.3f, 1f);

        private const float PlayerHalfSize = 0.5f; // 보스 원형 충격파 판정과 같은 여유

        [SyncVar] private byte wool;

        private bool _burstThisAttack;
        private GameObject _circle;
        private SpriteRenderer _circleRenderer;
        private TextMesh _label;
        private int _shownWool = -1;

        private bool IsFull => wool >= Cfg.woolMax;
        private bool IsBursting => wool > Cfg.woolMax; // 털 폭발 예고 중(서버가 woolMax+1로 표시)

        protected override void Awake()
        {
            base.Awake();
            _circle = TelegraphUtil.CreateCircle(transform, "WoolBurstTelegraph", telegraphColor, GetComponent<SpriteRenderer>());
            _circleRenderer = _circle.GetComponent<SpriteRenderer>();
        }

        public override int ModifyIncomingDamage(Vector2 attackerPosition, int damage)
        {
            if (isServer && !MonsterStub.IsDead && damage > 0 && wool < Cfg.woolMax)
            {
                wool++;
            }

            return damage;
        }

        protected override State DecideState(Transform target)
        {
            if (target != null && IsFull)
            {
                return Vector2.Distance(transform.position, target.position) <= Cfg.burstTriggerRange ? State.Attack : State.Chase;
            }

            return base.DecideState(target);
        }

        protected override void OnAttackStarted(Transform target)
        {
            _burstThisAttack = IsFull;
            if (_burstThisAttack)
            {
                wool = (byte)(Cfg.woolMax + 1);
            }
        }

        protected override float FirstWindupDuration => _burstThisAttack ? Cfg.burstWindupDuration : Stats.windupDuration;

        // 털 폭발 예고 중에는 기본 직사각형을 크기 0으로 숨기고(색은 기본 규칙이 그대로 칠함), 원은 UpdateClientVisuals가 그린다.
        protected override void PlaceTelegraphVisual(GameObject telegraph, Vector2 direction)
        {
            if (IsBursting)
            {
                telegraph.transform.localScale = Vector3.zero;
                return;
            }

            base.PlaceTelegraphVisual(telegraph, direction);
        }

        protected override void OnWindupFinished(Transform target)
        {
            if (!_burstThisAttack)
            {
                base.OnWindupFinished(target);
                return;
            }

            _burstThisAttack = false;
            wool = 0;
            Vector2 origin = transform.position;
            foreach (PlayerHealth health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.IsDead)
                {
                    continue; // 쓰러진 플레이어는 판정 대상에서 뺀다(대미지는 PlayerHealth가 막지만 명시)
                }

                if (Vector2.Distance(origin, health.transform.position) <= Cfg.burstRadius + PlayerHalfSize)
                {
                    DealDamage(health, attackDamage);
                }
            }

            Debug.Log($"[WoolMonster] {name} wool burst ({attackDamage})");
        }

        protected override void UpdateClientVisuals()
        {
            bool alive = !MonsterStub.IsDead && !MonsterStub.IsDefeated;

            bool showCircle = alive && IsWindingUp && IsBursting;
            _circle.SetActive(showCircle);
            if (showCircle)
            {
                Vector3 parentScale = transform.lossyScale;
                _circle.transform.localPosition = Vector3.zero;
                _circle.transform.localScale = new Vector3(Cfg.burstRadius / parentScale.x, Cfg.burstRadius / parentScale.y, 1f);
                TelegraphUtil.SetProgress(_circleRenderer, WindupProgress, telegraphColor, WindupLength);
            }

            UpdateWoolLabel(alive);
        }

        private void UpdateWoolLabel(bool alive)
        {
            if (_label == null)
            {
                if (!alive)
                {
                    return;
                }

                var go = new GameObject("WoolLabel");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = woolLabelOffset;
                _label = go.AddComponent<TextMesh>();
                _label.font = AbsorbRoguelike.UI.DamagePopup.BuiltinFont;
                _label.fontSize = 48;
                _label.characterSize = 0.06f;
                _label.anchor = TextAnchor.MiddleCenter;
                _label.alignment = TextAlignment.Center;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _label.font.material;
                var body = GetComponent<SpriteRenderer>();
                if (body != null)
                {
                    mr.sortingLayerID = body.sortingLayerID;
                    mr.sortingOrder = body.sortingOrder + 5;
                }
            }

            _label.gameObject.SetActive(alive);
            int shown = Mathf.Min(wool, Cfg.woolMax);
            if (alive && _shownWool != shown)
            {
                _shownWool = shown;
                _label.text = $"{shown}/{Cfg.woolMax}";
                _label.color = IsFull ? woolFullColor : woolLabelColor;
            }
        }
    }
}
