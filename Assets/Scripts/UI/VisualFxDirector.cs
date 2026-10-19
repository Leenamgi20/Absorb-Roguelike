using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 화면 후처리 연출(2026-10-15, 비주얼 퀄리티 1단계). 씬의 PostFx 오브젝트(전역 Volume)에 붙는다. 각 화면 로컬 — 네트워크 코드 없음.
    /// - V1 블룸: VisualFxConfig 값을 Bloom에 넣는다(문턱 ≥ 1 → 발광 재질 이펙트만 번짐, VisualFx 참고).
    /// - V2 층 속성 색 보정: FloorProgression.CurrentFloorElement에 맞춰 Color Adjustments(색 필터·채도·노출)를 약하게, ~1초에 걸쳐 바꾼다.
    ///   던전이 없거나 속성 None(타이틀·로비)이면 중립.
    /// - V3 비네트·위기: 기본 비네트를 깔고, 내 PlayerHealth(관전 중에도 내 것) 체력이 기준 미만이면 붉게 맥동, 쓰러지면 채도를 크게 낮춘다.
    /// - 2단계 전역 조명: 씬의 Global Light 2D 세기를 globalLightIntensity(0.7)로 낮춘다(씬 값 1은 그대로, 꺼짐이면 globalLightOffIntensity).
    /// VisualFx.Enabled가 꺼지면 Volume을 끈다. 공유 프로필 에셋이 플레이 중 바뀌지 않도록 복제본을 쓴다.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class VisualFxDirector : MonoBehaviour
    {
        [SerializeField] private VisualFxConfig config;

        private Volume _volume;
        private VolumeProfile _profile;
        private Bloom _bloom;
        private ColorAdjustments _color;
        private Vignette _vignette;
        private VisualFxConfig _runtimeDefaults;
        private Light2D _globalLight; // 2단계 전역 조명(씬의 Global Light 2D)

        // V2 층 색 보정 보간
        private ElementType _gradeElement;
        private Color _filterFrom = Color.white, _filterTo = Color.white, _filter = Color.white;
        private float _satFrom, _satTo, _sat;
        private float _expFrom, _expTo, _exp;
        private float _gradeT = 1f;

        // V3
        private PlayerHealth _localHealth;
        private NetworkIdentity _localIdentity;
        private float _downedBlend;

        private VisualFxConfig Config => config != null ? config : _runtimeDefaults;

        private void Awake()
        {
            if (config == null)
            {
                _runtimeDefaults = ScriptableObject.CreateInstance<VisualFxConfig>(); // 에셋이 없으면 코드 기본값
            }
            else
            {
                VisualFx.Config = config;
            }

            _volume = GetComponent<Volume>();
            _profile = _volume.sharedProfile != null ? Instantiate(_volume.sharedProfile) : ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "PostFx_Runtime";
            _volume.profile = _profile;
            _bloom = GetOrAdd<Bloom>();
            _color = GetOrAdd<ColorAdjustments>();
            _vignette = GetOrAdd<Vignette>();

            VisualFxConfig c = Config;
            _bloom.active = true;
            _bloom.intensity.Override(c.bloomIntensity);
            _bloom.threshold.Override(Mathf.Max(1f, c.bloomThreshold));
            _bloom.scatter.Override(c.bloomScatter);
            _color.active = true;
            _color.colorFilter.Override(Color.white);
            _color.saturation.Override(0f);
            _color.postExposure.Override(0f);
            _vignette.active = true;
            _vignette.color.Override(c.vignetteColor);
            _vignette.intensity.Override(c.vignetteIntensity);
            _vignette.smoothness.Override(c.vignetteSmoothness);

            _volume.enabled = VisualFx.Enabled;

            // 2단계 전역 조명: 씬 값(1)은 그대로 두고 코드로만 낮춘다 — 꺼짐이면 globalLightOffIntensity로 원복.
            foreach (Light2D light in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            {
                if (light.lightType == Light2D.LightType.Global)
                {
                    _globalLight = light;
                    break;
                }
            }

            ApplyGlobalLight(VisualFx.Enabled);
        }

        private void ApplyGlobalLight(bool on)
        {
            if (_globalLight != null)
            {
                VisualFxConfig c = Config;
                _globalLight.intensity = on ? c.globalLightIntensity : c.globalLightOffIntensity;
            }
        }

        private T GetOrAdd<T>() where T : VolumeComponent
        {
            return _profile.TryGet(out T component) ? component : _profile.Add<T>(true);
        }

        private void OnEnable()
        {
            VisualFx.EnabledChanged += OnEnabledChanged;
        }

        private void OnDisable()
        {
            VisualFx.EnabledChanged -= OnEnabledChanged;
        }

        private void OnDestroy()
        {
            if (config != null && VisualFx.Config == config)
            {
                VisualFx.Config = null;
            }

            if (_profile != null)
            {
                Destroy(_profile);
            }

            if (_runtimeDefaults != null)
            {
                Destroy(_runtimeDefaults);
            }
        }

        private void OnEnabledChanged(bool on)
        {
            if (_volume != null)
            {
                _volume.enabled = on;
            }

            ApplyGlobalLight(on);
        }

        private void Update()
        {
            if (_profile == null || !VisualFx.Enabled)
            {
                return;
            }

            VisualFxConfig c = Config;
            float dt = Time.unscaledDeltaTime;
            UpdateFloorGrade(c, dt);
            PlayerHealth health = LocalHealth();
            UpdateDowned(c, health, dt);
            UpdateVignette(c, health);
        }

        // ── V2 층 속성 색 보정 ─────────────────────────────────────

        private void UpdateFloorGrade(VisualFxConfig c, float dt)
        {
            FloorProgression dungeon = FloorProgression.Instance;
            ElementType element = dungeon != null ? dungeon.CurrentFloorElement : ElementType.None;
            if (element != _gradeElement)
            {
                _gradeElement = element;
                VisualFxConfig.ElementGrade grade = c.GradeFor(element);
                _filterFrom = _filter;
                _satFrom = _sat;
                _expFrom = _exp;
                _filterTo = Color.Lerp(Color.white, grade.colorFilter, c.gradeStrength);
                _satTo = grade.saturation * c.gradeStrength;
                _expTo = grade.postExposure * c.gradeStrength;
                _gradeT = 0f;
            }

            if (_gradeT < 1f)
            {
                _gradeT = c.floorBlendTime > 0f ? Mathf.Min(1f, _gradeT + dt / c.floorBlendTime) : 1f;
                float e = Mathf.SmoothStep(0f, 1f, _gradeT);
                _filter = Color.Lerp(_filterFrom, _filterTo, e);
                _sat = Mathf.Lerp(_satFrom, _satTo, e);
                _exp = Mathf.Lerp(_expFrom, _expTo, e);
            }

            _color.colorFilter.value = _filter;
            _color.postExposure.value = _exp;
        }

        // ── V3 쓰러짐 채도 · 위기 비네트 ──────────────────────────────

        private PlayerHealth LocalHealth()
        {
            NetworkIdentity identity = NetworkClient.localPlayer; // 관전 중에도 카메라 대상이 아닌 내 플레이어
            if (identity != _localIdentity)
            {
                _localIdentity = identity;
                _localHealth = identity != null ? identity.GetComponent<PlayerHealth>() : null;
            }

            return _localHealth;
        }

        private void UpdateDowned(VisualFxConfig c, PlayerHealth health, float dt)
        {
            bool downed = health != null && health.IsDowned;
            float step = c.downedBlendTime > 0f ? dt / c.downedBlendTime : 1f;
            _downedBlend = Mathf.MoveTowards(_downedBlend, downed ? 1f : 0f, step);
            _color.saturation.value = Mathf.Clamp(_sat + c.downedSaturation * _downedBlend, -100f, 100f); // 층 색 보정 위에 더한다
        }

        private void UpdateVignette(VisualFxConfig c, PlayerHealth health)
        {
            bool crisis = false;
            if (health != null && !health.IsDowned && !health.IsDead && health.MaxHealth > 0)
            {
                crisis = (float)health.CurrentHealth / health.MaxHealth < c.crisisHealthRatio;
            }

            if (crisis)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * c.crisisPulseSpeed);
                _vignette.color.value = c.crisisVignetteColor;
                _vignette.intensity.value = Mathf.Lerp(c.crisisIntensityMin, c.crisisIntensityMax, pulse);
            }
            else
            {
                _vignette.color.value = c.vignetteColor;
                _vignette.intensity.value = c.vignetteIntensity;
            }
        }
    }
}
