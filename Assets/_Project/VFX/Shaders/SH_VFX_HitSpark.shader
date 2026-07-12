Shader "Guildmaster/VFX/HitSpark"
{
    Properties
    {
        [MainColor] _BaseColor ("Core Color", Color) = (1, 0.95, 0.55, 1)
        _TipColor ("Tip Color", Color) = (1, 0.35, 0.1, 1)
        _Intensity ("Intensity", Range(1, 6)) = 2.5
        _PixelSteps ("Color Steps", Range(2, 12)) = 5
        _RadialSteps ("Radial Steps", Range(2, 24)) = 10
        _Spikes ("Spike Count", Range(2, 16)) = 8
        _Sharpness ("Spike Sharpness", Range(1, 20)) = 6
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                float _Intensity;
                float _PixelSteps;
                float _RadialSteps;
                float _Spikes;
                float _Sharpness;
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

            // Age of the particle (0 = birth, 1 = death), smuggled in through the alpha
            // channel of Color Over Lifetime so the shader can drive the burst shape
            // without needing Custom Vertex Streams.
            half4 frag(Varyings input) : SV_Target
            {
                float2 c = input.uv - 0.5;
                float r = length(c) * 2.0;
                float angle = atan2(c.y, c.x);
                float age = saturate(input.color.a);

                float rays = pow(abs(cos(angle * _Spikes * 0.5)), _Sharpness);
                float burstRadius = lerp(0.15, 1.0, age) * lerp(0.3, 1.0, rays);

                float rSnapped = floor(r * _RadialSteps) / _RadialSteps;
                float mask = step(rSnapped, burstRadius);

                float fade = pow(saturate(1.0 - age), 1.6);

                half3 col = lerp(_TipColor.rgb, _BaseColor.rgb, saturate(1.0 - rSnapped / max(burstRadius, 0.001)));
                col = floor(col * _PixelSteps) / max(_PixelSteps - 1.0, 1.0);
                col *= _Intensity;

                half alpha = mask * fade * _BaseColor.a;
                clip(alpha - _AlphaClip);
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
