Shader "Guildmaster/Vfx/HitForm"
{
    // ФОРМА УДАРА — главный знак попадания: серп, веретено, звезда дробящего, линия-всполох выстрела.
    // Рисуется ЦЕЛИКОМ здесь, на одном quad: спрайтовых листов у нас нет и не будет (канон
    // gdd/70-gamefeel/vfx-language §«Форму делает шейдер, а не спрайт»). Пиксельная текстура эффекта
    // конкурирует с пиксельным юнитом за детализацию и проигрывает; свет живёт в другом измерении —
    // в яркости, — и с артом не спорит.
    //
    // ЛОКАЛЬНОЕ ПРОСТРАНСТВО: quad натянут на форму так, что UV.x идёт вдоль оси A→B (0 — начало,
    // 1 — конец), UV.y — поперёк. Всё ниже считается в координатах p = (x∈[-1..1], y∈[-1..1]),
    // поэтому длина, поворот и место формы в мире — забота трансформа, а не шейдера.
    //
    // АРХЕТИП — ПРАВИЛО ГЕНЕРАЦИИ, А НЕ ГОТОВЫЙ ЗНАК (HARD, канон): каждый удар рисует свой серп.
    // Гуляют прогиб, толщина в коридоре, неровность краёв и лучи звезды; РАЗМЕР НЕ ГУЛЯЕТ НИКОГДА —
    // он несёт вес удара, и случайность в нём отняла бы у игрока единственный канал силы.
    Properties
    {
        [HDR] _CoreColor ("Core Colour (пересвет ядра)", Color) = (1, 1, 1, 1)
        [HDR] _RimColor  ("Rim Colour (кайма — элемент)", Color) = (1, 0.6, 0.2, 1)

        // Полудлина формы в долях полу-quad. Не всегда единица: у дробящего звезда шире собственного
        // следа, и quad растянут под неё — иначе лучи обрезались бы краем меша.
        _Len       ("Length Share (полудлина формы в долях полу-quad)", Range(0.05, 1)) = 1

        _Arc       ("Arc (прогиб, доли полудлины)", Range(-0.6, 0.6)) = 0.24
        _HalfThick ("Half Thickness (полутолщина в середине)", Range(0.005, 0.5)) = 0.09
        _CoreWidth ("Core Width (доля толщины под ядро)", Range(0, 1)) = 0.55
        _Rough     ("Roughness (неровность краёв)", Range(0, 1)) = 0.25
        _Seed      ("Seed (вариация)", Float) = 0

        // 0 — режущий (серп), 1 — колющий (веретено), 2 — дробящий (короткий след + звезда),
        // 3 — линия-всполох выстрела. Больше архетипов не будет: элемент приходит цветом, не формой.
        _Kind ("Kind", Float) = 0

        _StarRadius ("Star Radius (доли полу-quad)", Range(0, 1)) = 0.5
        _StarRays   ("Star Rays", Range(3, 12)) = 8

        // 0..1 жизнь формы. Прорастание от A и общее угасание — оба отсюда: показ ведёт прогресс
        // сам (и умеет замереть на hitstop), шейдер только рисует состояние.
        _Progress ("Progress", Range(0, 1)) = 0
        _Grow     ("Grow Share (за какую долю жизни дорастает)", Range(0.05, 1)) = 0.35

        // Хвост идёт СЛЕДОМ за головой: форма не «появляется и тает», а прочерчивается — росчерк
        // уходит вперёд по вектору удара и схлопывается за собой. Отставание задано долей жизни.
        _TailLag ("Tail Lag (отставание хвоста, доля жизни)", Range(0.05, 1)) = 0.45
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

        // Аддитив: форма — это СВЕТ. Альфа-блендинг дал бы плоскую наклейку поверх тела, а сложение
        // даёт пересвет на пересечении с артом и уходит за 1.0, где его подхватывает bloom (порог 1.0).
        Blend One One
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
                half4 _CoreColor;
                half4 _RimColor;
                half  _Len;
                half  _Arc;
                half  _HalfThick;
                half  _CoreWidth;
                half  _Rough;
                half  _Seed;
                half  _Kind;
                half  _StarRadius;
                half  _StarRays;
                half  _Progress;
                half  _Grow;
                half  _TailLag;
            CBUFFER_END

            // Шум — вторая ступень детерминизма (code-standards §8): неровность краёв обязана быть
            // ОДИНАКОВОЙ у всех клиентов кооператива, поэтому никакого шума по времени — только по
            // координате и сиду, который приходит снаружи из IRngService. Формулы — GM_* из Lib.

            // Профиль полутолщины вдоль формы: максимум в середине, ноль на обоих остриях. Показатель
            // ниже единицы даёт лист с заострёнными концами, а не полосу постоянной ширины — именно это
            // отличает росчерк от ленты.
            float ThicknessAt(float x)
            {
                float body = saturate(1.0 - x * x);
                float t = pow(body, 0.62);
                // Неровность края: слабая волна по длине. Форма живёт 4-5 кадров, поэтому рисунок должен
                // читаться сразу — частота низкая, амплитуда в четверть толщины.
                t *= 1.0 + _Rough * 0.25 * GM_ValueNoise11(x * 3.7 + _Seed * 17.13);
                return max(t, 0.0);
            }

            // Звезда дробящего: лучи неравной длины, рвущиеся от центра как стекло. Длина каждого луча
            // берётся из сида, поэтому две булавы подряд дают разные трещины при одном правиле.
            float Star(float2 p, float radius, float rays, float seed)
            {
                float r = length(p);
                if (r > radius * 1.6) return 0.0;

                float a = atan2(p.y, p.x);
                float k = 6.2831853 / max(rays, 3.0);
                float idx = floor((a + 3.14159265) / k);
                float frac_a = frac((a + 3.14159265) / k) - 0.5;

                float len  = radius * lerp(0.55, 1.0, GM_Hash11(idx * 3.11 + seed));
                float half_w = 0.09 * radius * lerp(0.6, 1.4, GM_Hash11(idx * 7.77 + seed));

                // Луч сходит на нет к своему концу: у основания толстый, на острие исчезает.
                float along = saturate(1.0 - r / max(len, 1e-4));
                float w = half_w * along;
                float across = abs(frac_a) * k * r;      // расстояние поперёк луча

                float ray = 1.0 - smoothstep(0.0, max(w, 1e-4), across);
                return ray * along;
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
                // UV → [-1..1] по обеим осям: центр quad = середина формы.
                float2 q = i.uv * 2.0 - 1.0;
                // Координаты САМОЙ ФОРМЫ: её полудлина может быть меньше полу-quad (см. _Len).
                float2 p = q / max(_Len, 1e-4);

                // РОСЧЕРК. Форма не появляется целиком и не тает на месте: голова уходит вперёд по вектору
                // удара, хвост идёт следом с отставанием и схлопывает её за собой. Видно ровно то, что
                // между ними, — поэтому удар читается как быстрое движение «оттуда сюда».
                float head = lerp(-1.0, 1.0, saturate(_Progress / max(_Grow, 1e-4)));
                float tail = lerp(-1.0, 1.0, saturate((_Progress - _TailLag) / max(_Grow, 1e-4)));

                float visible = (1.0 - smoothstep(head, head + 0.28, p.x))   // впереди головы пусто
                              * smoothstep(tail - 0.34, tail, p.x);           // позади хвоста тоже

                // Общее угасание — только к самому концу жизни: основную работу делает хвост, а не фейд.
                float fade = 1.0 - smoothstep(0.75, 1.0, _Progress);

                float mask = 0.0;
                float coreMask = 0.0;

                {
                    // Серп, веретено, короткий след дробящего и линия-всполох — одно правило с разными
                    // числами: центральная линия с прогибом плюс профиль толщины. У линии прогиб просто
                    // ноль, поэтому отдельной ветки ей не нужно. Прогиб задан параметром, а НЕ траекторией
                    // клипа: клипы правятся, а контракт «две точки» переживает перерисовку анимации.
                    float centre = _Arc * (1.0 - p.x * p.x);
                    float t = _HalfThick * ThicknessAt(p.x);
                    float d = abs(p.y - centre);

                    // Мягкий край в полтексела ширины формы: гладкость здесь принципиальна — эффект
                    // намеренно НЕ пиксельный, он живёт в другом измерении, чем арт.
                    float edge = max(fwidth(p.y) * 1.5, 1e-4);
                    mask = 1.0 - smoothstep(t - edge, t + edge, d);

                    // Ядро — белый пересвет во внутренней доле толщины. Красить его элементом нельзя:
                    // самое яркое место перестало бы быть самым ярким, и оба цвета потухли бы.
                    float coreT = t * _CoreWidth;
                    coreMask = (1.0 - smoothstep(coreT - edge, coreT + edge, d)) * mask;
                }

                if (_Kind > 1.5 && _Kind < 2.5)
                {
                    // Дробящий двухчастен: слабый след ДО точки контакта плюс звезда В ней. Точка хита у
                    // него КОНЕЧНАЯ — булава там и остаётся, в отличие от клинка, проходящего навылет.
                    // Звезда живёт в координатах QUAD, а не формы: её радиус и задаёт размер меша.
                    float star = Star(float2(q.x - _Len, q.y), _StarRadius, _StarRays, _Seed);
                    mask     = max(mask * 0.65, star);
                    coreMask = max(coreMask * 0.65, star * star);
                }

                float alpha = mask * visible * fade;
                if (alpha <= 0.001) return 0;

                // Кайма несёт элемент, ядро — пересвет. Так на форме, живущей четыре кадра, оба цвета
                // видны одновременно в одном месте (способ смешения «ядро и кайма», принят 31.07).
                half3 colour = _RimColor.rgb * alpha + _CoreColor.rgb * coreMask * visible * fade;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
