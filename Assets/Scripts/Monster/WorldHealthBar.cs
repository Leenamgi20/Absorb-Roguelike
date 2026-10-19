using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 몬스터/보스 머리 위 체력바. 같은 오브젝트의 MonsterStub 또는 BossStub에서 체력을 읽어 그린다.
    /// 체력이 가득 차 있으면 숨기고, 맞으면 표시, 죽거나 쓰러지면 숨긴다. 도트 규격(PPU 16)에 맞춰 높이 2px.
    /// 현재·최대 체력 모두 SyncVar라 클라이언트에서도 같은 값을 그린다(2026-09-30: 최대 체력 동기화 추가).
    /// </summary>
    public class WorldHealthBar : MonoBehaviour
    {
        private const float Pixel = 1f / 16f;

        [SerializeField] private float width = 1f;
        [SerializeField] private Color fillColor = new Color(0.9f, 0.15f, 0.15f);
        [SerializeField] private Color backColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        [SerializeField] private int sortingOrder = 15;

        private MonsterStub _monster;
        private BossStub _boss;
        private GameObject _root;
        private Transform _fill;
        private TextMesh _statsLabel; // 디버그 '몬스터 수치 표시'(F1) 전용 — 체력바(_root)와 따로 둬서 체력바 숨김 규칙과 무관
        private float _bodyBottom;

        private void Awake()
        {
            _monster = GetComponent<MonsterStub>();
            _boss = GetComponent<BossStub>();

            var body = GetComponent<SpriteRenderer>();
            float top = body != null && body.sprite != null ? body.sprite.bounds.max.y : 0.5f;
            _bodyBottom = body != null && body.sprite != null ? body.sprite.bounds.min.y : -0.5f;

            _root = new GameObject("HealthBar");
            _root.transform.SetParent(transform, false);
            _root.transform.localPosition = new Vector3(0f, top + 2f * Pixel, 0f);

            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
            MakeBar("Back", sprite, backColor, sortingOrder, new Vector3(width + 2f * Pixel, 4f * Pixel, 1f), -width * 0.5f - Pixel);
            _fill = MakeBar("Fill", sprite, fillColor, sortingOrder + 1, new Vector3(width, 2f * Pixel, 1f), -width * 0.5f).transform;
            _root.SetActive(false);
        }

        private GameObject MakeBar(string name, Sprite sprite, Color color, int order, Vector3 scale, float x)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        private void LateUpdate()
        {
            int current, max;
            bool dead;
            if (_monster != null)
            {
                current = _monster.CurrentHealth;
                max = _monster.MaxHealth;
                dead = _monster.IsDead;
            }
            else if (_boss != null)
            {
                current = _boss.CurrentHealth;
                max = _boss.MaxHealth;
                dead = _boss.IsDefeated;
            }
            else
            {
                return;
            }

            UpdateStatsLabel(current, max, dead);

            bool show = !dead && max > 0 && current < max;
            _root.SetActive(show);
            if (show)
            {
                _fill.localScale = new Vector3(width * current / max, _fill.localScale.y, 1f);
            }
        }

        // 디버그 '몬스터 수치 표시'(DebugMenuController.ShowMonsterStats, 2026-10-13): 켜져 있고 살아 있으면 발밑에 '현재/최대'.
        // 체력이 가득해 체력바가 숨겨진 상태에서도 최대 체력(층·인원 배율 적용 결과)을 볼 수 있게 체력바와 따로 켠다.
        // 머리 위(체력바·상태이상 스택 라벨·대미지 숫자 자리)와 겹치지 않게 몸 아래에 둔다.
        private void UpdateStatsLabel(int current, int max, bool dead)
        {
            bool showStats = AbsorbRoguelike.UI.DebugMenuController.ShowMonsterStats && !dead && max > 0;
            if (!showStats)
            {
                if (_statsLabel != null && _statsLabel.gameObject.activeSelf)
                {
                    _statsLabel.gameObject.SetActive(false);
                }

                return;
            }

            if (_statsLabel == null)
            {
                _statsLabel = AbsorbRoguelike.UI.DamagePopup.CreateText("DebugStatsLabel", transform, 28, sortingOrder + 2);
                _statsLabel.anchor = TextAnchor.UpperCenter;
                _statsLabel.transform.localPosition = new Vector3(0f, _bodyBottom - Pixel, 0f);
            }

            _statsLabel.gameObject.SetActive(true);
            _statsLabel.text = $"{current}/{max}";
        }
    }
}
