#ifndef GUILDMASTER_MAP_FOG_COMMON_INCLUDED
#define GUILDMASTER_MAP_FOG_COMMON_INCLUDED

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4  _FogColor;
    half   _Density;
    half   _Scale1;
    half   _Scale2;
    float4 _Speed1;
    float4 _Speed2;
    float  _RevealX;
    float  _Falloff;
    float  _Trail;
    half   _Dither;
CBUFFER_END

Varyings Vert(Attributes v)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.uv = v.uv;
    return o;
}

// Матрица Байера 4x4 — порог дизеринга по положению пикселя на экране. Даёт тот самый точечный растр
// вместо гладкого градиента: дымка перестаёт быть «фильтром поверх» и становится частью пиксельной картинки.
static const float BayerMatrix[16] =
{
     0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
    12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
     3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
    15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
};

float BayerThreshold(float2 screenPos)
{
    int2 p = int2(fmod(screenPos, 4.0));
    return BayerMatrix[p.y * 4 + p.x];
}

half4 Frag(Varyings i) : SV_Target
{
    // Шум берём по МИРОВЫМ координатам, а не по UV полотна: тогда рисунок тумана не растягивается
    // вместе с картой и не «дышит», когда полотно меняет размер под другой акт.
    float2 w = i.positionWS.xy;
    half n1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, w * _Scale1 + _Speed1.xy * _Time.y).r;
    half n2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, w * _Scale2 + _Speed2.xy * _Time.y).r;

    // Перемножение, а не сложение: даёт рваные клубы с просветами, сложение дало бы ровную муть.
    half clouds = saturate(n1 * n2 * 2.2h);

    // Развеивание: позади отряда тумана почти нет, впереди он набирает плотность.
    float ahead = (i.positionWS.x - (_RevealX - _Trail)) / max(0.01, _Falloff);
    half reveal = saturate(ahead);

    half alpha = clouds * reveal * _Density;

    // Дизеринг: вместо полупрозрачности — решение «пиксель есть или нет» по порогу Байера.
    float threshold = BayerThreshold(i.positionCS.xy);
    half dithered = step(threshold, alpha);
    alpha = lerp(alpha, dithered * alpha, _Dither);

    return half4(_FogColor.rgb, alpha);
}

#endif
