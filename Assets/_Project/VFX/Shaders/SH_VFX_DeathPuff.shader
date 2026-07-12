Shader "Guildmaster/VFX/DeathPuff"
{
    Properties
    {
        [MainColor] _BaseColor ("Cloud Color", Color) = (0.22, 0.2, 0.28, 1)
        _LightColor ("Rim Color", Color) = (0.55, 0.5, 0.62, 1)
        _NoiseScale ("Noise Scale", Range(1, 12)) = 5
        _NoiseSpeed ("Noise Scroll Speed", Range(0, 2)) = 0.3
        _PixelSteps ("Color Steps", Range(2, 12)) = 4
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LightColor;
                float _NoiseScale;
                float _NoiseSpeed;
                float _PixelSteps;
                float _AlphaClip;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float cc = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, u.x) + (cc - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            // Age of the particle (0 = birth, 1 = death), smuggled in through the alpha
            // channel of Color Over Lifetime. Drives a rising erosion threshold so the
            // cloud dissolves from its thinnest patches outward instead of just fading.
            half4 frag(Varyings input) : SV_Target
            {
                float2 c = input.uv - 0.5;
                float r = length(c) * 2.0;
                float falloff = saturate(1.0 - r);
                float age = saturate(input.color.a);

                float t = _Time.y * _NoiseSpeed;
                float n = ValueNoise(input.uv * _NoiseScale + t);
                n += ValueNoise(input.uv * _NoiseScale * 2.0 - t * 0.7) * 0.5;
                n /= 1.5;
                float cloud = n * falloff;

                float threshold = lerp(-0.05, 1.05, age);
                float mask = step(threshold, cloud);
                float endFade = 1.0 - smoothstep(0.88, 1.0, age);

                half3 col = lerp(_BaseColor.rgb, _LightColor.rgb, saturate(cloud * 1.3));
                col = floor(col * _PixelSteps) / max(_PixelSteps - 1.0, 1.0);

                half alpha = mask * endFade * _BaseColor.a;
                clip(alpha - _AlphaClip);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
