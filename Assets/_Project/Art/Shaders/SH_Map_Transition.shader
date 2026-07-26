Shader "Guildmaster/Map/Transition"
{
    // Переходная шторка карты: при выборе узла кадр затягивает чернилами, а не отряд едет по дорожке.
    //
    // Закрытие идёт ПО ТЕКСТУРЕ, а не ровной заливкой: шум задаёт, какие места темнеют раньше, поэтому
    // край расползается пятнами, как чернила по бумаге. Виньетка подмешана в порог — углы уходят первыми,
    // кадр схлопывается внутрь. Растворение дизерингом Байера, как у тумана карты: гладкий фейд поверх
    // пиксель-арта читается как чужой фильтр.
    Properties
    {
        _NoiseTex ("Шум закрытия (grayscale)", 2D) = "gray" {}
        _InkColor ("Цвет чернил", Color) = (0.055, 0.043, 0.031, 1)
        _Progress ("Закрытость", Range(0, 1)) = 0
        _Softness ("Мягкость края", Range(0.01, 1)) = 0.35
        _Scale ("Масштаб шума", Float) = 2
        _Vignette ("Схлопывание от углов", Range(0, 1)) = 0.6
        _Dither ("Сила дизеринга", Range(0, 1)) = 1
        _Center ("Точка схлопывания (uv)", Vector) = (0.5, 0.5, 0, 0)
        _Aspect ("Аспект кадра (ширина/высота)", Float) = 1.777
        _Dive ("Наезд узора к точке", Range(0, 0.6)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue"="Overlay"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapTransitionCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapTransitionCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
