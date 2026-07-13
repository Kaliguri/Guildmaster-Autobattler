Shader "Guildmaster/Sprite/HitFlash"
{
    // Точная копия официального URP 2D "Sprite-Unlit-Default" (SRP-batcher-совместимая редакция) + ОДНА
    // строка вспышки в фрагменте. Базироваться на официальном спрайт-шейдере критично: он несёт всю
    // per-renderer обвязку, которую SpriteRenderer прокидывает сам, — _Flip (flipX/flipY!), _MainTex_ST
    // (атлас/тайлинг), инстансинг. Наш прежний «голый» шейдер её не имел, из-за чего спрайт рисовался
    // криво (тинт/флип не подхватывались), пока на рендерере не появлялся MaterialPropertyBlock.
    //
    // Вспышка: _FlashAmount подмешивает _FlashColor ПОВЕРХ итогового цвета (не множитель) — «в белый»
    // реально заливает (в отличие от SpriteRenderer.color). Задаётся per-instance через MPB (UnitView),
    // материал один на всех.
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0

        // Спрайтовая обвязка — SpriteRenderer прокидывает per-renderer, руками не трогать.
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
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
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // --- Пасса для 2D Renderer ---
        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // SRP-batcher: единый layout, все per-material поля в одном CBUFFER, без #ifdef.
            CBUFFER_START(UnityPerMaterial)
                half4 _MainTex_ST;
                half4 _Color;
                half4 _Flip;
                half4 _FlashColor;
                half  _FlashAmount;
            CBUFFER_END

            float3 UnityFlipSprite(in float3 pos, in half2 flip)
            {
                return float3(pos.xy * flip, pos.z);
            }

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                v.positionOS = UnityFlipSprite(v.positionOS, _Flip.xy); // flipX/flipY спрайта
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
                o.color      = v.color * _Color;                        // vertex color = SpriteRenderer.color (тинт+альфа)
                return o;
            }

            half4 UnlitFragment(Varyings i) : SV_Target
            {
                half4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // Вспышка: заливаем rgb к _FlashColor по силе _FlashAmount (в чистый белый может).
                mainTex.rgb = lerp(mainTex.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                return mainTex;
            }
            ENDHLSL
        }

        // --- Пасса для Forward-рендерера (если проект не на 2D Renderer) — тот же результат ---
        Pass
        {
            Tags { "LightMode"="UniversalForward" "Queue"="Transparent" "RenderType"="Transparent" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _MainTex_ST;
                half4 _Color;
                half4 _Flip;
                half4 _FlashColor;
                half  _FlashAmount;
            CBUFFER_END

            float3 UnityFlipSprite(in float3 pos, in half2 flip)
            {
                return float3(pos.xy * flip, pos.z);
            }

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                v.positionOS = UnityFlipSprite(v.positionOS, _Flip.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
                o.color      = v.color * _Color;
                return o;
            }

            half4 UnlitFragment(Varyings i) : SV_Target
            {
                half4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                mainTex.rgb = lerp(mainTex.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
