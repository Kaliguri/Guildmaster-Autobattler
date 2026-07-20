#ifndef GUILDMASTER_MAP_TRANSITION_COMMON_INCLUDED
#define GUILDMASTER_MAP_TRANSITION_COMMON_INCLUDED

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
    UNITY_VERTEX_OUTPUT_STEREO
};

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4  _InkColor;
    half   _Progress;
    half   _Softness;
    half   _Scale;
    half   _Vignette;
    half   _Dither;
CBUFFER_END

Varyings Vert(Attributes v)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.uv = v.uv;
    return o;
}

// Та же матрица Байера, что у тумана карты, и по той же причине: растворение точечным растром читается
// как часть пиксельной картинки, а гладкий градиент — как фильтр, наложенный поверх неё.
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
    // Порядок закрытия задаёт ТЕКСТУРА, а не время: там, где шум темнее, чернила приходят раньше.
    // Отсюда и рваный, «расползающийся» край вместо ровной заливки кадра.
    half noise = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _Scale).r;

    // Виньетка подмешивается к порогу, а не к цвету: углы закрываются первыми, центр — последним,
    // и кадр схлопывается внутрь, вместо того чтобы просто гаснуть целиком.
    float2 c = i.uv - 0.5;
    half corner = saturate(dot(c, c) * 4.0h);
    half threshold = lerp(noise, saturate(noise - corner), _Vignette);

    half soft = max(0.001h, _Softness);
    half alpha = saturate((_Progress * (1.0h + soft) - threshold) / soft);

    half bayer = BayerThreshold(i.positionCS.xy);
    half dithered = step(bayer, alpha);
    alpha = lerp(alpha, dithered, _Dither);

    return half4(_InkColor.rgb, alpha * _InkColor.a);
}

#endif
