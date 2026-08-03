Shader "Guildmaster/Map/Fog"
{
    // Туман над непройденной частью акта — ЧИСТО АТМОСФЕРА: он ничего не скрывает и ни на что не влияет,
    // узлы под ним видны и кликаются. Смысл в том, что впереди акт «затянут», а за отрядом дымка разошлась.
    //
    // Два слоя шума ползут в разные стороны с разной скоростью — так облака не выглядят одной картинкой,
    // которую двигают. Плотность гасится по мировому X относительно фронта развеивания (_RevealX): там,
    // где отряд уже прошёл, тумана почти нет.
    //
    // Растворение — ДИЗЕРИНГОМ по матрице Байера, а не гладким альфа-градиентом. Гладкая дымка поверх
    // пиксельных иконок читается как фильтр из другой игры; точечный растр — как часть той же картинки.
    Properties
    {
        _MainTex ("Шум (grayscale)", 2D) = "gray" {}
        _FogColor ("Цвет тумана", Color) = (0.62, 0.60, 0.55, 1)
        _Density ("Плотность", Range(0, 1)) = 0.5
        _Scale1 ("Масштаб слоя 1", Float) = 0.05
        _Scale2 ("Масштаб слоя 2", Float) = 0.11
        _Speed1 ("Скорость слоя 1", Vector) = (0.012, 0.004, 0, 0)
        _Speed2 ("Скорость слоя 2", Vector) = (-0.008, 0.006, 0, 0)
        _RevealX ("Фронт развеивания (мировой X)", Float) = 0
        _Falloff ("Ширина перехода", Float) = 14
        _Trail ("Заход за отряд", Float) = 6
        _Dither ("Сила дизеринга", Range(0, 1)) = 1
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
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapFogCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapFogCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
