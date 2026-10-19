// 1px 외곽선 스프라이트(2026-10-15, 비주얼 퀄리티 3단계 V8). URP 2D Sprite-Lit-Default를 그대로 따르고(Universal2D 패스 = 2D 조명,
// NormalsRendering, 비조명 UniversalForward) 프래그먼트에서 외곽선만 더한다.
// - 자기 텍셀 알파가 0이고 상하좌우 _OutlineThickness 텍셀(정수, 1 = 32PPU 도트 1px) 안에 불투명 텍셀이 있으면 외곽선 텍셀로 칠한다.
//   스프라이트 여백(타이트 메시 extrude 1px) 안에서만 그린다 — 메시를 키우지 않는다. 텍셀 기준이라 늘이기·기울기(SlimeBodyAnimator)에도 도트와 같이 늘어난다.
// - 외곽선 색 = _OutlineColor(알파 포함). 정점색(SpriteRenderer.color)의 RGB는 곱하지 않는다 → 정예 주황 틴트·시체/쓰러짐 어둡게가
//   외곽선 색을 바꾸지 않는다(정점 알파만 곱해 페이드는 따라간다). 2D 조명 영향은 _OutlineLightInfluence(0 = 조명 무시, 1 = 몸과 같이)로 줄인다.
// - 몸 텍셀은 Sprite-Lit-Default와 같은 결과(정점색 곱 + 2D 조명).
// VisualFx.ApplyOutline이 Resources.Load로 불러 공유 재질(플레이어 흰색·정예 주황)을 만든다(docs/04_구현위키/비주얼_효과.md 6절).
Shader "Absorb/SpriteOutline"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineThickness("Outline Thickness (texels)", Range(1, 4)) = 1
        _OutlineLightInfluence("Outline Light Influence", Range(0, 1)) = 0.25

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
        // 실제로 묶인 스프라이트(아틀라스) 텍스처의 텍셀 크기(uv). _MainTex_TexelSize는 SpriteRenderer가 프로퍼티 블록으로 넣는
        // 텍스처와 어긋날 수 있어(재질 기본 white 텍스처 기준) GPU에서 직접 읽는다.
        #define OUTLINE_TEXEL_UV(tex, outTexel) { uint _ow, _oh; tex.GetDimensions(_ow, _oh); outTexel = 1.0 / float2(max(_ow, 1u), max(_oh, 1u)); }

        // 이 텍셀이 외곽선인지(자기 알파 0 + 상하좌우 thickness 텍셀 안에 불투명). 1 = 외곽선.
        #define OUTLINE_MASK(tex, samp, uv, selfAlpha, thickness, outMask)                                       \
        {                                                                                                         \
            float2 _texel; OUTLINE_TEXEL_UV(tex, _texel);                                                         \
            half _near = 0;                                                                                       \
            int _steps = clamp((int)round(thickness), 1, 4);                                                      \
            [unroll] for (int _i = 1; _i <= 4; _i++)                                                              \
            {                                                                                                     \
                if (_i <= _steps)                                                                                 \
                {                                                                                                 \
                    float2 _d = _texel * _i;                                                                      \
                    _near = max(_near, SAMPLE_TEXTURE2D_LOD(tex, samp, uv + float2(_d.x, 0), 0).a);                      \
                    _near = max(_near, SAMPLE_TEXTURE2D_LOD(tex, samp, uv - float2(_d.x, 0), 0).a);                      \
                    _near = max(_near, SAMPLE_TEXTURE2D_LOD(tex, samp, uv + float2(0, _d.y), 0).a);                      \
                    _near = max(_near, SAMPLE_TEXTURE2D_LOD(tex, samp, uv - float2(0, _d.y), 0).a);                      \
                }                                                                                                 \
            }                                                                                                     \
            outMask = (selfAlpha <= 0.0 && _near > 0.0) ? 1.0 : 0.0;                                              \
        }
    ENDHLSL

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color        : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineThickness;
                float _OutlineLightInfluence;
            CBUFFER_END

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half selfAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half isOutline;
                OUTLINE_MASK(_MainTex, sampler_MainTex, input.uv, selfAlpha, _OutlineThickness, isOutline);
                if (isOutline < 0.5)
                {
                    return CommonLitFragment(input, input.color); // 몸: Sprite-Lit-Default와 같음
                }

                // 외곽선: 정점색 RGB는 곱하지 않고(틴트 무관) 알파만 곱한다. 조명은 _OutlineLightInfluence만큼만.
                half4 outline = half4(_OutlineColor.rgb, _OutlineColor.a * input.color.a);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                const half3 normalTS = half3(0, 0, 1);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(outline.rgb, outline.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                return half4(lerp(outline.rgb, lit.rgb, _OutlineLightInfluence), outline.a);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineThickness;
                float _OutlineLightInfluence;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

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
                half4 _OutlineColor;
                float _OutlineThickness;
                float _OutlineLightInfluence;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half selfAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half isOutline;
                OUTLINE_MASK(_MainTex, sampler_MainTex, input.uv, selfAlpha, _OutlineThickness, isOutline);
                if (isOutline < 0.5)
                {
                    return CommonUnlitFragment(input, input.color);
                }

                return half4(_OutlineColor.rgb, _OutlineColor.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
