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

// Шум НЕ зовётся _MainTex намеренно: шторку рисует UI-слой, а туда она попадает через Graphics.Blit,
// и Blit подменяет своим источником именно _MainTex — узор чернил тогда затирался бы белым квадратом.
TEXTURE2D(_NoiseTex);
SAMPLER(sampler_NoiseTex);

CBUFFER_START(UnityPerMaterial)
    float4 _NoiseTex_ST;
    half4  _InkColor;
    half   _Progress;
    half   _Softness;
    half   _Scale;
    half   _Vignette;
    half   _Dither;
    float4 _Center;
    half   _Aspect;
    half   _Dive;
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
    // Узор ПОДЪЕЗЖАЕТ к точке схлопывания по ходу закрытия: вместе с наездом камеры на узел это читается
    // как движение внутрь кадра, а не как затемнение поверх неподвижной картинки.
    float2 center = _Center.xy;
    float2 uv = center + (i.uv - center) * (1.0 - _Dive * _Progress);

    // Порядок закрытия задаёт ТЕКСТУРА, а не время: там, где шум темнее, чернила приходят раньше.
    // Отсюда и рваный, «расползающийся» край вместо ровной заливки кадра.
    half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * _Scale).r;

    // Виньетка подмешивается к порогу, а не к цвету: дальнее от точки входа закрывается первым, сама точка —
    // последней, и кадр схлопывается ИМЕННО в неё, а не в геометрический центр экрана.
    // По ширине расстояние правим на аспект — иначе «воронка» вытягивается в овал.
    float2 c = (uv - center) * float2(_Aspect, 1.0);

    // Нормируем на расстояние до САМОГО ДАЛЬНЕГО угла от точки входа. Без этого сдвинутый от центра узел
    // сразу давал бы «единицу» на половине кадра, и закрытие схлопывалось бы рывком за первую треть хода.
    float2 far = max(center, 1.0 - center) * float2(_Aspect, 1.0);
    half corner = saturate(dot(c, c) / max(dot(far, far), 1e-4));
    half threshold = lerp(noise, saturate(noise - corner), _Vignette);

    half soft = max(0.001h, _Softness);
    half alpha = saturate((_Progress * (1.0h + soft) - threshold) / soft);

    half bayer = BayerThreshold(i.positionCS.xy);
    half dithered = step(bayer, alpha);
    alpha = lerp(alpha, dithered, _Dither);

    return half4(_InkColor.rgb, alpha * _InkColor.a);
}

#endif
