Shader "Guildmaster/Map/Backdrop"
{
    // Задник карты акта: полотно «старой бумаги» под узлами. Готовой текстуры пергамента в проекте нет,
    // поэтому фактура собирается из ОДНОГО grayscale-шума (Feel/MMNoise), взятого дважды с разным
    // тайлингом: крупно — волокно и разводы бумаги, мелко — зерно. Тон задаётся двумя цветами из
    // MapStyle, то есть теми же чернилами и пергаментом, что у остального интерфейса.
    //
    // Виньетка считается прямо здесь, по UV полотна, а не постобработкой: боевой Volume накрывает весь
    // кадр, и затемнять им карту значило бы задеть арену.
    Properties
    {
        _MainTex ("Шум (grayscale)", 2D) = "gray" {}
        _BaseColor ("Основа", Color) = (0.09, 0.08, 0.07, 1)
        _StainColor ("Разводы", Color) = (0.20, 0.17, 0.13, 1)
        _WeaveTiling ("Тайлинг волокна", Float) = 2
        _GrainTiling ("Тайлинг зерна", Float) = 14
        _WeaveStrength ("Сила волокна", Range(0, 1)) = 0.55
        _GrainStrength ("Сила зерна", Range(0, 1)) = 0.12
        _Vignette ("Виньетка", Range(0, 1)) = 0.45
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
            #include "MapBackdropCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapBackdropCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
