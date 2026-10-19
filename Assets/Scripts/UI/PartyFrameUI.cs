using System.Collections.Generic;
using AbsorbRoguelike.Absorption;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 왼쪽 위 파티 UI(2026-09-28). 플레이어마다 한 줄: [동물 아이콘 24×24 — 동물 색 사각형 + 가운데 한 글자,
    /// 특성 없으면 회색 '-'] [이름 — 본인은 맨 위 + '(나)'] [체력 바] [HP 80/100]. 쓰러지면 이름 회색 + '쓰러짐'.
    /// 데이터는 전부 동기화 값만 읽는다: 번호·이름·색 PlayerIdentityInfo(SyncVar), 특성 AbsorptionController.SyncedSkill(SyncVar),
    /// 체력 PlayerHealth.CurrentHealth/MaxHealth(SyncVar) — 그래서 호스트·원격 클라이언트 모두 같은 내용을 본다.
    /// 플레이어 목록은 PlayerIdentityInfo.All(입장/퇴장 시 자동 반영). 게임 씬 밖(접속 전 메뉴·로비, 플레이어 없음)에서는 숨긴다.
    /// 씬 배치 없이 코드로 만든다(FloorSituationBanner·FloorMapOverlay와 같은 패턴, 새 아트 없음).
    /// 상세·QA: docs/04_구현위키/파티UI.md
    /// </summary>
    public class PartyFrameUI : MonoBehaviour
    {
        // 정렬 순서: HUD 0 < 파티 UI 5 < 팝업 10 < 지도 15 < 런 종료 20 < 디버그 메뉴 30 < 층 배너 50.
        private const int SortingOrder = 5;
        private const float RefreshInterval = 0.1f;
        /// <summary>줄 높이(1280×720 기준 캔버스 단위). HUD 배치 조정 때 최대 인원 × 이 값으로 파티 UI 높이를 계산한다.</summary>
        public const float RowHeight = 32f; // 2026-10-06: 도트 칸 테두리 여유로 30 → 32(4인 = 10+128 → HUD 기준 207, 체력 바 위 210보다 위)
        public const float TopMargin = 10f;
        private const float LeftMargin = 12f;
        private const float IconSize = 24f;
        private const float NameWidth = 140f;
        private const float BarWidth = 120f;
        private const float BarHeight = 16f; // 도트 게이지 틀 테두리 4px(=8) × 위아래 → 최소 16
        private const float HpTextWidth = 125f; // 2026-09-29: 보호막 "+N" 표시 공간 포함(100 → 125)
        private const float Gap = 8f;

        private static readonly Color RowBackground = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color NoSkillColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color NormalBorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color BarBackColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BarFillColor = new Color(0.35f, 0.85f, 0.35f, 1f);
        private static readonly Color DeadColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        private class Row
        {
            public RectTransform Root;
            public Image IconBorder;
            public Image IconFill;
            public Text IconLetter;
            public Text Name;
            public RectTransform BarFill;
            public Text Hp;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<PlayerIdentityInfo> _order = new List<PlayerIdentityInfo>();
        private CanvasGroup _group;
        private RectTransform _root;
        private float _refreshTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("PartyFrameUI");
            DontDestroyOnLoad(go);
            go.AddComponent<PartyFrameUI>();
        }

        private void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var rootGo = new GameObject("Rows", typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            _root = (RectTransform)rootGo.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = new Vector2(LeftMargin, -TopMargin);
            _root.sizeDelta = Vector2.zero;
        }

        private void Update()
        {
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f)
            {
                return;
            }

            _refreshTimer = RefreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            bool inGame = NetworkClient.active && DungeonGameManager.Instance != null && PlayerIdentityInfo.All.Count > 0;
            _group.alpha = inGame ? 1f : 0f;
            if (!inGame)
            {
                return;
            }

            // 본인 맨 위, 나머지는 플레이어 번호순.
            _order.Clear();
            foreach (var info in PlayerIdentityInfo.All)
            {
                if (info != null)
                {
                    _order.Add(info);
                }
            }

            _order.Sort((a, b) =>
            {
                if (a.isLocalPlayer != b.isLocalPlayer)
                {
                    return a.isLocalPlayer ? -1 : 1;
                }

                return a.PlayerNumber.CompareTo(b.PlayerNumber);
            });

            for (int i = 0; i < _order.Count; i++)
            {
                Fill(GetRow(i), _order[i]);
            }

            for (int i = _order.Count; i < _rows.Count; i++)
            {
                _rows[i].Root.gameObject.SetActive(false);
            }
        }

        private static void Fill(Row row, PlayerIdentityInfo info)
        {
            row.Root.gameObject.SetActive(true);

            var absorption = info.GetComponent<AbsorptionController>();
            AbsorbSkill skill = absorption != null ? absorption.SyncedSkill : AbsorbSkill.None;
            bool hasSkill = skill != AbsorbSkill.None;
            row.IconFill.color = hasSkill ? DroppedTrait.ColorFor(skill) : NoSkillColor;
            row.IconLetter.text = DroppedTrait.ShortNameFor(skill);
            row.IconBorder.color = NormalBorderColor;

            var health = info.GetComponent<PlayerHealth>();
            int current = health != null ? health.CurrentHealth : 0;
            int max = health != null ? health.MaxHealth : 0;
            bool dead = health != null && health.IsDead;
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            row.BarFill.anchorMax = new Vector2(ratio, 1f);

            row.Name.text = info.isLocalPlayer ? Loc.Get("party.you", info.DisplayName) : info.DisplayName;
            // 지도 점과 같은 색: 본인은 흰색, 팀원은 번호별 색(PlayerIdentityInfo 공용 테이블).
            row.Name.color = dead ? DeadColor : (info.isLocalPlayer ? Color.white : info.DisplayColor);
            bool shield = health != null && health.HasShield;
            row.Hp.text = dead ? Loc.Get("party.down") : shield ? Loc.Get("party.hp_shield", current, max) : Loc.Get("party.hp", current, max); // 보호막 = 양털(다음 피격 1회 무효)
            row.Hp.color = dead ? DeadColor : Color.white;
        }

        private Row GetRow(int index)
        {
            while (_rows.Count <= index)
            {
                _rows.Add(CreateRow(_rows.Count));
            }

            return _rows[index];
        }

        private Row CreateRow(int index)
        {
            float width = IconSize + NameWidth + BarWidth + HpTextWidth + Gap * 5f;
            var background = MakeImage($"Row{index}", _root, RowBackground);
            UIBuilder.SkinBox(background); // 도트 파인 칸(키트 없으면 예전 반투명 검정)
            var root = background.rectTransform;
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(width, RowHeight - 2f);
            root.anchoredPosition = new Vector2(0f, -index * RowHeight);

            float x = Gap; // 칸 테두리(8) 안쪽부터
            var border = MakeImage("IconBorder", root, NormalBorderColor);
            Place(border.rectTransform, x, IconSize, IconSize);
            var fill = MakeImage("IconFill", border.transform, NoSkillColor);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = new Vector2(2f, 2f);
            fill.rectTransform.offsetMax = new Vector2(-2f, -2f);
            var letter = MakeText("Letter", fill.transform, 14, TextAnchor.MiddleCenter);
            letter.color = Color.black;
            letter.horizontalOverflow = HorizontalWrapMode.Overflow;
            x += IconSize + Gap;

            var name = MakeText("Name", root, 15, TextAnchor.MiddleLeft);
            Place(name.rectTransform, x, NameWidth, RowHeight - 2f);
            x += NameWidth + Gap;

            var barBack = MakeImage("HealthBar", root, BarBackColor);
            Place(barBack.rectTransform, x, BarWidth, BarHeight);
            var barFill = MakeImage("Fill", barBack.transform, BarFillColor);
            barFill.rectTransform.anchorMin = Vector2.zero;
            barFill.rectTransform.anchorMax = Vector2.one;
            barFill.rectTransform.offsetMin = Vector2.zero;
            barFill.rectTransform.offsetMax = Vector2.zero;
            if (UIBuilder.Skin != null)
            {
                // 도트 게이지: 틀 외곽선+금 테 2px(=4) 안쪽 홈에 채움을 넣는다(채움 색은 그대로 틴트).
                UIBuilder.SkinBar(barBack, barFill);
                barFill.rectTransform.offsetMin = new Vector2(4f, 4f);
                barFill.rectTransform.offsetMax = new Vector2(-4f, -4f);
            }
            x += BarWidth + Gap;

            var hp = MakeText("Hp", root, 14, TextAnchor.MiddleLeft);
            Place(hp.rectTransform, x, HpTextWidth, RowHeight - 2f);

            return new Row
            {
                Root = root,
                IconBorder = border,
                IconFill = fill,
                IconLetter = letter,
                Name = name,
                BarFill = barFill.rectTransform,
                Hp = hp,
            };
        }

        // 줄 안에서 왼쪽 x 위치, 세로 가운데 정렬로 배치한다.
        private static void Place(RectTransform rect, float x, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        private static Image MakeImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text MakeText(string name, Transform parent, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<Text>();
            text.font = DamagePopup.BuiltinFont;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
