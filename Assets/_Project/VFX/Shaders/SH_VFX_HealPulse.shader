Shader "Guildmaster/VFX/HealPulse"
{
    Properties
    {
        [MainColor] _BaseColor ("Ring Color", Color) = (0.35, 1, 0.5, 1)
        _RimColor ("Rim Color", Color) = (1, 0.95, 0.55, 1)
        _Intensity ("Intensity", Range(1, 6)) = 2
        _PixelSteps ("Color Steps", Range(2, 12)) = 5
        _Waves ("Shimmer Waves", Range(0, 16)) = 6
        _ShimmerAmount ("Shimmer Amount", Range(0, 0.2)) = 0.03
        _ShimmerSpeed ("Shimmer Speed", Range(0, 20)) = 8
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
                half4 _RimColor;
                float _Intensity;
                float _PixelSteps;
                float _Waves;
                float _ShimmerAmount;
                float _ShimmerSpeed;
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
            // channel of Color Over Lifetime. Drives an expanding, thinning SDF ring.
            half4 frag(Varyings input) : SV_Target
            {
                float2 c = input.uv - 0.5;
                float r = length(c) * 2.0;
                float angle = atan2(c.y, c.x);
                float age = saturate(input.color.a);

                float ringRadius = lerp(0.08, 0.95, age);
                float thickness = lerp(0.22, 0.05, age);
                float shimmer = sin(angle * _Waves + _Time.y * _ShimmerSpeed) * _ShimmerAmount;

                float dist = abs(r - ringRadius - shimmer);
                float halfThickness = max(thickness * 0.5, 0.001);
                float mask = step(dist, halfThickness);

                float endFade = 1.0 - smoothstep(0.82, 1.0, age);

                half3 col = lerp(_BaseColor.rgb, _RimColor.rgb, saturate(dist / halfThickness));
                col = floor(col * _PixelSteps) / max(_PixelSteps - 1.0, 1.0);
                col *= _Intensity;

                half alpha = mask * endFade * _BaseColor.a;
                clip(alpha - _AlphaClip);
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
