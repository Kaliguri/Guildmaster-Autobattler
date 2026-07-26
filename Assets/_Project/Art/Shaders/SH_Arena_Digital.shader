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
        _SwitchBand ("Длина вспышки смены", Range(0.005, 0.3)) = 0.035
        _RestoreBand ("Мягкость возврата", Range(0.005, 0.3)) = 0.05

        [HDR] _WireColor ("Цвет каркаса", Color) = (0.79, 0.64, 0.29, 1)
        [HDR] _SparkColor ("Цвет вспышки", Color) = (0.95, 0.84, 0.61, 1)
        _InkColor ("Цвет затемнения", Color) = (0.055, 0.043, 0.031, 1)
        _InkAmount ("Сила затемнения", Range(0, 1)) = 0.6

        _WireWidth ("Толщина линии (доля клетки)", Range(0.01, 0.3)) = 0.06
        _FloorWire ("Яркость каркаса пола", Range(0, 1)) = 0.34
        _WallFill ("Заливка стен", Range(0, 0.6)) = 0.12

        // Тест-зона живёт в цифре постоянно: вспышки гасим, дыхание включаем, контраст мягче.
        _Calm ("Спокойный режим", Range(0, 1)) = 0
        _BreathAmount ("Глубина дыхания", Range(0, 0.5)) = 0.15
        _BreathSpeed ("Скорость дыхания", Range(0, 4)) = 1.1
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
                half   _FloorWire;
                half   _WallFill;
                half   _Calm;
                half   _BreathAmount;
                half   _BreathSpeed;
            CBUFFER_END

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
                digital = max(digital, _Calm);   // в спокойном режиме цифра держится всегда
                clip(digital - 0.002);

                // Дыхание — только для спокойного режима: статичная сетка на минуты просмотра мертвеет.
                half breath = 1.0h - _Calm * _BreathAmount *
                              (0.5h - 0.5h * sin(_Time.y * _BreathSpeed + tSwitch * 12.0h));

                // Каркас: расстояние до края клетки. Стена берёт полную яркость и перечёркивается —
                // именно по этому контуры исходной арены и читаются, ровная сетка их теряет.
                float2 edge = min(local, 1.0 - local);
                float  dist = min(edge.x, edge.y);
                half   frame = 1.0h - smoothstep(_WireWidth * 0.5, _WireWidth, dist);

                // Состояние клетки упаковано парой (что было / что станет), по три значения на каждое:
                // пусто, пол, стена. До подмены тайла каркас очерчивает арену, ИЗ которой уходим —
                // её контуры и должны узнаваться, — а после переворота уже новую.
                half code      = round(info.r * 8.0h);
                half fromState = floor(code / 3.0h);
                half toState   = code - fromState * 3.0h;
                half state     = lerp(fromState, toState, step(tSwitch, _Progress));

                clip(state - 0.5h);              // пустой клетке рисовать нечего: за ареной пустота, не чертёж
                half isWall = step(1.5h, state);

                half diag   = abs(local.x - (1.0 - local.y));
                half cross  = isWall * (1.0h - smoothstep(_WireWidth, _WireWidth * 2.2, diag));

                // Пол держит только угловые засечки, стена — сплошную рамку с перечёркиванием. Ровная сетка
                // по всему полю читается как миллиметровка и съедает контуры, ради которых всё затевалось.
                float  cornerDist = max(edge.x, edge.y);
                half   corners    = 1.0h - smoothstep(0.16, 0.28, cornerDist);
                half   floorWire  = frame * corners * _FloorWire;

                half wire = saturate(lerp(floorWire, frame, isWall) + cross * 0.8h);
                wire *= digital * breath;

                // Вспышка ровно в момент подмены тайла — она и «продаёт» смену текстуры.
                half flash = saturate(1.0h - abs(_Progress - tSwitch) / _SwitchBand);
                flash *= digital * (1.0h - _Calm);

                half ink = _InkAmount * digital * breath + _WallFill * isWall * digital;

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
