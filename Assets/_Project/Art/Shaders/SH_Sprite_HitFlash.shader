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
    //
    // Голограмма (_Holo): юнит обесцвечивается, теряет плотность и набирает светящийся контур со
    // скан-линиями — состояние «тело уже не совсем здесь» перед смертью. Живёт в ЭТОМ шейдере, а не в
    // отдельном материале, потому что материал уже стоит на теле и уже управляется property block'ом:
    // вторая копия ради одной фазы плодила бы ещё один владелец вида юнита.
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
        _Holo ("Hologram Amount", Range(0, 1)) = 0
        [HDR] _HoloColor ("Hologram Color", Color) = (0.3, 0.95, 1, 1)
        [HDR] _HoloRimColor ("Hologram Rim Color", Color) = (0.7, 1, 1, 1)
        _HoloAlpha ("Hologram Body Alpha", Range(0, 1)) = 0.45
        _HoloScanScale ("Hologram Scanline Scale (px)", Float) = 3
        _HoloScanAmount ("Hologram Scanline Strength", Range(0, 1)) = 0.35
        [HideInInspector] _HoloTexel ("Hologram Texel Size", Vector) = (0.01, 0.01, 0, 0)

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
                half4  _MainTex_ST;
                half4  _Color;
                half4  _Flip;
                half4  _FlashColor;
                half4  _HoloColor;
                half4  _HoloRimColor;
                half   _FlashAmount;
                half4  _HoloTexel;   // xy = размер текселя спрайта; подаёт UnitView
                half   _Holo;
                half   _HoloAlpha;
                half   _HoloScanScale;
                half   _HoloScanAmount;
            CBUFFER_END

            // Голограмма: обесцветить → залить холодным цветом → контур по краю силуэта → скан-линии.
            // Контур считается по альфе соседних текселей: там, где рядом пустота, — граница тела.
            half4 ApplyHologram(half4 col, float2 uv)
            {
                half grey = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 body = _HoloColor.rgb * (0.35h + grey * 0.75h);

                // Шаг в один тексель приходит СНАРУЖИ, а не из _MainTex_TexelSize: авто-переменная Unity
                // внутри UnityPerMaterial ломает 2D SRP Batcher для всего материала (он ругается на
                // _TexelSize/_ST в буфере), а материал этот стоит на каждом юните.
                float2 px = _HoloTexel.xy;
                half neighbours = min(
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( px.x, 0)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-px.x, 0)).a),
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  px.y)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -px.y)).a));
                half rim = saturate(col.a - neighbours);

                // Скан-линии в ПИКСЕЛЯХ спрайта, не экрана: иначе полосы живут своей жизнью при зуме камеры.
                float rowPx = uv.y / max(px.y, 1e-6);
                half  scan  = step(0.5h, frac(rowPx / max(_HoloScanScale, 1.0h)));
                body *= 1.0h - _HoloScanAmount * scan;

                half3 holoRgb = lerp(body, _HoloRimColor.rgb, rim);
                half  holoA   = col.a * lerp(_HoloAlpha, 1.0h, rim);

                col.rgb = lerp(col.rgb, holoRgb, _Holo);
                col.a   = lerp(col.a,   holoA,   _Holo);
                return col;
            }

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
                // Голограмма идёт ПЕРЕД вспышкой: белый пересвет — следующая стадия смерти и должен
                // забивать её собой, а не смешиваться с ней.
                if (_Holo > 0.001h) mainTex = ApplyHologram(mainTex, i.uv);
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
                half4  _MainTex_ST;
                half4  _Color;
                half4  _Flip;
                half4  _FlashColor;
                half4  _HoloColor;
                half4  _HoloRimColor;
                half   _FlashAmount;
                half4  _HoloTexel;   // xy = размер текселя спрайта; подаёт UnitView
                half   _Holo;
                half   _HoloAlpha;
                half   _HoloScanScale;
                half   _HoloScanAmount;
            CBUFFER_END

            // Голограмма: обесцветить → залить холодным цветом → контур по краю силуэта → скан-линии.
            // Контур считается по альфе соседних текселей: там, где рядом пустота, — граница тела.
            half4 ApplyHologram(half4 col, float2 uv)
            {
                half grey = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 body = _HoloColor.rgb * (0.35h + grey * 0.75h);

                // Шаг в один тексель приходит СНАРУЖИ, а не из _MainTex_TexelSize: авто-переменная Unity
                // внутри UnityPerMaterial ломает 2D SRP Batcher для всего материала (он ругается на
                // _TexelSize/_ST в буфере), а материал этот стоит на каждом юните.
                float2 px = _HoloTexel.xy;
                half neighbours = min(
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( px.x, 0)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-px.x, 0)).a),
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  px.y)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -px.y)).a));
                half rim = saturate(col.a - neighbours);

                // Скан-линии в ПИКСЕЛЯХ спрайта, не экрана: иначе полосы живут своей жизнью при зуме камеры.
                float rowPx = uv.y / max(px.y, 1e-6);
                half  scan  = step(0.5h, frac(rowPx / max(_HoloScanScale, 1.0h)));
                body *= 1.0h - _HoloScanAmount * scan;

                half3 holoRgb = lerp(body, _HoloRimColor.rgb, rim);
                half  holoA   = col.a * lerp(_HoloAlpha, 1.0h, rim);

                col.rgb = lerp(col.rgb, holoRgb, _Holo);
                col.a   = lerp(col.a,   holoA,   _Holo);
                return col;
            }

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
