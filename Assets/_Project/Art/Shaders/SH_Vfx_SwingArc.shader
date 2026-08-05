Shader "Guildmaster/Vfx/SwingArc"
{
    // ДУГА ЗА КЛИНКОМ — первая стадия удара: сектор с центром в плече, заметающий угол, который клинок
    // УЖЕ прошёл. Живёт только на strike-фазе взмаха и гаснет сразу после неё, поэтому в постоянное
    // свечение не превращается. Рисуется всегда, когда клинок махнул: и на попадании, и на промахе, и на
    // холостом замахе — дуга говорит «клинок прошёл здесь», и это правда в любом исходе.
    //
    // Не путать с формой удара (SH_Vfx_HitForm): та появляется ПОСЛЕ контакта и заявляет «удар состоялся».
    // Две стадии в разное время — потому и не спорят между собой за внимание.
    //
    // Хвост теряет ПРОЗРАЧНОСТЬ, а не уходит во второй цвет: свет один, просто его меньше там, где клинок
    // побывал раньше.
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.8, 0.4, 1)
        [HDR] _CoreColor ("Core Colour (пересвет в середине следа)", Color) = (1, 1, 1, 1)

        _AngleFrom ("Angle From (rad)", Float) = 0
        _AngleTo   ("Angle To (rad)", Float) = 1.2

        _RadiusInner ("Inner Radius (доли полу-quad)", Range(0, 1)) = 0.35
        _RadiusOuter ("Outer Radius (доли полу-quad)", Range(0, 1)) = 0.95

        _Fade     ("Fade (общая сила)", Range(0, 1)) = 1
        _TailBias ("Tail Bias (насколько быстро гаснет хвост)", Range(0.2, 4)) = 1.6

        // СТУПЕНИ ПОПЕРЁК: доли полутолщины следа. Наружу от ядра идёт цвет, за ним — чёрная кромка,
        // которая толщину СЪЕДАЕТ ИЗНУТРИ, а не нарастает снаружи: габарит дуги от неё не меняется.
        _CoreShare   ("Core Share (доля толщины под пересвет)", Range(0, 1)) = 0.34
        _ColourShare ("Colour Share (доля толщины под цвет)", Range(0, 1)) = 0.74

        // Сколько альфы даёт кромка. Ноль — прежнее чисто аддитивное поведение: тёмного нет вовсе,
        // след ни на что не ложится. Это и есть выключатель приёма.
        _Opaque ("Opaque (сила перекрытия кромкой)", Range(0, 1)) = 1

        // ПРОФИЛЬ ШИРИНЫ ВДОЛЬ: полумесяц вместо ровного кольца.
        _ProfileOn   ("Profile On (0 — ровное кольцо)", Range(0, 1)) = 1
        _TailSharp   ("Tail Sharpness (меньше — резче сужение у хвоста)", Range(0.15, 2)) = 0.55
        _ProfilePeak ("Profile Peak (нормировка, считается снаружи)", Float) = 0.69

        // Ширина перехода между ступенями, в долях полутолщины. Ноль — ступени встык, и границы
        // читаются как три вложенные наклейки; выше — свет перетекает в цвет, а цвет в кромку.
        _Softness ("Softness (мягкость переходов между ступенями)", Range(0, 0.6)) = 0.28

        // РВАНОСТЬ: неровность краёв следа. Клинок не оставляет ровной ленты — след живой и дышит.
        _Rough ("Roughness (рванность краёв)", Range(0, 1)) = 0.35
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

        // PREMULTIPLIED ALPHA — как у формы удара, и по той же причине: чёрная кромка ПЕРЕКРЫВАЕТ то,
        // что под ней, а сложение перекрывать не умеет. Свет по-прежнему уходит в rgb, поэтому при
        // _Opaque = 0 шейдер ведёт себя ровно как прежний Blend One One.
        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Lib/Procedural.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                half  _AngleFrom;
                half  _AngleTo;
                half  _RadiusInner;
                half  _RadiusOuter;
                half  _Fade;
                half  _TailBias;
                half  _CoreShare;
                half  _ColourShare;
                half  _Opaque;
                half  _ProfileOn;
                half  _TailSharp;
                half  _ProfilePeak;
                half  _Softness;
                half  _Rough;
            CBUFFER_END

            // Профиль ширины следа: t = 0 у хвоста, 1 у клинка.
            //
            // Показатель у хвоста МЕНЬШЕ единицы намеренно — тогда производная там максимальна, и
            // сужение ускоряется к самому концу. Множитель (1 - 0.45 t³) поджимает передний край, но
            // не сводит его в ноль: свет обязан доходить до клинка, иначе взмах выглядит недоигранным.
            //
            // Нормировка приходит снаружи (_ProfilePeak): считать максимум профиля в пикселе значило
            // бы гонять цикл на каждом фрагменте ради числа, которое меняется раз в жизни материала.
            float TrailProfile(float t)
            {
                float raw = pow(max(t, 1e-4), _TailSharp) * (1.0 - 0.45 * t * t * t);
                return saturate(raw / max(_ProfilePeak, 1e-4));
            }

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;     // центр quad = плечо
                float r = length(p);
                if (r < 1e-4) return 0;

                // Грубая отсечка по кольцу: дальше внешнего радиуса и ближе внутреннего следа нет ни
                // при каком профиле, и считать там нечего.
                float edge = max(fwidth(r) * 1.5, 1e-4);
                if (r > _RadiusOuter + edge || r < _RadiusInner - edge) return 0;

                // Угловая маска: от начала взмаха до текущего положения клинка. Углы приходят снаружи
                // РАЗВЁРНУТЫМИ (без скачка через ±пи), поэтому дугу можно мерить простым отношением.
                float a = atan2(p.y, p.x);
                float span = _AngleTo - _AngleFrom;
                float absSpan = abs(span);
                if (absSpan < 1e-4) return 0;

                // Ближайшее представление угла пикселя к началу дуги: без этого сектор, перешедший через
                // ±пи, распадался бы надвое.
                float k = 6.2831853;
                float rel = a - _AngleFrom;
                rel -= k * floor((rel + 3.14159265) / k);
                float t = rel / span;                       // 0 — начало взмаха, 1 — текущий клинок
                if (t < 0.0 || t > 1.0) return 0;

                // Мягкие торцы сектора: жёсткий край читается как вырезанный кусок пирога.
                float angEdge = saturate(0.06 / absSpan);
                float ends = smoothstep(0.0, angEdge, t) * (1.0 - smoothstep(1.0 - angEdge, 1.0, t) * 0.15);

                // Хвост: чем дальше от текущего положения клинка, тем прозрачнее.
                float tail = pow(saturate(t), _TailBias);

                // ШИРИНА СЛЕДА в этой точке. Ровное кольцо (_ProfileOn = 0) — прежнее поведение;
                // профиль превращает его в полумесяц, схлопывая след к линии кончика у хвоста.
                float full   = max(_RadiusOuter - _RadiusInner, 1e-4);
                float width  = lerp(full, TrailProfile(t) * full, saturate(_ProfileOn));

                // РВАНОСТЬ. Шум идёт по УГЛУ и по сиду взмаха: клинок не оставляет ровной ленты. Двумя
                // потоками — толщина и смещение осевой линии, — потому что одна только толщина даёт
                // симметричное «дыхание», а не рваный край.
                //
                // Сид взят из угла начала взмаха: он постоянен всю жизнь эффекта (значит след не кипит
                // покадрово) и разный у соседних ударов. Отдельного сида дуге не заводим — своего
                // события у неё нет, а IRngService показу не принадлежит.
                float seed  = _AngleFrom * 3.7;
                float jag   = GM_ValueNoise11(t * 5.3 + seed);
                float drift = GM_ValueNoise11(t * 3.1 + seed + 11.0);
                width *= 1.0 + _Rough * 0.38 * jag;

                float halfW  = max(width * 0.5, 1e-4);
                // Внешняя граница всегда на радиусе кончика: сужается след ВНУТРЬ, к плечу.
                float rMid   = _RadiusOuter - halfW + halfW * _Rough * 0.30 * drift;
                float dr     = abs(r - rMid) / halfW;         // 0 — середина следа, 1 — его кромка

                // Мягкость перехода между ступенями. Пиксельный минимум (fwidth) обязателен — на тонком
                // хвосте без него край зазубрится, — но САМ переход задаётся долей толщины: иначе три
                // ступени встают встык и читаются тремя вложенными наклейками, а не одним градиентом.
                float e    = max(fwidth(r) / halfW * 1.5, 1e-3);
                float soft = max(_Softness * 0.5, e);

                // Внешняя граница РЕЗКАЯ — она и есть лайн (Макс, 06.08.2026), мягкий лайн перестаёт
                // быть лайном. Мягкость живёт только на внутренних переходах: свет → цвет → кромка.
                float band   = 1.0 - smoothstep(1.0 - e, 1.0, dr);
                if (band <= 0.001) return 0;
                float colour = 1.0 - smoothstep(max(_ColourShare - soft, 0.0), _ColourShare + soft, dr);
                float core   = 1.0 - smoothstep(max(_CoreShare - soft, 0.0), _CoreShare + soft, dr);

                float live = ends * tail * saturate(_Fade);

                // Свет: цвет по краям, пересвет в середине — та же ось «ядро и кайма», что у формы.
                half3 rgb = _Color.rgb * (colour * live) + _CoreColor.rgb * (core * live);

                // Перекрытие даёт ВСЯ полоса, а свет занимает её внутреннюю часть — поэтому снаружи от
                // цвета остаётся чёрная кромка, и она же есть лайн. Отдельного слоя под это нет.
                float alpha = band * live * saturate(_Opaque);
                if (alpha <= 0.001 && colour <= 0.001) return 0;

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
