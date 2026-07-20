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
    half   _WeaveTiling;
    half   _GrainTiling;
    half   _WeaveStrength;
    half   _GrainStrength;
    half   _Vignette;
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
    // Один и тот же шум, взятый дважды: крупно — волокно/разводы бумаги, мелко — зерно.
    // Второй слой сдвинут, иначе оба рисунка совпадут и дадут «пластиковую» правильность.
    half weave = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _WeaveTiling).r;
    half grain = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _GrainTiling + half2(0.37h, 0.61h)).r;

    half fabric = saturate((weave - 0.5h) * _WeaveStrength + (grain - 0.5h) * _GrainStrength + 0.5h);
    half3 rgb = lerp(_BaseColor.rgb, _StainColor.rgb, fabric);

    // Виньетка по полотну. ПО УМОЛЧАНИЮ ВЫКЛЮЧЕНА (в материале 0): пока полотно растянуто на всю карту
    // с запасом, его края всегда далеко за экраном, и затемнение там просто не видно — экранную виньетку
    // даёт локальный Volume в зоне карты. Параметр оставлен на случай, если карта станет ОГРАНИЧЕННЫМ
    // листом: тогда затемнение по краям самого листа снова обретёт смысл.
    float2 c = i.uv * 2.0 - 1.0;
    half edge = saturate(1.0 - dot(c, c) * 0.5);
    rgb *= lerp(1.0h, edge, _Vignette);

    return half4(rgb, _BaseColor.a);
}

#endif
