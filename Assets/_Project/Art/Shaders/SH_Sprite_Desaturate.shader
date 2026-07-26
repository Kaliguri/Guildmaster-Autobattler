Shader "Guildmaster/Sprite/Desaturate"
{
    // Обесцвечивание спрайтов и тайлмап на месте: полигон должен быть серой версией ТОЙ ЖЕ локации,
    // а не отдельным набором серых тайлов. Материал подменяется на время тест-зоны, тайлы не трогаются —
    // значит приём работает с любым тайлсетом и не требует ручного дубля под каждую новую арену.
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)

        _Desaturate ("Обесцвечивание", Range(0, 1)) = 1
        _GrayTint ("Оттенок серого", Color) = (0.92, 0.94, 1.0, 1)
        _Brightness ("Яркость", Range(0.2, 1.5)) = 0.82
        _Contrast ("Контраст", Range(0.2, 2)) = 0.92
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex DesatVertex
            #pragma fragment DesatFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Desaturate;
                half4  _GrayTint;
                half   _Brightness;
                half   _Contrast;
            CBUFFER_END

            Varyings DesatVertex(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _Color;
                return OUT;
            }

            half4 DesatFragment(Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                half lum = dot(c.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 grey = lum * _GrayTint.rgb;
                grey = (grey - 0.5h) * _Contrast + 0.5h;
                grey *= _Brightness;

                c.rgb = lerp(c.rgb, saturate(grey), _Desaturate);
                return c;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
