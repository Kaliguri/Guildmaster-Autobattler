Shader "Guildmaster/Map/Icon"
{
    // Иконки узлов карты в ЕДИНОМ цветовом решении: исходный спрайт обесцвечивается и красится по рампе
    // тень→свет (gradient map). Так разномастные иконки Honeti (каждая в своих цветах) читаются как один
    // набор, нарисованный одной рукой, — «старинная карта» вместо цветной мозаики.
    //
    // Почему шейдером, а не перекраской файлов: тон карты — вопрос вкуса, его крутят десятки раз.
    // Перекраска ассетов означала бы перегенерацию всего набора на каждый поворот тона.
    //
    // Обвязка скопирована с SH_Sprite_HitFlash (он, в свою очередь, с официального URP 2D Sprite-Unlit):
    // _Flip, _MainTex_ST, инстансинг — это то, что SpriteRenderer прокидывает сам, и терять его нельзя.
    // Состояние узла (доступен/пройден/закрыт) приходит через SpriteRenderer.color = vertex color.
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)

        _ShadowColor ("Тень рампы", Color) = (0.14, 0.10, 0.07, 1)
        _LightColor  ("Свет рампы", Color) = (0.94, 0.89, 0.77, 1)
        _Recolor ("Сила перекраски", Range(0, 1)) = 1
        _Contrast ("Контраст рампы", Range(0.25, 4)) = 1

        // Спрайтовая обвязка — SpriteRenderer прокидывает per-renderer, руками не трогать.
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
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
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // --- Пасса для 2D Renderer ---
        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapIconCommon.hlsl"
            ENDHLSL
        }

        // --- Пасса для Forward-рендерера (если проект не на 2D Renderer) — тот же результат ---
        Pass
        {
            Tags { "LightMode"="UniversalForward" "Queue"="Transparent" "RenderType"="Transparent" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapIconCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
