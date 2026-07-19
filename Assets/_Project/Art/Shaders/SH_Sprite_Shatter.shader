Shader "Guildmaster/Sprite/Shatter"
{
    // Разлёт спрайта на треугольники по одному дро-коллу. Меш — квад, триангулированный на РАЗЪЕДИНЁННЫЕ
    // треугольники (ShatterMesh); каждый треугольник несёт свой центроид (TEXCOORD1) и случайные параметры
    // (COLOR: r=speed, g=spin, b=dirJitter, a=tumbleAxis/phase). Vertex-шейдер двигает три вершины
    // треугольника как жёсткое целое: ПСЕВДО-3D кувыркание вокруг случайной оси (сжатие поперёк оси по
    // |cos| — ортопроекция переворота квада) + 2D-спин вокруг центроида + дрейф вверх-и-наружу + гравитация,
    // по прогрессу _Shatter (0..1). Цвет — три фазы: impact-вспышка (импульс _FlashAmount) → возврат исходного
    // цвета → выцветание в emissive-бирюзу (_EmberColor*_EmberBoost, bloom подхватит) + гашение к концу.
    // Пасс Universal2D — обязателен для Renderer2D (иначе невидим).
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
        _Shatter ("Shatter", Range(0, 1)) = 0
        _Explode ("Explode Force", Float) = 1.6
        _Gravity ("Gravity", Float) = 3
        _Spin ("Spin", Float) = 6
        _Spread ("Dir Spread", Float) = 0.8
        _Tumble ("Tumble (pseudo-3D)", Float) = 9
        _UpBias ("Up-and-out drift bias", Float) = 0.6
        [HDR] _EmberColor ("Ember Color", Color) = (0.25, 0.9, 1, 1)
        _EmberBoost ("Ember Emissive Boost", Float) = 2
        _EmberStart ("Ember Fade Start (age)", Range(0, 1)) = 0.4
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
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
            #pragma vertex ShatterVertex
            #pragma fragment ShatterFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 triCenter  : TEXCOORD1; // центр треугольника (общий у 3 вершин) — точка разлёта/вращения
                float4 color      : COLOR;     // r=speed, g=spin, b=dirJitter, a=tumbleAxis/phase (per-triangle)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  age        : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _MainTex_ST;
                half4 _Color;
                half4 _Flip;
                half4 _FlashColor;
                half4 _EmberColor;
                half  _FlashAmount;
                half  _Shatter;
                half  _Explode;
                half  _Gravity;
                half  _Spin;
                half  _Spread;
                half  _Tumble;
                half  _UpBias;
                half  _EmberBoost;
                half  _EmberStart;
            CBUFFER_END

            float3 UnityFlipSprite(in float3 pos, in half2 flip) { return float3(pos.xy * flip, pos.z); }

            Varyings ShatterVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float t   = saturate(_Shatter);
                float md  = t * t;                              // ease-in дрейфа: медленный старт = микро-«зависание»
                float2 c      = v.triCenter;
                float2 offset = v.positionOS.xy - c;

                float rSpeed = lerp(0.6, 1.5, v.color.r);
                float rSpin  = v.color.g * 2.0 - 1.0;           // -1..1
                float rDir   = (v.color.b - 0.5) * _Spread;     // джиттер направления

                // Псевдо-3D кувыркание: поворот offset в систему случайной оси, сжатие поперёк оси на |cos|
                // (ортопроекция вращающегося квада — «переворот», показывающий ребро), поворот обратно.
                float tumbleAxis = v.color.a * 6.2831853;
                float tumbleAng  = (v.color.a * 2.0 - 0.5) * _Tumble * t;
                float sAx, cAx; sincos(tumbleAxis, sAx, cAx);
                float2 loc = float2( offset.x * cAx + offset.y * sAx, -offset.x * sAx + offset.y * cAx);
                loc.y *= abs(cos(tumbleAng));
                float2 tumbled = float2( loc.x * cAx - loc.y * sAx, loc.x * sAx + loc.y * cAx);

                // 2D-спин вокруг центроида поверх кувыркания.
                float a = rSpin * _Spin * t;
                float sa, ca; sincos(a, sa, ca);
                float2 rotOff = float2(tumbled.x * ca - tumbled.y * sa, tumbled.x * sa + tumbled.y * ca);

                // Направление разлёта: радиально от центра фигуры + джиттер + восходящий bias (вверх-и-наружу).
                float2 baseDir = normalize(c + float2(0.00013, 0.00017));
                float sd, cd; sincos(rDir, sd, cd);
                float2 dir = float2(baseDir.x * cd - baseDir.y * sd, baseDir.x * sd + baseDir.y * cd);
                dir = normalize(dir + float2(0.0, _UpBias));

                float2 p = c + rotOff + dir * (_Explode * rSpeed * md) + float2(0.0, -_Gravity * t * t);

                float3 posOS = UnityFlipSprite(float3(p, v.positionOS.z), _Flip.xy);
                o.positionCS = TransformObjectToHClip(posOS);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                o.age = t;
                return o;
            }

            half4 ShatterFragment(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color; // фаза 2: исходный цвет юнита
                // Фаза 3 — выцветание в emissive-бирюзу к концу разлёта (bloom подхватит яркость).
                half emberT = smoothstep(_EmberStart, 1.0, i.age);
                tex.rgb = lerp(tex.rgb, _EmberColor.rgb * _EmberBoost, emberT);
                // Фаза 1 — impact-вспышка в белый (импульс _FlashAmount из DeathShatter: растёт, затем спадает).
                tex.rgb = lerp(tex.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                tex.a  *= saturate(1.0 - i.age);                                        // осколки гаснут к концу
                return tex;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
