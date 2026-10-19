// 발광 스프라이트(2026-10-15, 비주얼 퀄리티 1단계 V1). URP 2D Sprite-Unlit-Default와 같고, 출력 RGB에 _Intensity(HDR 배율)를 곱해
// 1을 넘긴다 → 블룸 문턱(1.0 이상)을 넘는 부분만 번진다. 정점색(SpriteRenderer.color)·텍스처 알파·뒤집기는 그대로.
// Resources 폴더라 빌드에 포함되고, VisualFx가 Resources.Load로 불러 이펙트 종류별 공유 재질을 만든다(docs/04_구현위키/비주얼_효과.md).
Shader "Absorb/SpriteGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Intensity ("Glow Intensity (HDR)", Float) = 1.5

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

            #pragma vertex GlowVertex
            #pragma fragment GlowFragment

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
                float _Intensity;
            CBUFFER_END

            Varyings GlowVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 GlowFragment(Varyings input) : SV_Target
            {
                float4 c = CommonUnlitFragment(input, input.color);
                c.rgb *= _Intensity;
                return c;
            }
            ENDHLSL
        }
    }
}
