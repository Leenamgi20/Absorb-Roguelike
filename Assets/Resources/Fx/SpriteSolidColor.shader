// 스프라이트를 한 가지 색(기본 흰색)으로 칠하는 셰이더(2026-10-07, 명중 번쩍임용). 알파는 스프라이트 그대로, RGB는 _Color × 정점 색.
// Resources 폴더에 있어 빌드에 포함되고, HitWhiteFlash가 Resources.Load로 불러 머티리얼을 만든다. URP 2D에서도 SRPDefaultUnlit로 그려진다.
Shader "Absorb/SpriteSolidColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a * i.color.a * _Color.a;
                return fixed4(_Color.rgb * i.color.rgb, a);
            }
            ENDCG
        }
    }
}
