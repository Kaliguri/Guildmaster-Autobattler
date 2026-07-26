Shader "Guildmaster/Arena/Digital"
{
    // Цифровой слой поверх арены: каркас «по содержимому» + тонировка + вспышка на клетках, которые
    // прямо сейчас меняют текстуру. Рисуется одним квадом в мировых координатах, поэтому накрывает разом
    // пол, стены и декор и не требует правок чужого шейдера тайлов (Cainos).
    //
    // Фазы НЕ пересчитываются здесь: моменты каждой клетки (когда уходит в каркас, когда переворачивается,
    // когда возвращается) приходят готовыми в карте клеток — их считает C# по тому же расписанию, что
    // подменяет тайлы. Считать их шейдером заново нельзя: HLSL и C# расходятся в младших разрядах, и
    // каркас поехал бы мимо клеток, которые меняются.
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)

        _CellMap ("Cell map (R=вид, G=в цифру, B=смена, A=обратно)", 2D) = "black" {}
        _MapRect ("Карта: xy = левый нижний угол в мире, zw = размер", Vector) = (0, 0, 1, 1)
        _Cells ("Клеток: x = по горизонтали, y = по вертикали", Vector) = (1, 1, 0, 0)
        _CellSize ("Размер клетки в мире", Float) = 1

        _Progress ("Ход перехода", Range(0, 1)) = 0
        _DigitizeBand ("Мягкость ухода в цифру", Range(0.005, 0.3)) = 0.05
        _SwitchBand ("Длина вспышки смены", Range(0.005, 0.3)) = 0.018
        _RestoreBand ("Мягкость возврата", Range(0.005, 0.3)) = 0.05

        // Бирюза (реш. Макса): мир уходит в холодный цифровой сумрак, контуры светятся. Графит до этого
        // давал не «цифру», а болото — зелень пола смешивалась с серым в грязь.
        [HDR] _WireColor ("Цвет контура", Color) = (0.30, 0.86, 0.92, 1)
        [HDR] _SparkColor ("Цвет вспышки", Color) = (0.78, 0.99, 1.0, 1)
        // Цвет заметно светлее, чем кажется правильным на глаз: в линейном пространстве тёмная бирюза
        // уезжает почти в чёрный, и от «цифры» остаётся просто потемневший мир.
        _InkColor ("Цвет затемнения", Color) = (0.06, 0.32, 0.38, 1)
        _InkAmount ("Сила затемнения", Range(0, 1)) = 0.82

        [Header(Scan)]
        _ScanAmount ("Сила скан-линий", Range(0, 0.4)) = 0.10
        _ScanFreq ("Частота скан-линий (на юнит мира)", Range(0.1, 4)) = 0.7
        _ScanSpeed ("Скорость скан-линий", Range(0, 3)) = 0.35
        _CellFlicker ("Разброс яркости по клеткам", Range(0, 0.3)) = 0.05

        _WireWidth ("Толщина контура (доля клетки)", Range(0.01, 0.3)) = 0.09

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
            #pragma vertex DigitalVertex
            #pragma fragment DigitalFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                float2 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_CellMap);  SAMPLER(sampler_CellMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                float4 _MapRect;
                float4 _Cells;
                float  _CellSize;
                float  _Progress;
                float  _DigitizeBand;
                float  _SwitchBand;
                float  _RestoreBand;
                half4  _WireColor;
                half4  _SparkColor;
                half4  _InkColor;
                half   _InkAmount;
                half   _WireWidth;
                half   _ScanAmount;
                half   _ScanFreq;
                half   _ScanSpeed;
                half   _CellFlicker;
            CBUFFER_END

            // Что в клетке ПРЯМО СЕЙЧАС: 0 — пусто, 1 — пол, 2 — стена. До подмены тайла отвечает исходный
            // облик, после — целевой, поэтому контур переезжает вместе с содержимым. За краем карты — пусто,
            // иначе крайняя клетка размазалась бы наружу и арена получила бы фальшивую кайму.
            half StateAt(float2 cell)
            {
                float2 uv = (cell + 0.5) / max(_Cells.xy, 1.0);
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0h;

                half4 i = SAMPLE_TEXTURE2D(_CellMap, sampler_CellMap, uv);
                half code = round(i.r * 8.0h);
                half from = floor(code / 3.0h);
                half to   = code - from * 3.0h;
                return lerp(from, to, step(i.b, _Progress));
            }

            Varyings DigitalVertex(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS.xy;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 DigitalFragment(Varyings IN) : SV_Target
            {
                // Клетка под пикселем и положение внутри неё. Сетка мировая, поэтому каркас лежит
                // ровно на тайлах при любом положении камеры и любом зуме.
                float2 grid = (IN.positionWS - _MapRect.xy) / _CellSize;
                float2 cell = floor(grid);
                float2 local = grid - cell;

                float2 mapUv = (cell + 0.5) / max(_Cells.xy, 1.0);
                clip(min(min(mapUv.x, mapUv.y), min(1.0 - mapUv.x, 1.0 - mapUv.y)));

                half4 info = SAMPLE_TEXTURE2D(_CellMap, sampler_CellMap, mapUv);
                half tDigitize = info.g;
                half tSwitch   = info.b;
                half tRestore  = info.a;

                // Насколько клетка сейчас в цифре: поднялась в каркас и ещё не вернулась.
                half up   = smoothstep(tDigitize - _DigitizeBand, tDigitize + _DigitizeBand, _Progress);
                half down = smoothstep(tRestore  - _RestoreBand,  tRestore  + _RestoreBand,  _Progress);
                half digital = saturate(up - down);
                clip(digital - 0.002);

                half state = StateAt(cell);
                clip(state - 0.5h);              // за ареной пустота — там рисовать нечего

                // Обводка идёт по ГРАНИЦАМ содержимого, а не по клеткам: линия загорается там, где стена
                // встречает пол, а пол — пустоту. Это и есть узнаваемый контур места. Сетка по каждой клетке
                // была ошибкой: клетка — внутренняя мера перещёлка, игроку её видеть незачем, и на ровном
                // поле она превращает мир в миллиметровку.
                half sL = StateAt(cell + float2(-1.0,  0.0));
                half sR = StateAt(cell + float2( 1.0,  0.0));
                half sD = StateAt(cell + float2( 0.0, -1.0));
                half sU = StateAt(cell + float2( 0.0,  1.0));

                half nearL = 1.0h - smoothstep(0.0, _WireWidth, local.x);
                half nearR = 1.0h - smoothstep(0.0, _WireWidth, 1.0 - local.x);
                half nearD = 1.0h - smoothstep(0.0, _WireWidth, local.y);
                half nearU = 1.0h - smoothstep(0.0, _WireWidth, 1.0 - local.y);

                // Светится сторона той клетки, которая «выше» соседа: обводится объект, а не дырка рядом с ним.
                half outline = max(max(nearL * step(0.5h, state - sL), nearR * step(0.5h, state - sR)),
                                   max(nearD * step(0.5h, state - sD), nearU * step(0.5h, state - sU)));

                half wire = outline * digital;

                // Вспышка ровно в момент подмены тайла — она и «продаёт» смену текстуры.
                half flash = saturate(1.0h - abs(_Progress - tSwitch) / _SwitchBand);
                flash *= digital;

                // Скан-линии ползут по миру, а не по экрану: иначе при движении камеры они «прилипают»
                // к стеклу и мир перестаёт выглядеть тем, что оцифровано.
                half scan = 0.5h + 0.5h * sin((IN.positionWS.y * _ScanFreq - _Time.y * _ScanSpeed) * 6.2831853);

                // Клетки светятся чуть по-разному — поле читается как данные, а не как ровная заливка.
                half flicker = 1.0h - _CellFlicker * frac(tSwitch * 7.3h);

                half ink = (_InkAmount + _ScanAmount * scan) * digital * flicker;

                half3 col = _InkColor.rgb;
                half  a   = saturate(ink);

                col = lerp(col, _WireColor.rgb, saturate(wire));
                a   = saturate(a + wire);

                col = lerp(col, _SparkColor.rgb, saturate(flash));
                a   = saturate(a + flash * 0.9h);

                clip(a - 0.003h);
                return half4(col * _Color.rgb, a * _Color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
