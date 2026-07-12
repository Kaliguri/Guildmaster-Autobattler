Shader "Guildmaster/Sprite/HitFlash"
{
    // Unlit-спрайт для URP 2D Renderer с добавленной вспышкой попадания. Тинт (цвет персонажа)
    // идёт через vertex color и УМНОЖАЕТСЯ на текстуру, как в дефолтном спрайте. Вспышка —
    // отдельный параметр _FlashAmount: подмешивает _FlashColor ПОСЛЕ текстуры (не множитель),
    // поэтому «осветление в белый» реально работает (в отличие от SpriteRenderer.color).
    // _FlashAmount задаётся per-instance через MaterialPropertyBlock (UnitView) — материал один.
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half4  _FlashColor;
                half   _FlashAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _MainTex);
                output.color      = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                // Вспышка только по силуэту (умножаем на col.a) — прозрачные пиксели не белеют.
                col.rgb = lerp(col.rgb, _FlashColor.rgb, saturate(_FlashAmount) * col.a);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
