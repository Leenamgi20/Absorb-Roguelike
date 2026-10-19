using AbsorbRoguelike.Abilities;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 비주얼 효과 수치(2026-10-15, 비주얼 퀄리티 1단계 V1·V2·V3, 2단계 조명, 3단계 V8 외곽선·V9 디졸브). 화면 연출 전용 — 게임 규칙·판정과 무관하고 각 화면 로컬.
    /// 씬의 VisualFxDirector가 들고 있다가 VisualFx(이펙트 발광 배율)와 PostFx Volume(블룸·색 보정·비네트)에 반영한다.
    /// 에셋: `Assets/Data/VisualFxConfig.asset`. 필드 표: docs/04_구현위키/비주얼_효과.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Visual Fx Config", fileName = "VisualFxConfig")]
    public class VisualFxConfig : ScriptableObject
    {
        [System.Serializable]
        public struct ElementGrade
        {
            public ElementType element;
            [Tooltip("색 필터(곱하기). 흰색 = 변화 없음.")]
            public Color colorFilter;
            [Tooltip("채도 보정(-100~100).")]
            public float saturation;
            [Tooltip("노출 보정(EV).")]
            public float postExposure;
        }

        [Header("V1 블룸")]
        [Tooltip("블룸 세기. 문턱을 넘는(발광 재질) 부분만 번진다.")]
        public float bloomIntensity = 0.8f;
        [Tooltip("블룸 문턱. 1.0 이상이어야 일반 그림(밝기 ≤ 1)은 번지지 않는다.")]
        [Min(1f)] public float bloomThreshold = 1.0f;
        [Range(0f, 1f)] public float bloomScatter = 0.6f;

        [Header("V1 이펙트 발광 배율 (SpriteGlow _Intensity — 1 = 원래 밝기)")]
        public float hitSparkGlow = 1.6f;
        [Tooltip("흡수 알갱이·방출 금빛 별")]
        public float absorbGlow = 1.8f;
        [Tooltip("무기 속성 오라 테두리·조각")]
        public float weaponAuraGlow = 1.5f;
        public float portalVortexGlow = 1.4f;
        [Tooltip("화상 불꽃·감전 번개 상태 이펙트")]
        public float statusFxGlow = 1.4f;

        [Header("V2 층 속성 색 보정")]
        [Tooltip("전체 세기 배율(0 = 끔, 1 = 표의 값 그대로). 기본 약하게.")]
        [Range(0f, 1f)] public float gradeStrength = 0.5f;
        [Tooltip("층 속성이 바뀔 때 색이 넘어가는 시간(초).")]
        public float floorBlendTime = 1.0f;
        public ElementGrade[] elementGrades =
        {
            new ElementGrade { element = ElementType.None, colorFilter = Color.white, saturation = 0f, postExposure = 0f },
            new ElementGrade { element = ElementType.Fire, colorFilter = new Color(1f, 0.9f, 0.8f), saturation = 10f, postExposure = 0f },
            new ElementGrade { element = ElementType.Ice, colorFilter = new Color(0.85f, 0.93f, 1f), saturation = -10f, postExposure = 0.05f },
            new ElementGrade { element = ElementType.Electric, colorFilter = new Color(1f, 1f, 0.85f), saturation = 5f, postExposure = 0.05f },
            new ElementGrade { element = ElementType.Poison, colorFilter = new Color(0.88f, 1f, 0.85f), saturation = 0f, postExposure = -0.05f },
            new ElementGrade { element = ElementType.Earth, colorFilter = new Color(1f, 0.93f, 0.85f), saturation = -10f, postExposure = -0.05f },
        };

        [Header("V3 비네트")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.2f;
        public Color vignetteColor = Color.black;
        [Range(0.01f, 1f)] public float vignetteSmoothness = 0.4f;

        [Header("V3 위기 (내 체력 비율이 기준 미만)")]
        [Range(0f, 1f)] public float crisisHealthRatio = 0.3f;
        public Color crisisVignetteColor = new Color(0.6f, 0f, 0f, 1f);
        [Range(0f, 1f)] public float crisisIntensityMin = 0.3f;
        [Range(0f, 1f)] public float crisisIntensityMax = 0.45f;
        [Tooltip("맥박 횟수(초당).")]
        public float crisisPulseSpeed = 1.5f;

        [Header("V3 쓰러짐")]
        [Tooltip("쓰러졌을 때 더하는 채도(-100 = 흑백).")]
        [Range(-100f, 0f)] public float downedSaturation = -80f;
        [Tooltip("쓰러짐·부활 때 채도가 바뀌는 시간(초).")]
        public float downedBlendTime = 0.5f;

        // ── 2단계 조명·2P 구분 (2026-10-15) ── 조명은 각 화면 로컬 Light2D, VisualFx.Enabled가 꺼지면 전역 조명 1·추가 조명 끔.
        [Header("2단계 조명·2P 구분 — 전역 조명")]
        [Tooltip("[PLAYTEST] 효과 켜짐일 때 Global Light 2D 세기(어둡게 깔고 점광원으로 밝힘).")]
        [Range(0f, 2f)] public float globalLightIntensity = 0.7f;
        [Tooltip("효과 꺼짐일 때 Global Light 2D 세기(원래 밝기).")]
        [Range(0f, 2f)] public float globalLightOffIntensity = 1f;

        [Header("2단계 — 플레이어 빛")]
        [Tooltip("[PLAYTEST] 바깥 반경(유닛).")]
        public float playerLightOuterRadius = 2.5f;
        [Tooltip("[PLAYTEST] 안쪽 반경(유닛, 이 안은 최대 세기).")]
        public float playerLightInnerRadius = 0.3f;
        [Tooltip("[PLAYTEST] 세기.")]
        public float playerLightIntensity = 0.5f;
        [Tooltip("[PLAYTEST] 혼자일 때 색(중립 따뜻한 흰빛). 2명 이상이면 번호 색.")]
        public Color playerLightSoloColor = new Color(1f, 0.92f, 0.8f, 1f);

        [Header("2단계 — 포탈 빛")]
        [Tooltip("[PLAYTEST] 일반 층 포탈(보라).")]
        public Color portalLightColor = new Color(0.7f, 0.4f, 1f, 1f);
        [Tooltip("[PLAYTEST] 마지막 층 포탈(금빛).")]
        public Color portalFinalLightColor = new Color(1f, 0.8f, 0.3f, 1f);
        [Tooltip("[PLAYTEST] 반경(유닛).")]
        public float portalLightRadius = 3f;
        [Tooltip("[PLAYTEST] 세기(열림).")]
        public float portalLightIntensity = 0.9f;
        [Tooltip("[PLAYTEST] 잠김일 때 세기 배율.")]
        [Range(0f, 1f)] public float portalLockedIntensityMul = 0.35f;

        [Header("2단계 — 보스 빛")]
        [Tooltip("[PLAYTEST] 반경(유닛).")]
        public float bossLightRadius = 3.5f;
        [Tooltip("[PLAYTEST] 세기.")]
        public float bossLightIntensity = 0.7f;
        [Tooltip("[PLAYTEST] 층 속성이 없을 때 색.")]
        public Color bossLightDefaultColor = new Color(1f, 0.85f, 0.6f, 1f);
        [Tooltip("[PLAYTEST] 폭주(페이즈 4) 색.")]
        public Color bossEnrageColor = new Color(1f, 0.15f, 0.1f, 1f);
        [Tooltip("[PLAYTEST] 폭주 맥박 횟수(초당).")]
        public float bossEnragePulseSpeed = 1.5f;
        [Tooltip("[PLAYTEST] 폭주 맥박 최소 세기 배율.")]
        public float bossEnragePulseMin = 0.7f;
        [Tooltip("[PLAYTEST] 폭주 맥박 최대 세기 배율.")]
        public float bossEnragePulseMax = 1.4f;

        [Header("2단계 — 상태이상 빛")]
        [Tooltip("[PLAYTEST] 화상 빛 색(주황).")]
        public Color burnLightColor = new Color(1f, 0.5f, 0.1f, 1f);
        [Tooltip("[PLAYTEST] 화상 빛 반경(유닛).")]
        public float burnLightRadius = 1.5f;
        [Tooltip("[PLAYTEST] 화상 빛 세기.")]
        public float burnLightIntensity = 0.6f;
        [Tooltip("[PLAYTEST] 감전 빛 색(노랑).")]
        public Color shockLightColor = new Color(1f, 0.95f, 0.3f, 1f);
        [Tooltip("[PLAYTEST] 감전 빛 반경(유닛).")]
        public float shockLightRadius = 1.5f;
        [Tooltip("[PLAYTEST] 감전 빛 세기.")]
        public float shockLightIntensity = 0.7f;
        [Tooltip("[PLAYTEST] 감전 빛 깜빡임 횟수(초당).")]
        public float shockFlickerSpeed = 12f;

        [Header("2단계 — 브레스 빛")]
        [Tooltip("[PLAYTEST] 용 브레스 빛 색(주황).")]
        public Color breathLightColor = new Color(1f, 0.55f, 0.15f, 1f);
        [Tooltip("[PLAYTEST] 반경(유닛).")]
        public float breathLightRadius = 2.5f;
        [Tooltip("[PLAYTEST] 세기.")]
        public float breathLightIntensity = 0.8f;

        [Header("2단계 — 발밑 번호 고리 (2명 이상일 때 플레이어만)")]
        [Tooltip("[PLAYTEST] 고리 두께(px, 32PPU).")]
        [Min(1)] public int footRingThicknessPx = 1;
        [Tooltip("[PLAYTEST] 고리 알파.")]
        [Range(0f, 1f)] public float footRingAlpha = 0.9f;
        [Tooltip("[PLAYTEST] 그림자 타원보다 가로로 넓힐 px(32PPU).")]
        [Min(0)] public int footRingPadXPx = 2;
        [Tooltip("[PLAYTEST] 그림자 타원보다 세로로 넓힐 px(32PPU).")]
        [Min(0)] public int footRingPadYPx = 1;

        // ── 3단계 디졸브·외곽선 (2026-10-15) ── 디졸브는 각 화면 로컬 잔상(DissolveGhost), 외곽선은 Fx/SpriteOutline 셰이더(VisualFx.ApplyOutline).
        [Header("3단계 — V9 디졸브")]
        [Tooltip("[PLAYTEST] 잡몹·정예 사라짐, 보스 시체 흡수 때 부서지는 시간(초). 0.4~0.6 범위 권장.")]
        [Range(0.1f, 1.5f)] public float dissolveDuration = 0.5f;
        [Tooltip("[PLAYTEST] 부서지는 가장자리 폭(노이즈 구간, 0~0.5). 클수록 빛나는 도트가 많다.")]
        [Range(0f, 0.5f)] public float dissolveEdgeWidth = 0.12f;
        [Tooltip("[PLAYTEST] 가장자리 발광 배율(HDR, 1 초과면 블룸).")]
        public float dissolveEdgeGlow = 2f;
        [Tooltip("[PLAYTEST] 상태이상·층 속성이 없을 때 가장자리 색(흰빛).")]
        public Color dissolveDefaultEdgeColor = new Color(1f, 0.97f, 0.9f, 1f);

        [Header("3단계 — V8 외곽선")]
        [Tooltip("[PLAYTEST] 외곽선 두께(정수 텍셀, 1 = 32PPU 도트 1px). 타이트 스프라이트 메시 여백이 1px이라 1 권장.")]
        [Range(1, 4)] public int outlineThickness = 1;
        [Tooltip("[PLAYTEST] 플레이어 외곽선 색(1P·2P 공통 흰색 — 2P 구분은 발밑 고리·조명 색).")]
        public Color playerOutlineColor = Color.white;
        [Tooltip("[PLAYTEST] 정예 외곽선 색(주황). 몸 틴트와 무관하게 이 색 그대로.")]
        public Color eliteOutlineColor = new Color(1f, 0.7f, 0.2f, 1f);
        [Tooltip("[PLAYTEST] 외곽선 알파(색의 알파에 곱함).")]
        [Range(0f, 1f)] public float outlineAlpha = 1f;
        [Tooltip("[PLAYTEST] 외곽선이 2D 조명을 받는 정도(0 = 조명 무시로 늘 또렷, 1 = 몸과 똑같이 어두워짐).")]
        [Range(0f, 1f)] public float outlineLightInfluence = 0.25f;

        public float GlowFor(GlowKind kind)
        {
            switch (kind)
            {
                case GlowKind.HitSpark: return hitSparkGlow;
                case GlowKind.Absorb: return absorbGlow;
                case GlowKind.WeaponAura: return weaponAuraGlow;
                case GlowKind.PortalVortex: return portalVortexGlow;
                default: return statusFxGlow;
            }
        }

        public ElementGrade GradeFor(ElementType element)
        {
            if (elementGrades != null)
            {
                foreach (ElementGrade grade in elementGrades)
                {
                    if (grade.element == element)
                    {
                        return grade;
                    }
                }
            }

            return new ElementGrade { element = element, colorFilter = Color.white };
        }
    }
}
