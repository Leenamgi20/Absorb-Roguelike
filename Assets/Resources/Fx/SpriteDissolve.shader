// 디졸브 스프라이트(2026-10-15, 비주얼 퀄리티 3단계 V9). 몬스터가 사라질 때 DissolveGhost(로컬 잔상)가 이 재질로 _Progress 0→1을 재생한다.
// URP 2D Unlit — 블렌딩·정렬 태그는 SpriteGlow와 같다. 노이즈는 텍스처 없이 '텍셀 좌표를 floor한 정수 셀'의 해시라
// 셀 1칸 = 스프라이트 텍셀 1칸 = 32PPU 도트 1칸 → 도트 단위로 블록지게 부서진다.
// noise < 진행도면 버리고, 진행도 ~ 진행도+_EdgeWidth 구간은 _EdgeColor × _EdgeIntensity(1 초과 → 블룸)로 칠한다. 원본 알파 0 텍셀은 그대로 버린다.
// Resources 폴더라 빌드에 포함된다(docs/04_구현위키/비주얼_효과.md 6절).
Shader "Absorb/SpriteDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Progress ("Progress (0 = 온전, 1 = 다 사라짐)", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width (노이즈 구간)", Range(0, 0.5)) = 0.12
        [HDR] _EdgeColor ("Edge Color", Color) = (1, 1, 1, 1)
        _EdgeIntensity ("Edge Intensity (HDR, 1 초과 → 블룸)", Float) = 2
        _PixelsPerUnit ("Pixels Per Unit (참조: 셀 = 텍셀 1칸 = 이 PPU의 도트 1칸)", Float) = 32
        _Seed ("Seed", Float) = 0

        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex DissolveVertex
            #pragma fragment DissolveFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Progress;
                float _EdgeWidth;
                half4 _EdgeColor;
                float _EdgeIntensity;
                float _PixelsPerUnit;
                float _Seed;
            CBUFFER_END

            // 정수 셀 해시(0~1). 텍스처 없이 셀마다 고정된 값.
            float CellHash(float2 cell)
            {
                float3 p3 = frac(float3(cell.xyx) * 0.1031 + _Seed * 0.0173);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 실제로 묶인 스프라이트(아틀라스) 텍스처 크기. _MainTex_TexelSize는 SpriteRenderer가 프로퍼티 블록으로 넣는 텍스처와
            // 어긋날 수 있어(재질 기본 white 텍스처 크기) GPU에서 직접 읽는다.
            float2 TextureSize()
            {
                uint w, h;
                _MainTex.GetDimensions(w, h);
                return float2(w, h);
            }

            Varyings DissolveVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 DissolveFragment(Varyings input) : SV_Target
            {
                half4 c = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(c.a - 0.001); // 원본 알파 0 텍셀은 버린다

                float2 cell = floor(input.uv * TextureSize()); // 텍셀 1칸 = 도트 1칸
                float noise = CellHash(cell);

                // 진행도를 -EdgeWidth ~ 1로 펴서 0일 때 가장자리 없이 온전, 1일 때 전부 사라지게 한다.
                float progress = _Progress * (1.0 + _EdgeWidth) - _EdgeWidth;
                clip(noise - progress);

                if (noise < progress + _EdgeWidth)
                {
                    c.rgb = _EdgeColor.rgb * _EdgeIntensity;
                }

                return c;
            }
            ENDHLSL
        }
    }
}
