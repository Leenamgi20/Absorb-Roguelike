using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Localization;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 대미지 숫자 팝업의 종류(결정 ④B). 색만 다르다. Mirror ClientRpc 인자로 넘기므로 public enum.
    /// </summary>
    public enum DamagePopupKind
    {
        Normal,        // 흰색 — 무기·흡수 스킬·뱉기 등 직접 타격
        FireDot,       // 주황 — 화상 도트
        PoisonDot,     // 보라 — 중독 도트
        ElectricDischarge, // 노랑 — 전기 방전
        BossDeflect,   // 회색 — 보스에게 맨몸 무기 명중(대미지 1 고정, BossBrain 회색 번쩍임과 같은 의미)
        // 2026-09-28 토끼 귀 채찍(결정 ㉑A) — 기존 값 뒤에만 붙인다(RPC 인자 순서 유지).
        EarWhipTip,    // 금색·큰 글씨·'!' — 귀 채찍 끝부분 강타(직접 명중)
        EarWhipClose,  // 밝은 회색·작은 글씨 — 귀 채찍 근접(약한) 타격(직접 명중)
    }

    public static class DamagePopupKindExtensions
    {
        /// <summary>BossBrain.deflectTint 기본값과 같은 회색.</summary>
        public static readonly Color BossDeflectColor = new Color(0.45f, 0.45f, 0.45f);
        public static readonly Color EarWhipTipColor = new Color(1f, 0.82f, 0.2f);   // [PLAYTEST] 금색
        public static readonly Color EarWhipCloseColor = new Color(0.7f, 0.7f, 0.7f); // [PLAYTEST] BossDeflect(0.45)보다 밝은 회색

        public static Color PopupColor(this DamagePopupKind kind)
        {
            switch (kind)
            {
                case DamagePopupKind.FireDot: return ElementType.Fire.StatusTintColor();
                case DamagePopupKind.PoisonDot: return ElementType.Poison.StatusTintColor();
                case DamagePopupKind.ElectricDischarge: return ElementType.Electric.StatusTintColor();
                case DamagePopupKind.BossDeflect: return BossDeflectColor;
                case DamagePopupKind.EarWhipTip: return EarWhipTipColor;
                case DamagePopupKind.EarWhipClose: return EarWhipCloseColor;
                default: return Color.white;
            }
        }

        /// <summary>무기·흡수 스킬 등 직접 명중인지(보스 갑옷을 깎는지). 도트·방전·BossDeflect는 false.</summary>
        public static bool IsDirectHit(this DamagePopupKind kind)
        {
            return kind == DamagePopupKind.Normal || kind == DamagePopupKind.EarWhipTip || kind == DamagePopupKind.EarWhipClose;
        }

        /// <summary>팝업 글자 크기 배율 — 귀 채찍 끝부분은 크게(≈1.3배), 근접은 작게. [PLAYTEST]</summary>
        public static float PopupFontScale(this DamagePopupKind kind)
        {
            switch (kind)
            {
                case DamagePopupKind.EarWhipTip: return 1.3f;
                case DamagePopupKind.EarWhipClose: return 0.8f;
                default: return 1f;
            }
        }

        public static string PopupSuffix(this DamagePopupKind kind)
        {
            return kind == DamagePopupKind.EarWhipTip ? "!" : string.Empty;
        }
    }

    /// <summary>
    /// 월드 공간 대미지 숫자(결정 ④B). 순수 표시 전용 — 서버 로직 없음. 호출부: MonsterStub/BossStub.ShowDamagePopup
    /// (호스트는 바로, 원격 클라이언트는 RpcShowDamage로).
    ///
    /// 대상의 자식으로 붙이지 않고 독립 GameObject로 만든다 — 잡몹은 죽는 타격에서 바로 NetworkServer.Destroy되므로
    /// 자식이면 숫자도 같이 사라진다.
    ///
    /// 렌더링은 legacy TextMesh + 내장 폰트(LegacyRuntime.ttf, HUD Text와 같은 기본 폰트)를 쓴다. 카메라는
    /// PixelPerfectCamera(PPU 16, 320×180)지만 UpscaleRT·GridSnapping이 꺼져 있어 화면 해상도로 그려지므로, 월드
    /// 캔버스나 TextMeshPro 폰트 에셋 없이도 충분히 선명하다. fontSize를 크게, characterSize를 작게 잡아 글리프
    /// 텍스처 해상도를 확보한다(TextMesh 크기 ≈ fontSize × characterSize × 0.1 유닛).
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        public const float Pixel = 1f / 16f;
        private const float Lifetime = 0.7f;          // [PLAYTEST]
        private const float RiseDistance = 7f * Pixel; // [PLAYTEST] 수명 동안 떠오르는 높이
        private const float RandomOffsetX = 3.5f * Pixel; // 같은 대상 연속 타격이 겹치지 않게
        private const int PopupFontSize = 48;          // 48 × 0.1 × 0.1 ≈ 0.48유닛 ≈ 8px
        private const int PopupSortingOrder = 25;      // 캐릭터(10)·체력바(15/16)·스택 표시(20)보다 위

        private const float TextLifetime = 1.3f;       // [PLAYTEST] 글자 팝업은 읽을 시간이 필요해 더 길게
        private const float TextRiseDistance = 10f * Pixel;
        private const int TextFontSize = 40;

        private TextMesh _text;
        private Color _color;
        private float _lifetime = Lifetime;
        private float _riseDistance = RiseDistance;
        private Vector3 _start;
        private float _elapsed;

        private static Font _font;

        /// <summary>게임 공용 폰트. 2026-10-06부터 한글 도트 폰트 Galmuri11(OFL, `Resources/Fonts/Galmuri11`, 렌더링 Hinted Raster =
        /// 안티에일리어싱 없이 또렷한 픽셀). 없으면 예전 내장 LegacyRuntime.ttf. 한 번만 불러 재사용한다(이름은 예전 그대로).</summary>
        public static Font BuiltinFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.Load<Font>("Fonts/Galmuri11");
                    if (_font == null)
                    {
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                }

                return _font;
            }
        }

        /// <summary>
        /// 팝업·스택 라벨 공용 TextMesh 생성. parent가 null이면 루트에 만든다. 정렬은 가운데·아래 기준.
        /// </summary>
        public static TextMesh CreateText(string objectName, Transform parent, int fontSize, int sortingOrder)
        {
            var go = new GameObject(objectName);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var text = go.AddComponent<TextMesh>();
            text.font = BuiltinFont;
            text.fontSize = fontSize;
            text.characterSize = 0.1f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.richText = true;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = BuiltinFont.material;
            renderer.sortingOrder = sortingOrder;
            return text;
        }

        /// <summary>worldPos(대상 머리 위)에 숫자를 띄운다. 각 클라이언트가 로컬로만 만든다.</summary>
        public static void Spawn(Vector3 worldPos, int amount, DamagePopupKind kind)
        {
            if (amount <= 0)
            {
                return;
            }

            TextMesh text = CreateText("DamagePopup", null, Mathf.RoundToInt(PopupFontSize * kind.PopupFontScale()), PopupSortingOrder);
            text.richText = false;
            text.text = amount + kind.PopupSuffix();

            // 귀 채찍 구간 번쩍임(결정 ㉑A)은 2026-10-07부터 HitFx.OnDirectHit(금색/작은 스파크)가 대상 몸 위에 띄운다(예전 HitFlash 별 대체).

            var popup = text.gameObject.AddComponent<DamagePopup>();
            popup._text = text;
            popup._color = kind.PopupColor();
            popup._start = worldPos + new Vector3(Random.Range(-RandomOffsetX, RandomOffsetX), 0f, 0f);
            text.color = popup._color;
            text.transform.position = popup._start;
        }

        /// <summary>worldPos에 글자 팝업(예: "호랑이 획득!")을 띄운다. 대미지 숫자와 같은 방식으로 떠오르며 사라진다. 로컬 표시 전용.</summary>
        /// message가 Loc.Pack 문자열(서버가 RPC로 보낸 키+인자)이면 이 클라이언트 언어로 풀어 표시한다.
        public static void SpawnText(Vector3 worldPos, string message, Color color)
        {
            message = Loc.Unpack(message);
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            TextMesh text = CreateText("TextPopup", null, TextFontSize, PopupSortingOrder);
            text.richText = false;
            text.text = message;

            var popup = text.gameObject.AddComponent<DamagePopup>();
            popup._text = text;
            popup._color = color;
            popup._start = worldPos;
            popup._lifetime = TextLifetime;
            popup._riseDistance = TextRiseDistance;
            text.color = color;
            text.transform.position = worldPos;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / _lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = _start + new Vector3(0f, _riseDistance * t, 0f);
            Color c = _color;
            c.a = 1f - t;
            _text.color = c;
        }
    }
}
