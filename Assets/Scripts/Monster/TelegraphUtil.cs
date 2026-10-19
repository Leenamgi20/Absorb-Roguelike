using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 공격 예고(텔레그래프) 공용 헬퍼 — 보스(BossBrain의 패턴 A~E)와 잡몹(MonsterBehaviorController 계열)이 같이 쓴다(2026-10-02).
    /// - 모양 생성: 크기 1짜리 스프라이트를 만들고, 실제 크기는 호출부가 localScale로 맞춘다(보스 기존 생성 코드를 그대로 옮김 — 모양 동일).
    /// - 진하기 규칙(결정 ㊶): SetProgress 하나로 통일. 예고가 시작되면 연하게(알파 0.15) 나타나 진행률에 비례해 0.65까지 진해지고,
    ///   남은 시간 마지막 0.1초는 알파 0.9 + 한 번 밝게 번쩍(흰색 쪽으로) — 색 진하기만 보고 공격까지 얼마나 남았는지 가늠할 수 있게.
    /// 표시 전용(클라이언트 로컬) — 판정과 무관하다.
    /// </summary>
    public static class TelegraphUtil
    {
        // [PLAYTEST] 예고 진하기 규칙 ㊶ 수치
        public const float StartAlpha = 0.15f;  // [PLAYTEST] 예고 시작 알파
        public const float EndAlpha = 0.65f;    // [PLAYTEST] 마지막 구간 직전 알파
        public const float FinalAlpha = 0.9f;   // [PLAYTEST] 남은 시간 FinalWindow초 구간 알파
        public const float FinalWindow = 0.1f;  // [PLAYTEST] 마지막 번쩍 구간(초)
        public const float FlashWhite = 0.6f;   // [PLAYTEST] 번쩍임 최대 흰색 비율

        /// <summary>1×1 직사각형(흰 텍스처 4×4, PPU 4). pivot (0.5,0.5) = 중심 기준(보스 돌진), (0,0.5) = 몸에서 앞으로 뻗는 직선(잡몹).</summary>
        public static GameObject CreateRect(Transform parent, string objectName, Vector2 pivot, Color color, SpriteRenderer sortReference)
        {
            return CreateObject(parent, objectName, Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), pivot, 4f), color, sortReference);
        }

        /// <summary>몸에서 앞(+x)으로 뻗는 직선 예고(길이·폭은 PlaceLine으로 맞춘다).</summary>
        public static GameObject CreateLine(Transform parent, string objectName, Color color, SpriteRenderer sortReference)
        {
            return CreateRect(parent, objectName, new Vector2(0f, 0.5f), color, sortReference);
        }

        /// <summary>반지름 1짜리 부채꼴 스프라이트(중심 피벗, +x 방향, 반각 halfAngle). 크기는 반경으로 스케일한다.</summary>
        public static GameObject CreateCone(Transform parent, string objectName, float halfAngle, Color color, SpriteRenderer sortReference)
        {
            const int size = 64;
            var tex = new Texture2D(size, size) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var center = new Vector2(size * 0.5f, size * 0.5f);
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - center;
                    bool inside = d.magnitude <= size * 0.5f && Vector2.Angle(Vector2.right, d) <= halfAngle;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            }

            tex.Apply();
            return CreateObject(parent, objectName, Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 0.5f), color, sortReference);
        }

        /// <summary>반지름 1짜리 원 스프라이트(중심 피벗). 크기는 반경으로 스케일한다.</summary>
        public static GameObject CreateCircle(Transform parent, string objectName, Color color, SpriteRenderer sortReference)
        {
            const int size = 64;
            var tex = new Texture2D(size, size) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var center = new Vector2(size * 0.5f, size * 0.5f);
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    bool inside = (new Vector2(x + 0.5f, y + 0.5f) - center).magnitude <= size * 0.5f;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            }

            tex.Apply();
            return CreateObject(parent, objectName, Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 0.5f), color, sortReference);
        }

        /// <summary>예고 오브젝트 공통 생성: 부모 아래 SpriteRenderer 하나, 정렬은 참조(몸) 바로 뒤(-1), 꺼진 상태로 시작.</summary>
        public static GameObject CreateObject(Transform parent, string objectName, Sprite sprite, Color color, SpriteRenderer sortReference)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            if (sortReference != null)
            {
                sr.sortingLayerID = sortReference.sortingLayerID;
                sr.sortingOrder = sortReference.sortingOrder - 1;
            }

            go.SetActive(false);
            return go;
        }

        /// <summary>CreateLine으로 만든 직선을 방향 dir, 길이 length, 폭 width로 놓는다(로컬 스케일 — 잡몹 기존 방식과 같음).</summary>
        public static void PlaceLine(GameObject line, Vector2 dir, float length, float width)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            line.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            line.transform.localScale = new Vector3(length, width, 1f);
        }

        /// <summary>예고 진행률(0~1). startTime은 예고가 이 화면에서 시작된 로컬 Time.time.</summary>
        public static float Progress(float startTime, float duration)
        {
            return duration <= 0f ? 1f : Mathf.Clamp01((Time.time - startTime) / duration);
        }

        /// <summary>
        /// 예고 진하기 규칙 ㊶. progress01 = 예고 진행률, duration = 예고 전체 길이(초, 마지막 0.1초 구간 환산용).
        /// baseColor의 RGB만 쓰고 알파는 규칙대로 정한다. 마지막 구간에서는 흰색 쪽으로 한 번 올라갔다 내려오는 번쩍임.
        /// </summary>
        public static void SetProgress(SpriteRenderer renderer, float progress01, Color baseColor, float duration)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.color = ProgressColor(progress01, baseColor, duration);
        }

        public static void SetProgress(GameObject telegraph, float progress01, Color baseColor, float duration)
        {
            if (telegraph != null)
            {
                SetProgress(telegraph.GetComponent<SpriteRenderer>(), progress01, baseColor, duration);
            }
        }

        public static Color ProgressColor(float progress01, Color baseColor, float duration)
        {
            float p = Mathf.Clamp01(progress01);
            float finalStart = duration > FinalWindow ? 1f - FinalWindow / duration : 1f; // 예고가 0.1초보다 짧으면 번쩍 구간 없음
            Color c = baseColor;
            if (p >= finalStart && finalStart < 1f)
            {
                float t = (p - finalStart) / (1f - finalStart);
                c = Color.Lerp(baseColor, Color.white, Mathf.Sin(t * Mathf.PI) * FlashWhite);
                c.a = FinalAlpha;
                return c;
            }

            float k = finalStart > 0f ? p / finalStart : 1f;
            c.a = Mathf.Lerp(StartAlpha, EndAlpha, k);
            return c;
        }
    }
}
