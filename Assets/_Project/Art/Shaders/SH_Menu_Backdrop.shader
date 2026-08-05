Shader "Guildmaster/UI/MenuBackdrop"
{
    // ЗАДНИК ЭКРАНОВ МЕТЫ: настройки, пауза, главное меню. Не стол из-под карты — у меты свой
    // регистр («тёмное стекло», см. tokens.semantic.uss), и тёплое дерево гроссбуха туда не едет.
    //
    // ЯРКОСТЬ ТЕКСТУРЫ ЗДЕСЬ — НЕ ЦВЕТ, А КООРДИНАТА. Картинка приходит в градациях серого и
    // работает картой высот света: её значение выбирает точку на рампе из трёх ступеней. Это
    // принципиально иначе, чем тинт умножением: умножение даёт ОДИН тон разной светлоты, а рампа
    // разводит концы — тень уходит в почти чёрный с холодком, свет в патину. Ровно так устроен
    // референс (Guildrun): промер 05.08.2026 дал тень #020A0C при свете #1B4B3F, и одним
    // умножением такую пару не получить.
    //
    // Отсюда же следствие: перекрасить всю мету — это поменять три цвета в материале, а не
    // перерисовать текстуру.
    Properties
    {
        _MainTex ("Карта света (серая)", 2D) = "gray" {}

        // Три ступени рампы. Значения по умолчанию — наши токены патины: 950 / 900 / 600.
        _ShadowColor ("Тень (низ рампы)", Color) = (0.004, 0.047, 0.055, 1)
        _MidColor ("Середина", Color) = (0.024, 0.255, 0.278, 1)
        _LightColor ("Свет (верх рампы)", Color) = (0.133, 0.475, 0.506, 1)

        // Где на шкале сидит средняя ступень. Двигает баланс тени и света, не трогая концы:
        // ниже 0.5 — кадр светлеет, выше — темнеет.
        _MidPoint ("Точка середины", Range(0.05, 0.95)) = 0.5

        // УРОВНИ ВХОДА. Исходник почти никогда не занимает полный диапазон: у `Background ver 5`
        // это 5..169 из 255, то есть без растяжки верхняя треть рампы не была бы задействована
        // вовсе, а светлые грани не дотянули бы до своего цвета.
        _InputBlack ("Вход: чёрная точка", Range(0, 1)) = 0.02
        _InputWhite ("Вход: белая точка", Range(0, 1)) = 0.66
        _Gamma ("Гамма отклика", Range(0.3, 3)) = 1

        _Vignette ("Виньетка (тёмный край)", Range(0, 1)) = 0.35
        _VignetteSoftness ("Мягкость виньетки", Range(0.2, 2)) = 0.9

        // Пропорция кадра, которую шлёт MenuBackdropView. Текстура вписывается по принципу
        // «покрыть»: на сверхшироком мониторе она обрежется, но не растянется — растянутый
        // диагональный луч ломает угол, и это видно сразу.
        _AspectX ("Пропорция кадра (ширина/высота)", Float) = 1.7778
        _TexAspect ("Пропорция картинки", Float) = 1.7778
    }

    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _ShadowColor;
            half4 _MidColor;
            half4 _LightColor;
            half _MidPoint;
            half _InputBlack;
            half _InputWhite;
            half _Gamma;
            half _Vignette;
            half _VignetteSoftness;
            float _AspectX;
            float _TexAspect;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.uv = input.uv;
            return o;
        }

        // «Покрыть кадр»: сжимаем ту ось, по которой картинка шире нужного, и режем излишек —
        // вместо растяжения. Центр остаётся центром.
        float2 CoverUv(float2 uv)
        {
            float scale = _AspectX / max(_TexAspect, 0.0001);
            float2 c = uv - 0.5;
            if (scale > 1)  c.y /= scale;   // кадр шире картинки — обрезаем по высоте
            else            c.x *= scale;   // кадр уже картинки — обрезаем по ширине
            return c + 0.5;
        }

        half3 Ramp(half t)
        {
            half mid = max(_MidPoint, 0.0001);
            half3 low  = lerp(_ShadowColor.rgb, _MidColor.rgb, saturate(t / mid));
            half3 high = lerp(_MidColor.rgb, _LightColor.rgb, saturate((t - mid) / max(1 - mid, 0.0001)));
            return t < mid ? low : high;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            float2 uv = CoverUv(TRANSFORM_TEX(input.uv, _MainTex));
            half src = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;

            // Уровни: растягиваем занятый диапазон исходника на всю рампу, потом гамма.
            half t = saturate((src - _InputBlack) / max(_InputWhite - _InputBlack, 0.0001));
            t = pow(t, _Gamma);

            half3 col = Ramp(t);

            // Виньетка живёт ЗДЕСЬ по той же причине, что и у стола: UI рисуется поверх
            // пост-обработки, а тёмный край нужен именно ПОД интерфейсом.
            float2 d = (input.uv - 0.5) * float2(_AspectX, 1);
            half edge = saturate(length(d) / max(_VignetteSoftness, 0.0001));
            col *= 1 - _Vignette * edge * edge;

            return half4(col, 1);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
