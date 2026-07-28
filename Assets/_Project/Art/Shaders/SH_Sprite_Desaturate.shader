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

        // Поклеточный переход цвета: клетки меняются вразнобой по той же карте, что и подмена текстур.
        // Без этого смена цвета — мгновенный щелчок, и длинному акту перехода нечем себя занять.
        [Toggle] _UseCellMap ("Идти по карте клеток", Float) = 0
        _CellMap ("Cell map (B = момент клетки)", 2D) = "black" {}
        _MapRect ("Карта: xy = угол в мире, zw = размер", Vector) = (0, 0, 1, 1)
        _Cells ("Клеток по осям", Vector) = (1, 1, 0, 0)
        _CellSize ("Размер клетки в мире", Float) = 1
        _Progress ("Ход перехода", Range(0, 1)) = 0
        _ToGrey ("Направление: 1 — в серое, 0 — в цвет", Range(0, 1)) = 1

        // Проявление: пиксель не рисуется, пока его клетка не перещёлкнулась. Нужно декору (трава, камни) —
        // он живёт отдельными спрайтами вне тайлмапа, и без этого стоял готовым, пока пол ещё собирается.
        [Toggle] _Reveal ("Проявляться по клеткам", Float) = 0
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
                float2 positionWS : TEXCOORD1;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_CellMap); SAMPLER(sampler_CellMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Desaturate;
                half4  _GrayTint;
                half   _Brightness;
                half   _Contrast;
                half   _UseCellMap;
                float4 _MapRect;
                float4 _Cells;
                float  _CellSize;
                float  _Progress;
                half   _ToGrey;
                half   _Reveal;
            CBUFFER_END

            Varyings DesatVertex(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS.xy;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _Color;
                return OUT;
            }

            half4 DesatFragment(Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                // Сколько обесцвечивания в ЭТОЙ точке: либо общее значение, либо по клетке — тогда пиксель
                // ждёт СВОЙ момент из карты, и поле перекрашивается вразнобой, а не одним щелчком.
                half amount = _Desaturate;
                if (_UseCellMap > 0.5h)
                {
                    float2 cell = floor((IN.positionWS - _MapRect.xy) / _CellSize);
                    float2 mapUv = (cell + 0.5) / max(_Cells.xy, 1.0);
                    half tSwitch = SAMPLE_TEXTURE2D(_CellMap, sampler_CellMap, saturate(mapUv)).b;
                    half passed = step(tSwitch, _Progress);
                    amount = lerp(1.0h - passed, passed, _ToGrey);

                    // Пока клетка под пикселем не перещёлкнулась — его тут ещё нет.
                    if (_Reveal > 0.5h) clip(passed - 0.5h);
                }

                half lum = dot(c.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 grey = lum * _GrayTint.rgb;
                grey = (grey - 0.5h) * _Contrast + 0.5h;
                grey *= _Brightness;

                c.rgb = lerp(c.rgb, saturate(grey), amount);
                return c;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
