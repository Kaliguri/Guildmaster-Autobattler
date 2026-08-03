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

        // Сколько свечения ложится РОВНО, не считаясь с артом: 1 = плоская заливка (тёмные пиксели
        // догоняют светлые, и клинок на пике превращается в однотонное пятно), 0 = свет строго по
        // яркости пикселя, форма и грани читаются целиком. Между ними — сколько силуэта отдаём свету.
        _GlowShapeKeep ("Glow Flatness", Range(0, 1)) = 0.35

        // Порезы: тело помнит бой. Каждое попадание оставляет светящуюся линию в ЛОКАЛЬНЫХ координатах
        // этой части, поэтому рана едет вместе с ней и не висит в воздухе там, где её нанесли. Красное у
        // нас — не брызги, а вскрытое (реф SAO): гладкий штрих, а не пиксельная царапина.
        // Три ОТДЕЛЬНЫХ вектора, а не массив: массив в property block выключает SRP Batcher для всего
        // материала, а он стоит на каждой части каждого юнита. Три раны на часть при лимите двенадцати на
        // тело — с запасом: порезы мелкие и расходятся по силуэту, а не копятся в одном предплечье.
        // Упаковка: xy — место в локальных координатах части, z — угол в радианах, w — длина.
        [HDR] _CutColor ("Cut Colour", Color) = (1.9, 0.18, 0.16, 1)
        _CutWidth ("Cut Half Width (локальные единицы)", Float) = 0.012
        _CutCount ("Cut Count", Float) = 0
        _Cut0 ("Cut 0", Vector) = (0, 0, 0, 0)
        _Cut1 ("Cut 1", Vector) = (0, 0, 0, 0)
        _Cut2 ("Cut 2", Vector) = (0, 0, 0, 0)
        _CutGlow ("Cut Brightness (xyz)", Vector) = (0, 0, 0, 0)

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
                float2 objectPos  : TEXCOORD1;   // положение в локальных координатах части — по нему живут порезы
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
                half   _GlowShapeKeep;
                half4  _CutColor;
                half4  _Cut0;
                half4  _Cut1;
                half4  _Cut2;
                half4  _CutGlow;
                half   _CutWidth;
                half   _CutCount;
            CBUFFER_END

            /// Расстояние от точки до отрезка пореза: место, угол и длина приходят одним вектором.
            float CutDistance(float2 p, half4 cut)
            {
                float2 dir = float2(cos(cut.z), sin(cut.z));
                float2 d   = p - cut.xy;
                // Проекция ограничена половиной длины в обе стороны — за концами меряем до самих концов,
                // поэтому у пореза скруглённые острия, а не обрубленные.
                float t = clamp(dot(d, dir), -cut.w * 0.5, cut.w * 0.5);
                return length(d - dir * t);
            }

            /// Порезы: светящиеся красные прорехи ПО ТЕЛУ. Умножаются на альфу спрайта, иначе рана
            /// висела бы в прозрачном углу квада рядом с силуэтом.
            half4 ApplyCuts(half4 col, float2 objectPos)
            {
                if (_CutCount < 0.5) return col;

                float glow = 0.0;
                float w = max(_CutWidth, 1e-5);

                // Мягкий профиль вместо порога: порез должен читаться светящимся штрихом, а не полоской
                // с жёстким краем — он рисуется в том же гладком стиле, что весь остальной джус.
                glow += _CutGlow.x * saturate(1.0 - CutDistance(objectPos, _Cut0) / w);
                if (_CutCount > 1.5) glow += _CutGlow.y * saturate(1.0 - CutDistance(objectPos, _Cut1) / w);
                if (_CutCount > 2.5) glow += _CutGlow.z * saturate(1.0 - CutDistance(objectPos, _Cut2) / w);

                glow = saturate(glow) * col.a;
                col.rgb = lerp(col.rgb, _CutColor.rgb, saturate(glow));
                return col;
            }

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
                o.objectPos  = v.positionOS.xy;   // уже с учётом flip — порез обязан ехать вместе со спрайтом
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
                // Порезы — часть САМОГО ТЕЛА, поэтому идут раньше контура и вспышки: и то, и другое —
                // события поверх тела, и заливать их раной было бы неверно.
                mainTex = ApplyCuts(mainTex, i.objectPos);
                // Контур каста — поверх тела, но под вспышкой удара.
                if (_Outline > 0.001h) mainTex = ApplyOutline(mainTex, i.uv);
                // Вспышка: заливаем rgb к _FlashColor по силе _FlashAmount (в чистый белый может).
                mainTex.rgb = lerp(mainTex.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                // Свечение части: emission поверх, только по телу (умножаем на alpha, иначе засветится
                // прозрачный квад). HDR _GlowColor уводит rgb за 1.0 — bloom подхватывает свет, не арт.
                //
                // Свет МОДУЛИРУЕТСЯ яркостью пикселя, а не заливает площадь ровно: ровная добавка
                // поднимает тёмные места так же, как светлые, внутренние контрасты схлопываются, и
                // светящийся клинок читается силуэтом без формы. _GlowShapeKeep задаёт, какую долю
                // отдаём ровному свету, остальное идёт по арту.
                half glowLum   = dot(mainTex.rgb, half3(0.299h, 0.587h, 0.114h));
                half glowShape = lerp(glowLum, 1.0h, saturate(_GlowShapeKeep));
                mainTex.rgb += _GlowColor.rgb * (saturate(_GlowAmount) * mainTex.a * glowShape);
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
                float2 objectPos  : TEXCOORD1;   // положение в локальных координатах части — по нему живут порезы
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
                half   _GlowShapeKeep;
                half4  _CutColor;
                half4  _Cut0;
                half4  _Cut1;
                half4  _Cut2;
                half4  _CutGlow;
                half   _CutWidth;
                half   _CutCount;
            CBUFFER_END

            /// Расстояние от точки до отрезка пореза: место, угол и длина приходят одним вектором.
            float CutDistance(float2 p, half4 cut)
            {
                float2 dir = float2(cos(cut.z), sin(cut.z));
                float2 d   = p - cut.xy;
                // Проекция ограничена половиной длины в обе стороны — за концами меряем до самих концов,
                // поэтому у пореза скруглённые острия, а не обрубленные.
                float t = clamp(dot(d, dir), -cut.w * 0.5, cut.w * 0.5);
                return length(d - dir * t);
            }

            /// Порезы: светящиеся красные прорехи ПО ТЕЛУ. Умножаются на альфу спрайта, иначе рана
            /// висела бы в прозрачном углу квада рядом с силуэтом.
            half4 ApplyCuts(half4 col, float2 objectPos)
            {
                if (_CutCount < 0.5) return col;

                float glow = 0.0;
                float w = max(_CutWidth, 1e-5);

                // Мягкий профиль вместо порога: порез должен читаться светящимся штрихом, а не полоской
                // с жёстким краем — он рисуется в том же гладком стиле, что весь остальной джус.
                glow += _CutGlow.x * saturate(1.0 - CutDistance(objectPos, _Cut0) / w);
                if (_CutCount > 1.5) glow += _CutGlow.y * saturate(1.0 - CutDistance(objectPos, _Cut1) / w);
                if (_CutCount > 2.5) glow += _CutGlow.z * saturate(1.0 - CutDistance(objectPos, _Cut2) / w);

                glow = saturate(glow) * col.a;
                col.rgb = lerp(col.rgb, _CutColor.rgb, saturate(glow));
                return col;
            }

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
                o.objectPos  = v.positionOS.xy;   // уже с учётом flip — порез обязан ехать вместе со спрайтом
                o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
                o.color      = v.color * _Color;
                return o;
            }

            half4 UnlitFragment(Varyings i) : SV_Target
            {
                half4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                mainTex = ApplyCuts(mainTex, i.objectPos);
                mainTex.rgb = lerp(mainTex.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                // Свечение части: emission поверх, только по телу (умножаем на alpha, иначе засветится
                // прозрачный квад). HDR _GlowColor уводит rgb за 1.0 — bloom подхватывает свет, не арт.
                //
                // Свет МОДУЛИРУЕТСЯ яркостью пикселя, а не заливает площадь ровно: ровная добавка
                // поднимает тёмные места так же, как светлые, внутренние контрасты схлопываются, и
                // светящийся клинок читается силуэтом без формы. _GlowShapeKeep задаёт, какую долю
                // отдаём ровному свету, остальное идёт по арту.
                half glowLum   = dot(mainTex.rgb, half3(0.299h, 0.587h, 0.114h));
                half glowShape = lerp(glowLum, 1.0h, saturate(_GlowShapeKeep));
                mainTex.rgb += _GlowColor.rgb * (saturate(_GlowAmount) * mainTex.a * glowShape);
                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
