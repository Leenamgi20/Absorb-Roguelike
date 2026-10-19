using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AbsorbRoguelike.UI
{
    /// <summary>발광 재질을 쓰는 이펙트 종류(배율은 VisualFxConfig에서 종류별로).</summary>
    public enum GlowKind
    {
        HitSpark,
        Absorb,
        WeaponAura,
        PortalVortex,
        StatusFx,
    }

    /// <summary>외곽선 재질 종류(3단계 V8). 색은 VisualFxConfig에서.</summary>
    public enum OutlineKind
    {
        /// <summary>플레이어(1P·2P 공통 흰색). VisualFx.Enabled를 끄면 원래 재질로.</summary>
        Player,
        /// <summary>정예(주황). 정예 표식이라 토글과 무관하게 항상 외곽선.</summary>
        Elite,
    }

    /// <summary>
    /// 비주얼 효과 정적 진입점(2026-10-15, 비주얼 퀄리티 1단계 V1). 각 화면 로컬 표시 전용, 네트워크 코드 없음.
    /// - Enabled: 전체 켜기/끄기(디버그 메뉴 토글). 끄면 PostFx Volume이 꺼지고(VisualFxDirector) 이펙트 재질이 원래대로 돌아간다.
    /// - ApplyGlow: 이펙트 SpriteRenderer에 발광 재질(`Resources/Fx/SpriteGlow`, 종류별 공유 재질)을 입힌다. 처음 가진 재질을 기억해 두고
    ///   꺼지면 그 재질로 되돌린다. 로직·색·스프라이트는 건드리지 않고 재질만 바꾼다.
    /// - Config: VisualFxDirector가 주입한다. 없으면 하드코딩 기본 배율.
    /// - 2단계 조명(2026-10-15): AttachPointLight로 런타임 점광원(Light2D)을 붙이고 SetLight로 켜고 싶은 상태를 정한다. Enabled=false면 전부 끔.
    /// - 3단계 외곽선(2026-10-15, V8): ApplyOutline으로 몸 SpriteRenderer에 1px 외곽선 재질(`Resources/Fx/SpriteOutline`, 종류별 공유 재질 —
    ///   플레이어 흰색·정예 주황)을 입힌다. 플레이어는 Enabled를 따르고 정예는 항상 켠다. RemoveOutline은 원래 재질로 되돌리고 등록을 푼다.
    ///   잔상 디졸브(V9)는 DissolveGhost가 따로 맡는다.
    /// </summary>
    public static class VisualFx
    {
        private const float DefaultGlow = 1.5f;

        private struct Entry
        {
            public SpriteRenderer Renderer;
            public Material Original;
            public GlowKind Kind;
        }

        private static bool _enabled = true;
        private static VisualFxConfig _config;
        private static Shader _shader;
        private static bool _shaderLoaded;
        private static readonly Dictionary<GlowKind, Material> Materials = new Dictionary<GlowKind, Material>();
        private static readonly List<Entry> Registered = new List<Entry>();
        private static int _pruneAt = 128;

        /// <summary>Enabled가 바뀔 때(새 값).</summary>
        public static event Action<bool> EnabledChanged;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                RefreshRegistered();
                EnabledChanged?.Invoke(value);
            }
        }

        public static VisualFxConfig Config
        {
            get => _config;
            set
            {
                _config = value;
                foreach (KeyValuePair<GlowKind, Material> pair in Materials)
                {
                    pair.Value.SetFloat("_Intensity", GlowFor(pair.Key));
                }

                foreach (KeyValuePair<OutlineKind, Material> pair in OutlineMaterials)
                {
                    ConfigureOutline(pair.Value, pair.Key);
                }
            }
        }

        /// <summary>SpriteGlow 셰이더(Resources). 없으면 null — 그때 ApplyGlow는 아무것도 하지 않는다.</summary>
        public static Shader GlowShader
        {
            get
            {
                if (!_shaderLoaded)
                {
                    _shaderLoaded = true;
                    _shader = Resources.Load<Shader>("Fx/SpriteGlow");
                }

                return _shader;
            }
        }

        private static float GlowFor(GlowKind kind) => _config != null ? _config.GlowFor(kind) : DefaultGlow;

        private static Material GlowMaterial(GlowKind kind)
        {
            if (Materials.TryGetValue(kind, out Material material) && material != null)
            {
                return material;
            }

            if (GlowShader == null)
            {
                return null;
            }

            material = new Material(GlowShader) { name = "SpriteGlow_" + kind };
            material.SetFloat("_Intensity", GlowFor(kind));
            Materials[kind] = material;
            return material;
        }

        /// <summary>이펙트 렌더러에 발광 재질을 입힌다(꺼져 있으면 원래 재질 유지). 같은 렌더러에 여러 번 불러도 된다.</summary>
        public static void ApplyGlow(SpriteRenderer renderer, GlowKind kind)
        {
            if (renderer == null || GlowShader == null)
            {
                return;
            }

            int index = Registered.FindIndex(e => e.Renderer == renderer);
            if (index < 0)
            {
                if (Registered.Count >= _pruneAt)
                {
                    Registered.RemoveAll(e => e.Renderer == null); // 사라진 이펙트 정리
                    _pruneAt = Mathf.Max(128, Registered.Count * 2);
                }

                Registered.Add(new Entry { Renderer = renderer, Original = renderer.sharedMaterial, Kind = kind });
                index = Registered.Count - 1;
            }

            Apply(Registered[index]);
        }

        private static void Apply(Entry entry)
        {
            Material glow = _enabled ? GlowMaterial(entry.Kind) : null;
            entry.Renderer.sharedMaterial = glow != null ? glow : entry.Original;
        }

        private static void RefreshRegistered()
        {
            Registered.RemoveAll(e => e.Renderer == null);
            foreach (Entry entry in Registered)
            {
                Apply(entry);
            }

            RefreshLights();
            RefreshOutlines();
        }

        // ── 3단계 외곽선(2026-10-15, V8) ───────────────────────────────────────

        private struct OutlineEntry
        {
            public SpriteRenderer Renderer;
            public Material Original;
            public OutlineKind Kind;
        }

        private static Shader _outlineShader;
        private static bool _outlineShaderLoaded;
        private static readonly Dictionary<OutlineKind, Material> OutlineMaterials = new Dictionary<OutlineKind, Material>();
        private static readonly List<OutlineEntry> OutlineRegistered = new List<OutlineEntry>();
        private static int _outlinePruneAt = 64;

        /// <summary>SpriteOutline 셰이더(Resources). 없으면 null(에러 로그 1회) — 그때 ApplyOutline은 기존 재질을 그대로 둔다.</summary>
        public static Shader OutlineShader
        {
            get
            {
                if (!_outlineShaderLoaded)
                {
                    _outlineShaderLoaded = true;
                    _outlineShader = Resources.Load<Shader>("Fx/SpriteOutline");
                    if (_outlineShader == null)
                    {
                        Debug.LogError("[VisualFx] Resources/Fx/SpriteOutline 셰이더를 불러오지 못했다 — 외곽선 없이 원래 재질로 그린다.");
                    }
                }

                return _outlineShader;
            }
        }

        private static void ConfigureOutline(Material material, OutlineKind kind)
        {
            VisualFxConfig fx = Settings;
            Color color = kind == OutlineKind.Elite ? fx.eliteOutlineColor : fx.playerOutlineColor;
            color.a *= fx.outlineAlpha;
            material.SetColor("_OutlineColor", color);
            material.SetFloat("_OutlineThickness", Mathf.Max(1, fx.outlineThickness));
            material.SetFloat("_OutlineLightInfluence", fx.outlineLightInfluence);
        }

        private static Material OutlineMaterial(OutlineKind kind)
        {
            if (OutlineMaterials.TryGetValue(kind, out Material material) && material != null)
            {
                return material;
            }

            if (OutlineShader == null)
            {
                return null;
            }

            material = new Material(OutlineShader) { name = "SpriteOutline_" + kind };
            ConfigureOutline(material, kind);
            OutlineMaterials[kind] = material;
            return material;
        }

        /// <summary>
        /// 몸 렌더러에 외곽선 재질을 입힌다(처음 가진 재질을 기억). Player는 Enabled가 꺼지면 원래 재질, Elite는 항상 외곽선.
        /// 같은 렌더러에 여러 번 불러도 된다. 셰이더가 없으면 아무것도 바꾸지 않는다.
        /// </summary>
        public static void ApplyOutline(SpriteRenderer renderer, OutlineKind kind)
        {
            if (renderer == null || OutlineShader == null)
            {
                return;
            }

            int index = OutlineRegistered.FindIndex(e => e.Renderer == renderer);
            if (index < 0)
            {
                if (OutlineRegistered.Count >= _outlinePruneAt)
                {
                    OutlineRegistered.RemoveAll(e => e.Renderer == null);
                    _outlinePruneAt = Mathf.Max(64, OutlineRegistered.Count * 2);
                }

                OutlineRegistered.Add(new OutlineEntry { Renderer = renderer, Original = renderer.sharedMaterial, Kind = kind });
                index = OutlineRegistered.Count - 1;
            }
            else if (OutlineRegistered[index].Kind != kind)
            {
                OutlineEntry entry = OutlineRegistered[index];
                entry.Kind = kind;
                OutlineRegistered[index] = entry;
            }

            ApplyOutlineEntry(OutlineRegistered[index]);
        }

        /// <summary>외곽선을 떼고 처음 재질로 되돌린다(정예 시체 등). 등록돼 있지 않으면 아무것도 안 한다.</summary>
        public static void RemoveOutline(SpriteRenderer renderer)
        {
            int index = OutlineRegistered.FindIndex(e => e.Renderer == renderer);
            if (index < 0)
            {
                return;
            }

            OutlineEntry entry = OutlineRegistered[index];
            OutlineRegistered.RemoveAt(index);
            if (entry.Renderer != null)
            {
                entry.Renderer.sharedMaterial = entry.Original;
            }
        }

        private static void ApplyOutlineEntry(OutlineEntry entry)
        {
            bool on = entry.Kind == OutlineKind.Elite || _enabled;
            Material outline = on ? OutlineMaterial(entry.Kind) : null;
            entry.Renderer.sharedMaterial = outline != null ? outline : entry.Original;
        }

        private static void RefreshOutlines()
        {
            OutlineRegistered.RemoveAll(e => e.Renderer == null);
            foreach (OutlineEntry entry in OutlineRegistered)
            {
                ApplyOutlineEntry(entry);
            }
        }

        // ── 2단계 조명(2026-10-15) ─────────────────────────────────────────────

        private static VisualFxConfig _fallbackConfig;
        private static readonly Dictionary<Light2D, bool> Lights = new Dictionary<Light2D, bool>(); // 조명 → 켜고 싶은지
        private static readonly List<Light2D> LightScratch = new List<Light2D>();
        private static int _lightPruneAt = 64;

        /// <summary>Config, 없으면 코드 기본값 인스턴스(조명 부착 쪽이 null 검사 없이 수치를 읽게).</summary>
        public static VisualFxConfig Settings
        {
            get
            {
                if (_config != null)
                {
                    return _config;
                }

                if (_fallbackConfig == null)
                {
                    _fallbackConfig = ScriptableObject.CreateInstance<VisualFxConfig>();
                    _fallbackConfig.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallbackConfig;
            }
        }

        /// <summary>
        /// parent 자식으로 부드러운 점광원(Light2D Point, 그림자 끔, 모든 Sorting Layer 대상)을 붙인다. 각 화면 로컬(네트워크 오브젝트 아님).
        /// 등록해 두고 Enabled=false면 끄고, 다시 켜면 SetLight로 정한 상태로 되돌린다. 처음엔 켜고 싶은 상태.
        /// </summary>
        public static Light2D AttachPointLight(Transform parent, Color color, float radius, float intensity)
        {
            if (parent == null)
            {
                return null;
            }

            var go = new GameObject("FxLight");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.pointLightOuterRadius = Mathf.Max(0.01f, radius);
            light.pointLightInnerRadius = 0f;
            light.falloffIntensity = 0.7f; // 부드럽게
            light.shadowsEnabled = false;
            SortingLayer[] layers = SortingLayer.layers;
            var ids = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                ids[i] = layers[i].id;
            }

            light.targetSortingLayers = ids;

            if (Lights.Count >= _lightPruneAt)
            {
                PruneLights();
                _lightPruneAt = Mathf.Max(64, Lights.Count * 2);
            }

            Lights[light] = true;
            light.enabled = _enabled;
            return light;
        }

        /// <summary>조명을 켜고 싶은지 정한다(실제 enabled = on && Enabled). 매 프레임 불러도 된다.</summary>
        public static void SetLight(Light2D light, bool on)
        {
            if (light == null)
            {
                return;
            }

            Lights[light] = on;
            bool enabled = on && _enabled;
            if (light.enabled != enabled)
            {
                light.enabled = enabled;
            }
        }

        private static void PruneLights()
        {
            LightScratch.Clear();
            foreach (Light2D light in Lights.Keys)
            {
                if (light == null)
                {
                    LightScratch.Add(light);
                }
            }

            foreach (Light2D dead in LightScratch)
            {
                Lights.Remove(dead);
            }

            LightScratch.Clear();
        }

        private static void RefreshLights()
        {
            PruneLights();
            LightScratch.AddRange(Lights.Keys);
            foreach (Light2D light in LightScratch)
            {
                light.enabled = Lights[light] && _enabled;
            }

            LightScratch.Clear();
        }
    }
}
