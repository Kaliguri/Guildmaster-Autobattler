#ifndef GUILDMASTER_MAP_BACKDROP_COMMON_INCLUDED
#define GUILDMASTER_MAP_BACKDROP_COMMON_INCLUDED

// Тело обеих пасс SH_Map_Backdrop — в общем файле, чтобы правка фактуры не разъезжалась между
// пассами 2D и Forward.

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
    half4  _BaseColor;
    half4  _StainColor;
    half4  _EdgeColor;
    half   _WeaveTiling;
    half   _GrainTiling;
    half   _WeaveStrength;
    half   _GrainStrength;
    half   _Vignette;
    half   _EdgeRagged;
    half   _EdgeNoiseScale;
    half   _EdgeBurn;
    half   _AspectX;
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

half4 Frag(Varyings i) : SV_Target
{
    // --- фактура бумаги ---
    // Один и тот же шум дважды: крупно — волокно и разводы, мелко — зерно. Второй слой сдвинут,
    // иначе оба рисунка совпадут и дадут «пластиковую» правильность.
    half weave = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _WeaveTiling).r;
    half grain = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _GrainTiling + half2(0.37h, 0.61h)).r;

    half fabric = saturate((weave - 0.5h) * _WeaveStrength + (grain - 0.5h) * _GrainStrength + 0.5h);
    half3 rgb = lerp(_BaseColor.rgb, _StainColor.rgb, fabric);

    // --- рваный край ---
    // Расстояние до ближайшего края листа. По X делим на пропорции, иначе на длинной карте рваность
    // по горизонтали растянется в пологую волну, а по вертикали останется частой.
    float2 d = min(i.uv, 1.0 - i.uv);
    d.x *= max(0.01h, _AspectX);
    float edge = min(d.x, d.y);

    // Шум вдоль периметра рвёт край: где шум выше, бумага «откушена» глубже.
    half tear = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _EdgeNoiseScale).r;
    half tear2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _EdgeNoiseScale * 2.7h + half2(0.13h, 0.71h)).r;
    half bite = (tear * 0.65h + tear2 * 0.35h) * _EdgeRagged;

    float cut = edge - bite;
    // Мягкая, но КОРОТКАЯ растушёвка: край должен быть рваным, а не размытым в дымку.
    half alpha = saturate(cut * 220.0h);

    // Потемнение у самого края — бумага истрёпана и затёрта по периметру.
    half burn = saturate(1.0 - cut * 26.0h);
    rgb = lerp(rgb, _EdgeColor.rgb, burn * _EdgeBurn);

    // Виньетка по листу: теперь она осмысленна — лист ОГРАНИЧЕН, его края видно на экране.
    float2 c = i.uv * 2.0 - 1.0;
    half vig = saturate(1.0 - dot(c, c) * 0.5);
    rgb *= lerp(1.0h, vig, _Vignette);

    return half4(rgb, alpha * _BaseColor.a);
}

#endif
