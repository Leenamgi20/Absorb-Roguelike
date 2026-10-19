using System.Collections.Generic;
using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// Tab 지도 + 층 정보 패널(2026-09-28 유저 요청). Tab을 누르고 있는 동안만 화면 중앙에 이번 층에서 파티가 가 본 방 지도와
    /// 층 정보(층 N / M, 층 속성, 보스 특성 — 힌트, 이 층 동물)를 보여 준다. 입력을 소비하지 않아 이동·공격·가드는 그대로 된다.
    ///
    /// 2차(서버 동기화): 방·복도 목록과 종류·클리어·방문 여부는 서버가 만든 <see cref="FloorMapService.MapRooms"/>(SyncList)만
    /// 읽는다 — 방문은 파티 공유(누구든 한 명이 들어가면 모두의 지도에 보임), 원격 클라이언트도 호스트와 같은 지도를 본다.
    /// 로컬에서 계산하는 것은 현재 방(노랑 테두리, 로컬 플레이어 위치)과 플레이어 점(NetworkTransform으로 동기화된 transform 위치)뿐이다.
    ///
    /// 한계(docs/04_구현위키/탭지도.md):
    /// - 방은 입장 트리거 Collider2D.bounds 사각형으로 그린다(비직사각형 방도 사각형으로 보임).
    /// - 방문 판정은 서버가 0.2초마다 하므로 방에 들어간 뒤 최대 0.2초(+ 네트워크 지연) 늦게 보인다.
    /// 씬 배치 없이 코드로 만든다(FloorSituationBanner와 같은 패턴, 새 아트 없음).
    ///
    /// 3차(2026-09-29): 지도가 열려 있는 동안 클리어한 방(시작 방 포함, 보스 방은 보스 처치 후)에 커서를 올리면 밝게 강조하고
    /// '클릭해 이동' 툴팁을 띄우며, 좌클릭하면 <see cref="FloorMapService.CmdRequestMapTeleport"/>로 서버에 순간이동을 요청한다.
    /// 최종 판정은 서버가 하고, 거부되면 화면 아래쪽에 안내 문구를 2초 동안 띄운다. 지도 패널 위 좌클릭은 UI 클릭으로 처리돼 공격이 나가지 않는다.
    /// 한쪽 끝에만 방문한 방이 닿은 복도에는 반대쪽 끝에 '?'를 찍는다(가 보지 않은 길).
    /// </summary>
    public class FloorMapOverlay : MonoBehaviour
    {
        // 정렬 순서: HUD 0 < 파티 UI 5 < 팝업 10 < 지도 15 < 전체 화면(런 종료) 20 < 디버그 메뉴 30 < 층 배너 50.
        // 디버그 메뉴 값은 Assets/Prefabs/UI/DebugMenu.prefab의 Canvas m_SortingOrder: 30(씬 배치 프리팹)으로 확인함.
        // 런 종료 화면(20) 위에 지도가 겹치지 않도록 20보다 작게 잡았다.
        private const int SortingOrder = 15;
        private const float RedrawInterval = 0.15f;
        private const float MapMargin = 28f; // 2026-10-06: 도트 패널 금 테(10px × 2단위 = 20) 안쪽에 지도가 들어가게 20 → 28
        private const float DotSize = 8f;
        private const float DenyMessageDuration = 2f;
        private const float HoverLighten = 0.45f;
        private const float CorridorTouchPadding = 1f;
        private static readonly Vector2 MapPanelSize = new Vector2(800f, 480f);
        private static readonly Vector2 MapPanelPosition = new Vector2(0f, -50f);
        private static readonly Vector2 InfoPanelSize = new Vector2(800f, 112f);
        private static readonly Vector2 InfoPanelPosition = new Vector2(0f, 250f);

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.7f);
        private static readonly Color VisitedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        private static readonly Color ClearedColor = new Color(0.62f, 0.85f, 0.62f, 1f);
        private static readonly Color BossColor = new Color(0.85f, 0.25f, 0.25f, 1f);
        private static readonly Color CorridorColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color CurrentBorderColor = new Color(1f, 0.9f, 0.2f, 1f);
        private static readonly Color RoomBorderColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color LocalDotColor = Color.white;

        private class AreaView
        {
            public RectTransform Border;
            public Image BorderImage;
            public RectTransform Fill;
            public Image FillImage;
            public Text Label;
            public int Index;      // 이 뷰가 그리고 있는 mapRooms 인덱스(뷰 풀 순서와 다르다)
            public Color BaseColor; // 강조 전 채움색
        }

        private class DotView
        {
            public Image Dot;
            public Text Label;
        }

        private readonly List<AreaView> _roomViews = new List<AreaView>();
        private readonly List<AreaView> _corridorViews = new List<AreaView>();
        private readonly List<DotView> _dots = new List<DotView>();
        private readonly List<Text> _unknownMarks = new List<Text>();

        private CanvasGroup _group;
        private bool _soundOpen;
        private RectTransform _mapPanel;
        private RectTransform _corridorLayer;
        private RectTransform _roomLayer;
        private RectTransform _dotLayer;
        private Text _emptyText;
        private Text _infoText;
        private Image _mapPanelImage;
        private RectTransform _content;
        private RectTransform _unknownLayer;
        private Text _tooltip;
        private Text _denyText;
        private float _denyTimer;
        private AreaView _hovered;
        private int _roomsUsed;
        private int _currentIndex = -1;

        private float _redrawTimer;
        private Rect _union;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("FloorMapOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<FloorMapOverlay>();
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
            // 3차: 지도 방 클릭·패널 위 공격 차단용. 닫혀 있을 때는 CanvasGroup.blocksRaycasts=false라 클릭이 게임으로 그대로 간다.
            gameObject.AddComponent<GraphicRaycaster>();
            // 'Tab: 지도' 안내는 항상 보여야 하므로 CanvasGroup(Tab 누를 때만 보임)은 루트가 아니라 Content 자식에 둔다.
            // HUD 텍스트(700×320, 글자 30, Truncate)는 이미 9줄 가까이 차 있어 줄을 더하면 잘려서 여기 하단 모서리에 둔다.
            var hint = MakeText("TabHint", transform, 14, TextAnchor.LowerLeft);
            UIBuilder.Localize(hint, "map.tab_hint");
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            hint.rectTransform.offsetMin = new Vector2(12f, 8f);
            hint.rectTransform.offsetMax = new Vector2(-12f, -8f);

            var content = MakeLayer("Content", transform);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            _content = content;
            _group = content.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _mapPanelImage = MakeImage("MapPanel", content, PanelColor);
            _mapPanelImage.raycastTarget = true; // 패널 위 좌클릭을 UI로 잡아 공격을 막는다(열림 동안만 blocksRaycasts)
            UIBuilder.SkinPanel(_mapPanelImage, plain: true); // 도트 패널 틀(보석 없음). 안의 방 사각형은 그대로. 키트 없으면 예전 단색
            _mapPanel = _mapPanelImage.rectTransform;
            _mapPanel.sizeDelta = MapPanelSize;
            _mapPanel.anchoredPosition = MapPanelPosition;
            _corridorLayer = MakeLayer("Corridors", _mapPanel);
            _roomLayer = MakeLayer("Rooms", _mapPanel);
            _unknownLayer = MakeLayer("Unknown", _mapPanel);
            _dotLayer = MakeLayer("Players", _mapPanel);
            _emptyText = MakeText("Empty", _mapPanel, 20, TextAnchor.MiddleCenter);
            UIBuilder.Localize(_emptyText, "map.empty");

            var infoPanel = MakeImage("InfoPanel", content, PanelColor).rectTransform;
            infoPanel.sizeDelta = InfoPanelSize;
            infoPanel.anchoredPosition = InfoPanelPosition;
            _infoText = MakeText("Info", infoPanel, 18, TextAnchor.MiddleLeft);
            _infoText.rectTransform.offsetMin = new Vector2(16f, 4f);
            _infoText.rectTransform.offsetMax = new Vector2(-16f, -4f);

            // 커서 옆 '클릭해 이동' 툴팁(Content 안 — 지도와 함께 보이고 숨는다).
            _tooltip = MakeText("Tooltip", content, 14, TextAnchor.UpperLeft);
            var tipRect = _tooltip.rectTransform;
            tipRect.anchorMin = tipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tipRect.pivot = new Vector2(0f, 1f);
            tipRect.sizeDelta = new Vector2(120f, 20f);
            _tooltip.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIBuilder.Localize(_tooltip, "map.click_to_move");
            _tooltip.gameObject.AddComponent<Outline>().effectColor = Color.black;
            _tooltip.enabled = false;

            // 거부 안내(Content 밖, 루트 캔버스 아래 — Tab을 떼도 보인다). 화면 중앙 아래쪽.
            _denyText = MakeText("TeleportDenied", transform, 22, TextAnchor.LowerCenter);
            _denyText.rectTransform.offsetMin = new Vector2(0f, 140f);
            _denyText.color = new Color(1f, 0.85f, 0.4f, 1f);
            _denyText.gameObject.AddComponent<Outline>().effectColor = Color.black;
            _denyText.enabled = false;

            FloorMapService.OnMapChanged += OnMapChanged;
            FloorMapService.OnMapTeleportDenied += OnTeleportDenied;
        }

        private void OnDestroy()
        {
            FloorMapService.OnMapChanged -= OnMapChanged;
            FloorMapService.OnMapTeleportDenied -= OnTeleportDenied;
        }

        private void OnTeleportDenied(string message)
        {
            _denyText.text = message;
            _denyText.enabled = true;
            _denyTimer = DenyMessageDuration;
        }

        // 열려 있을 때 목록이 바뀌면(방문·클리어·층 이동) 다음 프레임에 바로 다시 그린다.
        private void OnMapChanged()
        {
            _redrawTimer = 0f;
        }

        private void Update()
        {
            if (_denyText.enabled)
            {
                _denyTimer -= Time.unscaledDeltaTime;
                if (_denyTimer <= 0f)
                {
                    _denyText.enabled = false;
                }
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool open = keyboard != null && keyboard.tabKey.isPressed && !PauseMenuController.IsOpen && !OptionsMenuController.IsOpen; // ESC 메뉴·옵션 중엔 지도 닫힘
            if (open != _soundOpen)
            {
                _soundOpen = open;
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.UiOpen); // 지도 열기·닫기(2026-10-14, 로컬)
            }
            if (!open)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false; // 닫힘: 클릭이 그대로 게임으로 통과
                _redrawTimer = 0f; // 다시 열면 바로 그린다
                SetHovered(null, false);
                return;
            }

            _group.alpha = 1f;
            _group.blocksRaycasts = true; // 열림: 패널 위 클릭은 UI가 받아 공격이 막힌다
            _redrawTimer -= Time.unscaledDeltaTime;
            if (_redrawTimer <= 0f)
            {
                _redrawTimer = RedrawInterval;
                _hovered = null; // DrawMap이 채움색을 다시 칠하므로 강조 상태도 새로 잡는다
                DrawMap();
                DrawInfo();
            }

            UpdateHoverAndClick();
        }

        // 매 프레임(열림 동안): 커서 아래 방을 찾아 이동 가능하면 강조·툴팁, 좌클릭이면 서버에 요청한다(최종 판정은 서버).
        private void UpdateHoverAndClick()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var dungeon = FloorMapService.Instance;
            if (mouse == null || dungeon == null)
            {
                SetHovered(null, false);
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            AreaView hit = null;
            for (int i = _roomsUsed - 1; i >= 0; i--) // 나중에 그린(위에 있는) 것부터
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_roomViews[i].Border, screen, null))
                {
                    hit = _roomViews[i];
                    break;
                }
            }

            IReadOnlyList<FloorMapService.MapRoom> rooms = dungeon.MapRooms;
            bool valid = hit != null && hit.Index >= 0 && hit.Index < rooms.Count;
            bool teleportable = valid && CanTeleportTo(rooms[hit.Index], hit.Index);
            SetHovered(valid ? hit : null, teleportable);
            if (teleportable && RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, screen, null, out Vector2 local))
            {
                _tooltip.rectTransform.anchoredPosition = local + new Vector2(16f, -8f);
            }

            // 이동 불가한 방(클리어 안 됨 등)도 보내 서버 안내를 받는다. 현재 방은 보내지 않는다.
            if (valid && hit.Index != _currentIndex && mouse.leftButton.wasPressedThisFrame && NetworkClient.isConnected)
            {
                dungeon.CmdRequestMapTeleport(hit.Index);
            }
        }

        // 로컬 판정(강조 전용 — 서버가 다시 검증한다): 복도 아님, 방문함, 클리어됐거나 시작 방, 현재 방 아님.
        private bool CanTeleportTo(FloorMapService.MapRoom room, int index)
        {
            return room.kind != FloorMapService.MapKindCorridor
                && room.visited
                && (room.cleared || room.kind == FloorMapService.MapKindSpawn)
                && index != _currentIndex;
        }

        private void SetHovered(AreaView view, bool highlight)
        {
            if (_hovered != null && (_hovered != view || !highlight))
            {
                _hovered.FillImage.color = _hovered.BaseColor;
            }

            _hovered = highlight ? view : null;
            if (_hovered != null)
            {
                _hovered.FillImage.color = Color.Lerp(_hovered.BaseColor, Color.white, HoverLighten);
            }

            _tooltip.enabled = _hovered != null;
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        private void DrawMap()
        {
            var dungeon = FloorMapService.Instance;
            IReadOnlyList<FloorMapService.MapRoom> rooms = dungeon != null ? dungeon.MapRooms : null;
            int count = rooms != null ? rooms.Count : 0;
            _emptyText.enabled = count == 0;

            // 전체 범위는 방문 여부와 무관하게 이 층 전체 기준이라, 탐험해도 지도 배율이 바뀌지 않는다.
            _union = default;
            for (int i = 0; i < count; i++)
            {
                _union = i == 0 ? rooms[i].bounds : Union(_union, rooms[i].bounds);
            }

            Vector2 inner = MapPanelSize - 2f * MapMargin * Vector2.one;
            float scale = _union.width > 0f && _union.height > 0f
                ? Mathf.Min(inner.x / _union.width, inner.y / _union.height)
                : 0f;

            // 현재 방: 로컬 플레이어 위치가 들어 있는 (복도 아닌) 방. 여러 개면 마지막 것(1차와 같음).
            int current = -1;
            if (NetworkClient.localPlayer != null)
            {
                Vector2 position = NetworkClient.localPlayer.transform.position;
                for (int i = 0; i < count; i++)
                {
                    if (rooms[i].kind != FloorMapService.MapKindCorridor && rooms[i].bounds.Contains(position))
                    {
                        current = i;
                    }
                }
            }

            _currentIndex = current; // 3차: 클릭·강조 판정용
            int corridorsUsed = 0;
            int roomsUsed = 0;
            for (int i = 0; i < count; i++)
            {
                FloorMapService.MapRoom room = rooms[i];
                if (!room.visited && room.kind != FloorMapService.MapKindBoss)
                {
                    continue; // 방문 안 한 방·복도는 숨긴다(안개). 보스 방만 처음부터 보인다(2026-10-16 — 방이 많아 보스 방 찾기가 루즈함)
                }

                if (room.kind == FloorMapService.MapKindCorridor)
                {
                    var corridorView = GetView(_corridorViews, _corridorLayer, corridorsUsed++);
                    corridorView.Border.gameObject.SetActive(true);
                    Place(corridorView, room.bounds, scale, 0f);
                    corridorView.FillImage.color = CorridorColor;
                    corridorView.BaseColor = CorridorColor;
                    corridorView.Index = i;
                    corridorView.BorderImage.color = Color.clear;
                    corridorView.Label.text = "";
                    continue;
                }

                var view = GetView(_roomViews, _roomLayer, roomsUsed++);
                view.Border.gameObject.SetActive(true);
                bool isCurrent = i == current;
                Place(view, room.bounds, scale, isCurrent ? 3f : 1.5f);
                view.BorderImage.color = isCurrent ? CurrentBorderColor : RoomBorderColor;
                if (room.kind == FloorMapService.MapKindBoss)
                {
                    view.FillImage.color = BossColor;
                    view.Label.text = Loc.Get("map.boss");
                }
                else
                {
                    view.FillImage.color = room.cleared ? ClearedColor : VisitedColor;
                    view.Label.text = room.kind == FloorMapService.MapKindSpawn ? "S" : "";
                }

                view.BaseColor = view.FillImage.color;
                view.Index = i;
            }

            HideExtra(_corridorViews, corridorsUsed);
            HideExtra(_roomViews, roomsUsed);
            _roomsUsed = roomsUsed;
            DrawUnknownMarks(rooms, count, scale);
            DrawDots(scale, count > 0);
        }

        // 방문한 복도 중 한쪽 끝에만 방문한 방이 닿아 있는 것(복도 사각형을 1유닛 넓혀 방 사각형과 겹침 검사, 방이 딱 하나)은
        // 방이 없는 반대쪽 끝에 '?'를 찍는다. 기존 SyncList 데이터만으로 계산한다.
        private void DrawUnknownMarks(IReadOnlyList<FloorMapService.MapRoom> rooms, int count, float scale)
        {
            int used = 0;
            for (int i = 0; i < count; i++)
            {
                FloorMapService.MapRoom corridor = rooms[i];
                if (corridor.kind != FloorMapService.MapKindCorridor || !corridor.visited)
                {
                    continue;
                }

                Rect padded = new Rect(corridor.bounds.position - CorridorTouchPadding * Vector2.one,
                    corridor.bounds.size + 2f * CorridorTouchPadding * Vector2.one);
                int touching = 0;
                Rect touched = default;
                for (int j = 0; j < count; j++)
                {
                    FloorMapService.MapRoom room = rooms[j];
                    if (room.kind != FloorMapService.MapKindCorridor && room.visited && padded.Overlaps(room.bounds))
                    {
                        touching++;
                        touched = room.bounds;
                    }
                }

                if (touching != 1)
                {
                    continue;
                }

                // 긴 축 방향으로, 닿은 방에서 먼 쪽 끝.
                Rect b = corridor.bounds;
                Vector2 end = b.width >= b.height
                    ? new Vector2(touched.center.x < b.center.x ? b.xMax : b.xMin, b.center.y)
                    : new Vector2(b.center.x, touched.center.y < b.center.y ? b.yMax : b.yMin);
                Text mark = GetUnknownMark(used++);
                mark.rectTransform.anchoredPosition = ToMap(end, scale);
            }

            for (int i = used; i < _unknownMarks.Count; i++)
            {
                _unknownMarks[i].gameObject.SetActive(false);
            }
        }

        private Text GetUnknownMark(int index)
        {
            while (_unknownMarks.Count <= index)
            {
                var mark = MakeText("Unknown", _unknownLayer, 16, TextAnchor.MiddleCenter);
                var rect = mark.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(20f, 20f);
                mark.text = "?";
                mark.color = new Color(1f, 0.9f, 0.2f, 1f);
                mark.fontStyle = FontStyle.Bold;
                mark.gameObject.AddComponent<Outline>().effectColor = Color.black;
                _unknownMarks.Add(mark);
            }

            _unknownMarks[index].gameObject.SetActive(true);
            return _unknownMarks[index];
        }

        // 씬의 모든 플레이어를 transform 위치(원격 플레이어는 NetworkTransform 동기화 값) 그대로 찍는다.
        // 로컬 = 흰 점, 팀원 = 플레이어 번호별 색(PlayerIdentityInfo, 파티 UI와 같은 색). 점 옆에 '1P/2P' 라벨.
        private void DrawDots(float scale, bool hasMap)
        {
            int used = 0;
            if (hasMap)
            {
                foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                {
                    var identity = player.GetComponent<PlayerIdentityInfo>();
                    Color color = player.isLocalPlayer
                        ? LocalDotColor
                        : identity != null ? identity.DisplayColor : PlayerIdentityInfo.ColorForNumber(0);
                    var view = GetDot(used++);
                    view.Dot.color = color;
                    view.Dot.rectTransform.anchoredPosition = ToMap(player.transform.position, scale);
                    view.Label.color = color;
                    view.Label.text = identity != null ? identity.ShortLabel : "";
                    // 로컬 플레이어 점을 맨 위로
                    if (player.isLocalPlayer)
                    {
                        view.Dot.rectTransform.SetAsLastSibling();
                    }
                }
            }

            for (int i = used; i < _dots.Count; i++)
            {
                _dots[i].Dot.gameObject.SetActive(false);
            }
        }

        private Vector2 ToMap(Vector2 world, float scale)
        {
            return (world - _union.center) * scale;
        }

        private void Place(AreaView view, Rect world, float scale, float border)
        {
            view.Border.anchoredPosition = ToMap(world.center, scale);
            view.Border.sizeDelta = world.size * scale + 2f * border * Vector2.one;
            view.Fill.offsetMin = new Vector2(border, border);
            view.Fill.offsetMax = new Vector2(-border, -border);
        }

        private static void HideExtra(List<AreaView> views, int count)
        {
            for (int i = count; i < views.Count; i++)
            {
                views[i].Border.gameObject.SetActive(false);
            }
        }

        private AreaView GetView(List<AreaView> views, RectTransform layer, int index)
        {
            while (views.Count <= index)
            {
                var border = MakeImage("Area", layer, Color.clear);
                var fill = MakeImage("Fill", border.transform, Color.white);
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = Vector2.one;
                var label = MakeText("Label", fill.transform, 14, TextAnchor.MiddleCenter);
                label.color = Color.black;
                label.horizontalOverflow = HorizontalWrapMode.Overflow; // 작은 방에서도 라벨이 잘리지 않게
                views.Add(new AreaView
                {
                    Border = border.rectTransform,
                    BorderImage = border,
                    Fill = fill.rectTransform,
                    FillImage = fill,
                    Label = label,
                });
            }

            return views[index];
        }

        private DotView GetDot(int index)
        {
            while (_dots.Count <= index)
            {
                var dot = MakeImage("Dot", _dotLayer, Color.white);
                dot.rectTransform.sizeDelta = new Vector2(DotSize, DotSize);
                var label = MakeText("Label", dot.transform, 11, TextAnchor.MiddleLeft);
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(30f, 14f);
                rect.anchoredPosition = new Vector2(2f, 0f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                _dots.Add(new DotView { Dot = dot, Label = label });
            }

            _dots[index].Dot.gameObject.SetActive(true);
            return _dots[index];
        }

        private void DrawInfo()
        {
            var dungeon = FloorProgression.Instance;
            if (dungeon == null)
            {
                _infoText.text = Loc.Get("map.info", "-", "-", "-", "-");
                return;
            }

            string element = dungeon.CurrentFloorElement == ElementType.None ? Loc.Get("common.none") : dungeon.CurrentFloorElement.DisplayName();
            var situation = dungeon.CurrentBossSituation;
            string boss = situation == BossSituation.None ? Loc.Get("common.none") : Loc.Get("map.boss_trait_value", situation.DisplayName(), situation.HintText());
            string animals = dungeon.AnimalPoolLabel;
            if (string.IsNullOrEmpty(animals))
            {
                animals = "-";
            }

            _infoText.text = Loc.Get("map.info", Loc.Get("map.floor_value", dungeon.Floor, dungeon.MaxFloors), element, boss, animals);
        }

        private static RectTransform MakeLayer(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            return rect;
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
