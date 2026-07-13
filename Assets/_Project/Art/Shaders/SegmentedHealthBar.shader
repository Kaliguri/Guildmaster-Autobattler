// Сегментированный HP/щит-бар для uGUI (world-space, надголовный).
// Всё рисуется за один проход по UV.x (доля вдоль ширины бара):
//   [0 .. HpFrac)        — HP (цвет по принадлежности, из палитры)
//   [HpFrac .. Combined)  — щит (absorb-пул поверх HP)
//   chip-дельта           — недавно потерянное/восстановленное EHP (догоняющий trail)
//   [.. 1]                — пустота (тёмный фон)
// Насечки — фиксированного ЗНАЧЕНИЯ: частота Segments = scale / tickValue, поэтому при росте
// суммарного EHP насечки СГУЩАЮТСЯ, а ширина бара не меняется (трюк LoL). Жирные — каждые MajorEvery.
Shader "Guildmaster/UI/SegmentedHealthBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _HpFrac       ("HP Frac", Range(0,1)) = 1
        _CombinedFrac ("HP+Shield Frac", Range(0,1)) = 1
        _TrailFrac    ("Trail Frac", Range(0,1)) = 1

        _Segments      ("Segments (scale/tickValue)", Float) = 5
        _MajorEvery    ("Major every N segments", Float) = 5
        _TickWidth     ("Minor tick width (UV)", Float) = 0.004
        _MajorTickWidth("Major tick width (UV)", Float) = 0.010
        _MinorTickHeight("Minor tick height (from top, 0..1)", Range(0,1)) = 0.5
        [Toggle] _MinorTicksOn("Minor ticks on (0 = only major, clean mode)", Float) = 1

        _HpColor     ("HP Color", Color) = (0.30,0.85,0.35,1)
        _ShieldColor ("Shield Color", Color) = (0.62,0.86,1.0,1)
        _EmptyColor  ("Empty Color", Color) = (0.06,0.06,0.06,0.85)
        _DamageColor ("Damage Trail", Color) = (0.95,0.85,0.20,1)
        _HealColor   ("Heal Trail", Color) = (0.60,1.00,0.70,1)
        _TickColor   ("Minor Tick Color", Color) = (0,0,0,0.45)
        _MajorTickColor ("Major Tick Color", Color) = (0,0,0,0.80)

        // Стандартный UI stencil-плумбинг (маски Canvas).
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float _HpFrac, _CombinedFrac, _TrailFrac;
            float _Segments, _MajorEvery, _TickWidth, _MajorTickWidth, _MinorTickHeight, _MinorTicksOn;
            fixed4 _HpColor, _ShieldColor, _EmptyColor, _DamageColor, _HealColor, _TickColor, _MajorTickColor;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.color    = v.color;
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float x = i.texcoord.x;

                // --- Заливка: HP -> щит -> пустота -----------------------------------------
                fixed4 col = _EmptyColor;
                if (x < _HpFrac)            col = _HpColor;
                else if (x < _CombinedFrac) col = _ShieldColor;

                // --- Chip-дельта (догоняющий trail): урон справа от combined, хил слева ------
                if (_TrailFrac > _CombinedFrac && x >= _CombinedFrac && x < _TrailFrac)
                    col = _DamageColor;
                else if (_TrailFrac < _CombinedFrac && x >= _TrailFrac && x < _CombinedFrac)
                    col = _HealColor;

                // --- Насечки: линия у ближайшей границы сегмента ----------------------------
                float seg  = x * _Segments;
                float bi   = floor(seg + 0.5);     // индекс ближайшей границы
                float nb   = bi / _Segments;       // её позиция в UV.x
                float dist = abs(x - nb);
                float aa   = fwidth(x);

                float m       = fmod(bi, _MajorEvery);
                bool  isMajor = (m < 0.5) || (m > _MajorEvery - 0.5);
                float halfW   = (isMajor ? _MajorTickWidth : _TickWidth) * 0.5;

                // Минорные насечки — короткие (от верха вниз до _MinorTickHeight, стиль LoL); жирные — на всю высоту.
                // Чистый режим (_MinorTicksOn=0): минорные не рисуются, остаются только жирные деления.
                float yGate  = isMajor ? 1.0 : step(1.0 - _MinorTickHeight, i.texcoord.y);
                float allow  = isMajor ? 1.0 : _MinorTicksOn;
                float tickMask = (1.0 - smoothstep(halfW, halfW + aa, dist)) * yGate * allow;
                if (bi < 0.5) tickMask = 0.0;      // левый край — не насечка

                fixed4 tc = isMajor ? _MajorTickColor : _TickColor;
                col.rgb = lerp(col.rgb, tc.rgb, tickMask * tc.a);

                // --- Финал: форма/альфа спрайта и вершинная альфа ---------------------------
                col.a  *= i.color.a * tex2D(_MainTex, i.texcoord).a;
                col.rgb *= i.color.rgb;
                return col;
            }
            ENDCG
        }
    }
}
