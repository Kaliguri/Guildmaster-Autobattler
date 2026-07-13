Shader "Guildmaster/VFX/PixelBurst"
{
    // Пиксельный «брызг» частиц одним дро-коллом (hit-spark / muzzle / dust / heal). Общий меш — набор
    // квадов-«пикселей» (PixelBurstMesh) с запечёнными на частицу: угол разлёта (uv2.x), рандомы скорости/
    // жизни (color.r/g) и порог отсева по количеству (color.b). Vertex гонит каждый квад от центра по
    // направлению (в конусе _ConeHalf вокруг _BaseDir) на _Speed за жизнь _Life (0..1), с гравитацией;
    // размер пикселя — _PixelSize (мировые ед.). Пасс Universal2D — обязателен для Renderer2D.
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Life ("Life (0..1)", Range(0, 1)) = 0
        _Speed ("Spread Distance", Float) = 3
        _Gravity ("Gravity", Float) = 0
        _ConeHalf ("Cone Half (rad)", Float) = 3.1416
        _BaseDir ("Base Dir (xy)", Vector) = (1, 0, 0, 0)
        _CountFrac ("Count Frac", Range(0, 1)) = 1
        _PixelSize ("Pixel Size", Float) = 0.05
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex BurstVertex
            #pragma fragment BurstFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;   // xy — угловое смещение пикселя [-0.5..0.5]
                float2 rnd        : TEXCOORD1;   // x — угол частицы 0..1; y — резерв
                float4 color      : COLOR;       // r=speed rnd, g=life rnd, b=count threshold
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  age        : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BaseDir;
                half  _Life;
                half  _Speed;
                half  _Gravity;
                half  _ConeHalf;
                half  _CountFrac;
                half  _PixelSize;
            CBUFFER_END

            Varyings BurstVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Отсев по количеству: частицы с порогом выше _CountFrac уводим за клип (не рисуются).
                if (v.color.b > _CountFrac)
                {
                    o.positionCS = float4(2, 2, 2, 1);
                    o.age = 2;
                    return o;
                }

                float lifeScale = lerp(0.6, 1.0, v.color.g);
                float age = saturate(_Life / lifeScale);

                float baseAng = atan2(_BaseDir.y, _BaseDir.x);
                float ang = baseAng + (v.rnd.x - 0.5) * 2.0 * _ConeHalf;
                float2 dir = float2(cos(ang), sin(ang));

                float speed = _Speed * lerp(0.5, 1.2, v.color.r);
                float2 center = dir * (speed * age) + float2(0.0, -_Gravity * age * age);
                float2 pos = center + v.positionOS.xy * _PixelSize; // квад-пиксель постоянного размера

                o.positionCS = TransformObjectToHClip(float3(pos, 0.0));
                o.age = age;
                return o;
            }

            half4 BurstFragment(Varyings i) : SV_Target
            {
                half4 c = _Color;
                c.a *= saturate(1.0 - i.age); // гаснет к концу жизни
                clip(c.a - 0.01);
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
