#ifndef GUILDMASTER_MAP_TABLE_COMMON_INCLUDED
#define GUILDMASTER_MAP_TABLE_COMMON_INCLUDED

// Тело обеих пасс SH_Map_Table — в общем файле, как и у листа: правка поверхности не должна
// разъезжаться между пассами 2D и Forward.

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

TEXTURE2D(_MainTex);     SAMPLER(sampler_MainTex);     // тайл поверхности (Kenney pattern-pack)
TEXTURE2D(_LightMask);   SAMPLER(sampler_LightMask);   // пятно света (Kenney light-masks)

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4  _BaseColor;
    half4  _PatternColor;
    half4  _LightColor;
    half   _PatternTiling;
    half   _PatternStrength;
    half   _LightStrength;
    half   _Ambient;
    half   _AspectX;
    half   _Vignette;
    half   _VignetteSoftness;
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
    // --- поверхность ---
    // Тайл тянем по пропорциям квада, иначе на вытянутом столе рисунок растянется в полосы.
    float2 tileUv = i.uv * _PatternTiling;
    tileUv.x *= max(0.01h, _AspectX);
    half pattern = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, tileUv).r;

    half3 rgb = lerp(_BaseColor.rgb, _PatternColor.rgb, pattern * _PatternStrength);

    // --- свет ---
    // Маска лежит на всём столе: карта попадает в тёплое пятно, углы кадра тонут. Свет ДОБАВЛЯЕТСЯ
    // к общему подсвету, а не заменяет его, — иначе за пределами пятна получается угольная дыра,
    // в которой не читается даже край листа.
    half light = SAMPLE_TEXTURE2D(_LightMask, sampler_LightMask, i.uv).r;
    half lit = saturate(_Ambient + light * _LightStrength);

    rgb *= lerp(half3(1, 1, 1) * _Ambient, _LightColor.rgb, lit);

    // --- виньетка ---
    // Радиальное затемнение от центра кадра. Считается по КВАДУ, а не по маске света: маска лежит
    // текстурой и тянется вместе с тайлом, а край кадра обязан темнеть одинаково при любом
    // разрешении. Аспект учитываем, иначе на широком экране пятно света вытягивается в овал и
    // верх кадра гаснет раньше боков.
    float2 vd = (i.uv - 0.5h) * 2.0h;
    vd.x *= max(0.01h, _AspectX);
    half dist = saturate(length(vd) / max(0.2h, _VignetteSoftness * 2.0h));
    half vig = 1.0h - dist * dist;              // квадрат — спад мягче линейного, край не «обрубается»
    rgb *= lerp(1.0h, vig, saturate(_Vignette));

    return half4(rgb, 1.0h);
}

#endif
