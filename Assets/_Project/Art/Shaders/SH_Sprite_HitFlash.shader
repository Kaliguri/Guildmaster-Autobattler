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
        _HoloAlpha ("Hologram Body Alpha", Range(0, 1)) = 0.45
        _HoloScanScale ("Hologram Scanline Scale (px)", Float) = 3
        _HoloScanAmount ("Hologram Scanline Strength", Range(0, 1)) = 0.35
        [HideInInspector] _HoloTexel ("Hologram Texel Size", Vector) = (0.01, 0.01, 0, 0)

        _Outline ("Outline Amount", Range(0, 1)) = 0
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)

        // Свечение части (телеграф каста, реф SAO): оружие/конечность-источник наливается светом.
        // Emission ПОВЕРХ тела, HDR-цвет пробивает bloom. Пишется per-instance через MPB только в ту
        // часть, чья роль несёт приём (UnitView/Body), материал один на всех частях.
        _GlowAmount ("Glow Amount", Range(0, 1)) = 0
        [HDR] _GlowColor ("Glow Color", Color) = (1, 1, 1, 1)

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
                half   _FlashAmount;
                half4  _HoloTexel;   // xy = размер текселя спрайта; подаёт UnitView
                half4  _OutlineColor;
                half   _Holo;
                half   _HoloAlpha;
                half   _HoloScanScale;
                half   _HoloScanAmount;
                half   _Outline;
                half4  _GlowColor;
                half   _GlowAmount;
            CBUFFER_END

            /// Контур по краю силуэта: там, где рядом с пикселем пустота, — граница тела. Здесь он к месту
            /// (телеграф каста «сейчас будет»), в отличие от голограммы, где выделял кромку не по делу.
            half4 ApplyOutline(half4 col, float2 uv)
            {
                float2 px = _HoloTexel.xy;
                half neighbours = min(
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( px.x, 0)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-px.x, 0)).a),
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  px.y)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -px.y)).a));

                half rim = saturate(col.a - neighbours) * saturate(_Outline);
                col.rgb = lerp(col.rgb, _OutlineColor.rgb, rim);
                col.a   = max(col.a, rim);
                return col;
            }

            // Голограмма: обесцветить → залить холодным цветом → скан-линии. Контура по краю силуэта здесь
            // НЕТ намеренно: он выделял кромку отдельным шагом, и тело распадалось на «обводку и заливку»
            // вместо того, чтобы целиком стать светом (реш. Макса на play-QA).
            half4 ApplyHologram(half4 col, float2 uv)
            {
                half grey = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 body = _HoloColor.rgb * (0.35h + grey * 0.75h);

                // Скан-линии в ПИКСЕЛЯХ спрайта, не экрана: иначе полосы живут своей жизнью при зуме камеры.
                // Шаг текселя приходит снаружи (_HoloTexel): авто-переменная _MainTex_TexelSize в буфере
                // ломает 2D SRP Batcher для всего материала, а он стоит на каждом юните.
                float rowPx = uv.y / max(_HoloTexel.y, 1e-6);
                half  scan  = step(0.5h, frac(rowPx / max(_HoloScanScale, 1.0h)));
                body *= 1.0h - _HoloScanAmount * scan;

                col.rgb = lerp(col.rgb, body,               _Holo);
                col.a   = lerp(col.a,   col.a * _HoloAlpha, _Holo);
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
                // Контур каста — поверх тела, но под вспышкой удара.
                if (_Outline > 0.001h) mainTex = ApplyOutline(mainTex, i.uv);
                // Вспышка: заливаем rgb к _FlashColor по силе _FlashAmount (в чистый белый может).
                mainTex.rgb = lerp(mainTex.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                // Свечение части: emission поверх, только по телу (умножаем на alpha, иначе засветится
                // прозрачный квад). HDR _GlowColor уводит rgb за 1.0 — bloom подхватывает свет, не арт.
                mainTex.rgb += _GlowColor.rgb * (saturate(_GlowAmount) * mainTex.a);
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
                half   _FlashAmount;
                half4  _HoloTexel;   // xy = размер текселя спрайта; подаёт UnitView
                half4  _OutlineColor;
                half   _Holo;
                half   _HoloAlpha;
                half   _HoloScanScale;
                half   _HoloScanAmount;
                half   _Outline;
                half4  _GlowColor;
                half   _GlowAmount;
            CBUFFER_END

            /// Контур по краю силуэта: там, где рядом с пикселем пустота, — граница тела. Здесь он к месту
            /// (телеграф каста «сейчас будет»), в отличие от голограммы, где выделял кромку не по делу.
            half4 ApplyOutline(half4 col, float2 uv)
            {
                float2 px = _HoloTexel.xy;
                half neighbours = min(
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( px.x, 0)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-px.x, 0)).a),
                    min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  px.y)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -px.y)).a));

                half rim = saturate(col.a - neighbours) * saturate(_Outline);
                col.rgb = lerp(col.rgb, _OutlineColor.rgb, rim);
                col.a   = max(col.a, rim);
                return col;
            }

            // Голограмма: обесцветить → залить холодным цветом → скан-линии. Контура по краю силуэта здесь
            // НЕТ намеренно: он выделял кромку отдельным шагом, и тело распадалось на «обводку и заливку»
            // вместо того, чтобы целиком стать светом (реш. Макса на play-QA).
            half4 ApplyHologram(half4 col, float2 uv)
            {
                half grey = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 body = _HoloColor.rgb * (0.35h + grey * 0.75h);

                // Скан-линии в ПИКСЕЛЯХ спрайта, не экрана: иначе полосы живут своей жизнью при зуме камеры.
                // Шаг текселя приходит снаружи (_HoloTexel): авто-переменная _MainTex_TexelSize в буфере
                // ломает 2D SRP Batcher для всего материала, а он стоит на каждом юните.
                float rowPx = uv.y / max(_HoloTexel.y, 1e-6);
                half  scan  = step(0.5h, frac(rowPx / max(_HoloScanScale, 1.0h)));
                body *= 1.0h - _HoloScanAmount * scan;

                col.rgb = lerp(col.rgb, body,               _Holo);
                col.a   = lerp(col.a,   col.a * _HoloAlpha, _Holo);
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
                // Свечение части: emission поверх, только по телу (умножаем на alpha, иначе засветится
                // прозрачный квад). HDR _GlowColor уводит rgb за 1.0 — bloom подхватывает свет, не арт.
                mainTex.rgb += _GlowColor.rgb * (saturate(_GlowAmount) * mainTex.a);
                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
