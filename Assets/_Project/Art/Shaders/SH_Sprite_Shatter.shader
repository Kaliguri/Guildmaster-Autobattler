Shader "Guildmaster/Sprite/Shatter"
{
    // Разлёт спрайта на треугольники по одному дро-коллу. Меш — квад, триангулированный на РАЗЪЕДИНЁННЫЕ
    // треугольники (ShatterMesh); каждый треугольник несёт свой центроид (TEXCOORD1) и случайные параметры
    // (COLOR: r=speed, g=spin, b=dirJitter, a=tumbleAxis/phase). Vertex-шейдер двигает три вершины
    // треугольника как жёсткое целое: ПСЕВДО-3D кувыркание вокруг случайной оси (сжатие поперёк оси по
    // |cos| — ортопроекция переворота квада) + 2D-спин вокруг центроида + дрейф вверх-и-наружу + гравитация,
    // по прогрессу _Shatter (0..1). Цвет — три фазы: impact-вспышка (импульс _FlashAmount) → возврат исходного
    // цвета → выцветание в тлеющий уголёк по РАМПЕ core→mid→tail (bloom подхватит яркость) + гашение к концу.
    // Пасс Universal2D — обязателен для Renderer2D (иначе невидим).
    //
    // Разброс между осколками берётся хешем от центроида блока — он у каждого свой, и отдельный
    // вершинный канал под это не нужен. Хеш чисто визуальный: сходиться с C# ему не с чем, поэтому
    // sin-хеш здесь безопасен (в отличие от карт арены, где число читают обе стороны).
    //
    // Блендинг premultiplied (Blend One OneMinusSrcAlpha): пока осколок — кусок спрайта, rgb домножается
    // на альфу и результат тождественен обычному SrcAlpha; к концу домножение снимается, и уголёк начинает
    // светить ПОВЕРХ фона, а не заслонять его. Это и есть разница между «кусок тела» и «искра».
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1, 1, 1, 1)
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
        _Shatter ("Shatter", Range(0, 1)) = 0
        _Explode ("Explode Force", Float) = 1.6
        _Gravity ("Gravity", Float) = 3
        _Spin ("Spin", Float) = 6
        _Spread ("Dir Spread", Float) = 0.8
        _Tumble ("Tumble (pseudo-3D)", Float) = 9
        _UpBias ("Up-and-out drift bias", Float) = 0.6
        [HDR] _EmberColor ("Ember Color (mid)", Color) = (0.25, 0.9, 1, 1)
        [HDR] _EmberCore ("Ember Core (белое ядро)", Color) = (0.85, 1, 1, 1)
        [HDR] _EmberTail ("Ember Tail (глубокий синий)", Color) = (0.1, 0.3, 0.95, 1)
        _EmberBoost ("Ember Emissive Boost", Float) = 2
        _EmberStart ("Ember Fade Start (age)", Range(0, 1)) = 0.4
        _FadePower ("Fade Power (меньше = дольше держится)", Range(0.15, 3)) = 0.35
        _HueJitter ("Hue Jitter (доля осколков в тёплое)", Range(0, 1)) = 0.35
        _LifeVariance ("Life Variance (разброс скорости угасания)", Range(0, 0.8)) = 0.35
        _Glow ("Glow (аддитивность уголька)", Range(0, 1)) = 1
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Blend One OneMinusSrcAlpha   // premultiplied: rgb домножается в фрагменте, к концу домножение снимается
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex ShatterVertex
            #pragma fragment ShatterFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 triCenter  : TEXCOORD1; // центр треугольника (общий у 3 вершин) — точка разлёта/вращения
                float4 color      : COLOR;     // r=speed, g=spin, b=dirJitter, a=tumbleAxis/phase (per-triangle)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  age        : TEXCOORD1;   // возраст ЭТОГО осколка (с разбросом), 0..1+
                float  shardRnd   : TEXCOORD2;   // хеш осколка: сдвиг оттенка уголька
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _MainTex_ST;
                half4 _Color;
                half4 _Flip;
                half4 _FlashColor;
                half4 _EmberColor;
                half4 _EmberCore;
                half4 _EmberTail;
                half  _FlashAmount;
                half  _Shatter;
                half  _Explode;
                half  _Gravity;
                half  _Spin;
                half  _Spread;
                half  _Tumble;
                half  _UpBias;
                half  _EmberBoost;
                half  _EmberStart;
                half  _FadePower;
                half  _HueJitter;
                half  _LifeVariance;
                half  _Glow;
            CBUFFER_END

            float3 UnityFlipSprite(in float3 pos, in half2 flip) { return float3(pos.xy * flip, pos.z); }

            // Хеш осколка от его центроида — у каждого блока свой, отдельный вершинный канал не нужен.
            float ShardHash(float2 c) { return frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453); }

            Varyings ShatterVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float t   = saturate(_Shatter);
                // Дрейф с ТОРМОЖЕНИЕМ: осколки выстреливают резко и замирают, продолжая тлеть. Прежний
                // ease-in (t*t) разгонял их к концу — на длинном разлёте это читалось как «сдувает ветром».
                float md  = 1.0 - pow(1.0 - t, 2.2);
                float2 c      = v.triCenter;
                float2 offset = v.positionOS.xy - c;

                float rSpeed = lerp(0.6, 1.5, v.color.r);
                float rSpin  = v.color.g * 2.0 - 1.0;           // -1..1
                float rDir   = (v.color.b - 0.5) * _Spread;     // джиттер направления

                // Псевдо-3D кувыркание: поворот offset в систему случайной оси, сжатие поперёк оси на |cos|
                // (ортопроекция вращающегося квада — «переворот», показывающий ребро), поворот обратно.
                float tumbleAxis = v.color.a * 6.2831853;
                float tumbleAng  = (v.color.a * 2.0 - 0.5) * _Tumble * t;
                float sAx, cAx; sincos(tumbleAxis, sAx, cAx);
                float2 loc = float2( offset.x * cAx + offset.y * sAx, -offset.x * sAx + offset.y * cAx);
                loc.y *= abs(cos(tumbleAng));
                float2 tumbled = float2( loc.x * cAx - loc.y * sAx, loc.x * sAx + loc.y * cAx);

                // 2D-спин вокруг центроида поверх кувыркания.
                float a = rSpin * _Spin * t;
                float sa, ca; sincos(a, sa, ca);
                float2 rotOff = float2(tumbled.x * ca - tumbled.y * sa, tumbled.x * sa + tumbled.y * ca);

                // Направление разлёта: радиально от центра фигуры + джиттер + восходящий bias (вверх-и-наружу).
                float2 baseDir = normalize(c + float2(0.00013, 0.00017));
                float sd, cd; sincos(rDir, sd, cd);
                float2 dir = float2(baseDir.x * cd - baseDir.y * sd, baseDir.x * sd + baseDir.y * cd);
                dir = normalize(dir + float2(0.0, _UpBias));

                float2 p = c + rotOff + dir * (_Explode * rSpeed * md) + float2(0.0, -_Gravity * t * t);

                float3 posOS = UnityFlipSprite(float3(p, v.positionOS.z), _Flip.xy);
                o.positionCS = TransformObjectToHClip(posOS);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);

                // Разброс времени жизни: часть осколков догорает раньше, часть тлеет дольше положенного.
                // Ровное угасание всем разом выдаёт «один эффект», разнобой — рой отдельных искр.
                float rnd = ShardHash(c);
                o.age      = t * lerp(1.0 - _LifeVariance, 1.0 + _LifeVariance, rnd);
                o.shardRnd = rnd;
                return o;
            }

            half4 ShatterFragment(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color; // фаза 2: исходный цвет юнита
                half age    = saturate(i.age);
                half emberT = smoothstep(_EmberStart, 1.0, age);

                // Фаза 3 — выцветание в уголёк по РАМПЕ: белое ядро → тело → глубокий хвост. Один цвет на всё
                // выцветание давал плоскую заливку; в референсе осколок ближе к ядру белый, дальше — синеет.
                half3 mid = _EmberColor.rgb;
                // Часть осколков уходит в тёплое (жёлто-зелёные искры в ядре вспышки). Оттенок берём поворотом
                // каналов самого mid-цвета: циан → салат, и он остаётся согласован с палитрой без лишнего поля.
                mid = lerp(mid, mid.gbr, step(i.shardRnd, _HueJitter));
                half3 ember = emberT < 0.5h
                    ? lerp(_EmberCore.rgb, mid, emberT * 2.0h)
                    : lerp(mid, _EmberTail.rgb, (emberT - 0.5h) * 2.0h);
                tex.rgb = lerp(tex.rgb, ember * _EmberBoost, emberT);

                // Фаза 1 — impact-вспышка в белый (импульс _FlashAmount из DeathShatter: растёт, затем спадает).
                tex.rgb = lerp(tex.rgb, _FlashColor.rgb, saturate(_FlashAmount));

                // Гашение с зажимом: при _FadePower < 1 осколок держит яркость почти весь путь и тухнет в конце.
                half a = tex.a * pow(saturate(1.0 - age), _FadePower);

                // Premultiplied. Пока это кусок тела — обычный альфа-блендинг; по мере превращения в уголёк
                // выходная альфа уводится в 0 при сохранённом rgb, и осколок начинает светить поверх фона.
                half glow = emberT * _Glow;
                return half4(tex.rgb * a, a * (1.0h - glow));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
