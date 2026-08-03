Shader "Guildmaster/Vfx/SwingArc"
{
    // ДУГА ЗА КЛИНКОМ — первая стадия удара: сектор с центром в плече, заметающий угол, который клинок
    // УЖЕ прошёл. Живёт только на strike-фазе взмаха и гаснет сразу после неё, поэтому в постоянное
    // свечение не превращается. Рисуется всегда, когда клинок махнул: и на попадании, и на промахе, и на
    // холостом замахе — дуга говорит «клинок прошёл здесь», и это правда в любом исходе.
    //
    // Не путать с формой удара (SH_Vfx_HitForm): та появляется ПОСЛЕ контакта и заявляет «удар состоялся».
    // Две стадии в разное время — потому и не спорят между собой за внимание.
    //
    // Хвост теряет ПРОЗРАЧНОСТЬ, а не уходит во второй цвет: свет один, просто его меньше там, где клинок
    // побывал раньше.
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.8, 0.4, 1)

        _AngleFrom ("Angle From (rad)", Float) = 0
        _AngleTo   ("Angle To (rad)", Float) = 1.2

        _RadiusInner ("Inner Radius (доли полу-quad)", Range(0, 1)) = 0.35
        _RadiusOuter ("Outer Radius (доли полу-quad)", Range(0, 1)) = 0.95

        _Fade     ("Fade (общая сила)", Range(0, 1)) = 1
        _TailBias ("Tail Bias (насколько быстро гаснет хвост)", Range(0.2, 4)) = 1.6
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

        Blend One One
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _AngleFrom;
                half  _AngleTo;
                half  _RadiusInner;
                half  _RadiusOuter;
                half  _Fade;
                half  _TailBias;
            CBUFFER_END

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;     // центр quad = плечо
                float r = length(p);
                if (r < 1e-4) return 0;

                // Радиальная маска кольца: у самого плеча свечения нет (там рука, а не след клинка),
                // наружу оно сходит на нет мягко.
                float edge = max(fwidth(r) * 1.5, 1e-4);
                float ring = (1.0 - smoothstep(_RadiusOuter - edge, _RadiusOuter + edge, r))
                           * smoothstep(_RadiusInner - edge, _RadiusInner + edge, r);
                if (ring <= 0.001) return 0;

                // Угловая маска: от начала взмаха до текущего положения клинка. Углы приходят снаружи
                // РАЗВЁРНУТЫМИ (без скачка через ±пи), поэтому дугу можно мерить простым отношением.
                float a = atan2(p.y, p.x);
                float span = _AngleTo - _AngleFrom;
                float absSpan = abs(span);
                if (absSpan < 1e-4) return 0;

                // Ближайшее представление угла пикселя к началу дуги: без этого сектор, перешедший через
                // ±пи, распадался бы надвое.
                float k = 6.2831853;
                float rel = a - _AngleFrom;
                rel -= k * floor((rel + 3.14159265) / k);
                float t = rel / span;                       // 0 — начало взмаха, 1 — текущий клинок
                if (t < 0.0 || t > 1.0) return 0;

                // Мягкие торцы сектора: жёсткий край читается как вырезанный кусок пирога.
                float angEdge = saturate(0.06 / absSpan);
                float ends = smoothstep(0.0, angEdge, t) * (1.0 - smoothstep(1.0 - angEdge, 1.0, t) * 0.15);

                // Хвост: чем дальше от текущего положения клинка, тем прозрачнее. Второго цвета нет —
                // это тот же свет, которого стало меньше.
                float tail = pow(saturate(t), _TailBias);

                float alpha = ring * ends * tail * saturate(_Fade);
                if (alpha <= 0.001) return 0;

                return half4(_Color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
