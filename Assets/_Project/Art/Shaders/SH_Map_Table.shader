Shader "Guildmaster/Map/Table"
{
    // Поверхность ПОД листом карты: то, что видно за рваными краями. Ничего не рисуется руками —
    // тайл берётся из Kenney pattern-pack, пятно света из Kenney light-masks, цвет задаётся здесь.
    // Читается как «карта лежит на чём-то тёмном, и на неё падает свет»; буквальным столом быть
    // не обязано.
    //
    // Непрозрачный и самый дальний по Z: именно он закрывает пустоту, которую иначе камера заливает
    // своим цветом очистки (ровно та «синева», из-за которой заводился лист).
    Properties
    {
        _MainTex ("Тайл поверхности", 2D) = "black" {}
        _LightMask ("Маска света", 2D) = "white" {}
        _BaseColor ("Основа (тёмное сукно)", Color) = (0.055, 0.045, 0.038, 1)
        _PatternColor ("Цвет рисунка", Color) = (0.105, 0.086, 0.070, 1)
        _LightColor ("Цвет света (тёплый)", Color) = (1.0, 0.88, 0.68, 1)
        _PatternTiling ("Тайлинг рисунка", Float) = 6
        _PatternStrength ("Сила рисунка", Range(0, 1)) = 0.5
        _LightStrength ("Сила света", Range(0, 3)) = 1.6
        _Ambient ("Подсвет вне пятна", Range(0, 1)) = 0.25
        _AspectX ("Пропорция стола (ширина/высота)", Float) = 1
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

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapTableCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapTableCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
