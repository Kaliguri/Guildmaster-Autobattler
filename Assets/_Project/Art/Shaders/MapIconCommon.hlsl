#ifndef GUILDMASTER_MAP_ICON_COMMON_INCLUDED
#define GUILDMASTER_MAP_ICON_COMMON_INCLUDED

// Тело обеих пасс SH_Map_Icon. Вынесено в общий файл, чтобы правка рампы не разъезжалась между
// пассами 2D и Forward: в HitFlash код продублирован, и это уже готча на будущее.

struct Attributes
{
    float3 positionOS : POSITION;
    float4 color      : COLOR;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    half4  color      : COLOR;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

// SRP-batcher: единый layout, все per-material поля в одном CBUFFER, без #ifdef.
CBUFFER_START(UnityPerMaterial)
    half4 _MainTex_ST;
    half4 _Color;
    half4 _Flip;
    half4 _ShadowColor;
    half4 _LightColor;
    half  _Recolor;
    half  _Contrast;
CBUFFER_END

float3 UnityFlipSprite(in float3 pos, in half2 flip)
{
    return float3(pos.xy * flip, pos.z);
}

Varyings UnlitVertex(Attributes v)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    v.positionOS = UnityFlipSprite(v.positionOS, _Flip.xy); // flipX/flipY спрайта
    o.positionCS = TransformObjectToHClip(v.positionOS);
    o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
    o.color      = v.color * _Color;                        // vertex color = SpriteRenderer.color (состояние узла)
    return o;
}

half4 UnlitFragment(Varyings i) : SV_Target
{
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

    // Яркость по восприятию (Rec.601): зелёный весит больше синего, иначе синие иконки уходят в чёрный.
    half lum = dot(tex.rgb, half3(0.299h, 0.587h, 0.114h));
    // Контраст рампы — гаммой: <1 вытягивает светлые тона, >1 топит в тень. Спрайты Honeti довольно
    // светлые, и без этой ручки они все схлопываются в верхний конец рампы.
    lum = pow(saturate(lum), _Contrast);

    half3 mapped = lerp(_ShadowColor.rgb, _LightColor.rgb, lum);
    half3 rgb    = lerp(tex.rgb, mapped, saturate(_Recolor));

    // Состояние узла домножается ПОСЛЕ перекраски: гашение пройденных/закрытых обязано работать
    // одинаково независимо от того, какого цвета была исходная иконка.
    return half4(rgb * i.color.rgb, tex.a * i.color.a);
}

#endif
