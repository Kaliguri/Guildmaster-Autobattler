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
        _BaseColor ("Основа (бумага)", Color) = (0.78, 0.71, 0.55, 1)
        _StainColor ("Разводы и потёртости", Color) = (0.62, 0.54, 0.39, 1)
        _EdgeColor ("Затёртый край", Color) = (0.42, 0.34, 0.22, 1)
        _WeaveTiling ("Тайлинг волокна", Float) = 2
        _GrainTiling ("Тайлинг зерна", Float) = 14
        _WeaveStrength ("Сила волокна", Range(0, 1)) = 0.55
        _GrainStrength ("Сила зерна", Range(0, 1)) = 0.12
        _Vignette ("Виньетка листа", Range(0, 1)) = 0.35
        _EdgeRagged ("Рваность края", Range(0, 0.2)) = 0.045
        _EdgeNoiseScale ("Частота рваности", Float) = 9
        _EdgeBurn ("Затёртость края", Range(0, 1)) = 0.7
        _AspectX ("Пропорция листа (ширина/высота)", Float) = 1
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
