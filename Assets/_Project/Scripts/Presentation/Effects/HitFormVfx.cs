using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>Архетип формы удара: правило, по которому она генерируется.</summary>
    /// <remarks>
    /// Архетипов ровно четыре и больше не будет: <b>форма говорит, КАК доставили, цвет говорит, ЧЕМ
    /// ударили</b>. Отдельных форм под огонь, лёд и прочее не заводится — элемент несёт палитра юнита,
    /// поэтому новый элемент стоит одну строку палитры, а не новый эффект.
    /// </remarks>
    public enum HitFormKind
    {
        /// <summary>Режущий: дуга через точку хита, проходит навылет. Точка хита — середина.</summary>
        Slash = 0,

        /// <summary>Колющий: почти прямое веретено, центральный прокол. Точка хита — середина.</summary>
        Pierce = 1,

        /// <summary>Дробящий: короткий след до точки хита плюс звезда в ней. Точка хита — КОНЕЦ.</summary>
        Blunt = 2,

        /// <summary>Линия-всполох выстрела: тот же колющий с нулевым прогибом, по вектору снаряда.</summary>
        Bolt = 3,
    }

    /// <summary>
    /// Заказ на одну форму удара. Собирается презентером в момент попадания и больше не меняется:
    /// форма живёт в мире и не едет за бьющим.
    /// </summary>
    public readonly struct HitFormParams
    {
        /// <summary>Точка удара: середина формы либо её конец, см. <see cref="EndsAtHit"/>.</summary>
        public readonly Vector3 At;

        /// <summary>
        /// Единичное направление удара — куда шёл клинок в момент касания (у выстрела — курс снаряда).
        /// </summary>
        /// <remarks>
        /// Пришло на смену паре точек «начало взмаха → попадание» 06.08.2026. Хорда врала о направлении:
        /// замах начинается за спиной и выше, цель стоит впереди, поэтому знак рубящего удара ложился
        /// почти горизонтально (~20°) вместо честных ~75° вниз, а длину брал по расстоянию до цели.
        /// </remarks>
        public readonly Vector2 Dir;

        public readonly HitFormKind Kind;

        /// <summary>
        /// Кончается ли форма в точке хита. <c>false</c> — клинок проходит НАВЫЛЕТ, и форма продолжается
        /// за B ровно настолько, насколько шла до неё: так удар читается как «рассёк», а не «дотянулся».
        /// <c>true</c> у дробящего (булава остаётся в цели) и у удара, который принял щит.
        /// </summary>
        public readonly bool EndsAtHit;

        /// <summary>Полная длина формы, мировые единицы — несёт вес удара и потому не гуляет.</summary>
        public readonly float Length;

        /// <summary>Полутолщина в середине, мировые единицы.</summary>
        public readonly float HalfThickness;

        /// <summary>
        /// Ширина тёмной ОБВОДКИ снаружи формы, мировые единицы. Ноль — обводки нет.
        /// </summary>
        /// <remarks>
        /// Не путать с <see cref="Rim"/>: кайма живёт ВНУТРИ формы и несёт цвет элемента, обводка лежит
        /// СНАРУЖИ и цвета не имеет вовсе — она перекрывает кадр чёрным (канон
        /// <c>gdd/70-gamefeel/vfx-language</c> §«Форму обводит чёрный контур», 05.08.2026).
        /// </remarks>
        public readonly float LineWidth;

        /// <summary>
        /// Мягкость переходов МЕЖДУ ступенями знака (ядро → кайма → обводка), доля толщины. Внешней
        /// границы обводки не касается: лайн обязан быть краем, а не растушёвкой.
        /// </summary>
        public readonly float Softness;

        /// <summary>Прогиб, мировые единицы. Знак задаёт сторону выгиба.</summary>
        public readonly float Arc;

        /// <summary>Неровность краёв 0..1.</summary>
        public readonly float Roughness;

        /// <summary>Радиус звезды дробящего, мировые единицы. У остальных архетипов — ноль.</summary>
        public readonly float StarRadius;

        /// <summary>Число лучей звезды.</summary>
        public readonly int StarRays;

        /// <summary>Сид вариации — из <c>IRngService</c>, чтобы клиенты кооператива рисовали одно и то же.</summary>
        public readonly float Seed;

        /// <summary>Цвет пересвета в ядре. Всегда белый: раскрасить его значит потушить оба элемента.</summary>
        public readonly Color Core;

        /// <summary>Цвет каймы — палитра бьющего, то есть элемент удара.</summary>
        public readonly Color Rim;

        /// <summary>Сколько форма живёт, сек.</summary>
        public readonly float Life;

        /// <summary>Доля жизни, за которую голова росчерка проходит форму насквозь.</summary>
        public readonly float GrowShare;

        /// <summary>
        /// На сколько (в долях жизни) хвост росчерка отстаёт от головы. Он и гасит форму, догоняя её:
        /// удар выглядит движением, а не вспыхнувшей и растаявшей полосой.
        /// </summary>
        public readonly float TailLag;

        /// <summary>Доля толщины под ядро.</summary>
        public readonly float CoreWidth;

        /// <summary>
        /// Сколько первых секунд форма стоит замороженной — окно hitstop той же пары. Удар залипает в
        /// воздухе на кадр вместе с телами, а не проскакивает мимо остановленного времени.
        /// </summary>
        public readonly float FreezeSeconds;

        public HitFormParams(Vector3 at, Vector2 dir, HitFormKind kind, bool endsAtHit,
            float length, float halfThickness, float lineWidth, float softness,
            float arc, float roughness, float starRadius, int starRays, float seed,
            Color core, Color rim, float life, float growShare, float tailLag, float coreWidth,
            float freezeSeconds)
        {
            At = at; Dir = dir; Kind = kind; EndsAtHit = endsAtHit;
            Length = length; HalfThickness = halfThickness; LineWidth = lineWidth; Softness = softness;
            Arc = arc; Roughness = roughness;
            StarRadius = starRadius; StarRays = starRays; Seed = seed;
            Core = core; Rim = rim;
            Life = life; GrowShare = growShare; TailLag = tailLag;
            CoreWidth = coreWidth; FreezeSeconds = freezeSeconds;
        }
    }

    /// <summary>
    /// Носитель формы удара: один quad, на котором шейдер <c>Guildmaster/Vfx/HitForm</c> рисует серп,
    /// веретено, звезду или линию-всполох. Кода в форме ровно столько, чтобы поставить quad в мир и
    /// вести прогресс жизни — сама форма считается в пикселе.
    /// </summary>
    /// <remarks>
    /// <b>Почему не партиклы и не меш из кода.</b> Партикловая система не умеет формы по двум точкам с
    /// прогибом, а кодовый меш — это визуал, который нельзя приёмить глазами в инспекторе и нельзя
    /// отдать художнику. Здесь код не рисует ничего: он задаёт трансформ и передаёт числа.
    /// <para>
    /// Живёт рядом с <see cref="PooledVfx"/> на том же префабе: пул, sorting и возврат — его забота,
    /// геометрия и параметры — эта.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Renderer))]
    public sealed class HitFormVfx : MonoBehaviour
    {
        /// <summary>
        /// Во сколько раз quad шире звезды дробящего: лучи неравной длины и рвутся наружу, поэтому
        /// впритык к радиусу их обрезало бы краем меша.
        /// </summary>
        private const float StarQuadMargin = 2.4f;

        /// <summary>
        /// Потолок ширины обводки в долях полудлины формы — тот же, что в проперти шейдера
        /// (<c>_LineWidth</c>). Держится здесь, потому что <see cref="MaterialPropertyBlock"/> объявленный
        /// в шейдере диапазон не соблюдает: <c>Range</c> ограничивает только инспектор материала.
        /// </summary>
        private const float MaxLineShare = 0.3f;

        private static readonly int CoreColorId  = Shader.PropertyToID("_CoreColor");
        private static readonly int RimColorId   = Shader.PropertyToID("_RimColor");
        private static readonly int LenId        = Shader.PropertyToID("_Len");
        private static readonly int ArcId        = Shader.PropertyToID("_Arc");
        private static readonly int HalfThickId  = Shader.PropertyToID("_HalfThick");
        private static readonly int CoreWidthId  = Shader.PropertyToID("_CoreWidth");
        private static readonly int RoughId      = Shader.PropertyToID("_Rough");
        private static readonly int SeedId       = Shader.PropertyToID("_Seed");
        private static readonly int KindId       = Shader.PropertyToID("_Kind");
        private static readonly int StarRadiusId = Shader.PropertyToID("_StarRadius");
        private static readonly int StarRaysId   = Shader.PropertyToID("_StarRays");
        private static readonly int ProgressId   = Shader.PropertyToID("_Progress");
        private static readonly int GrowId       = Shader.PropertyToID("_Grow");
        private static readonly int TailLagId    = Shader.PropertyToID("_TailLag");
        private static readonly int LineWidthId  = Shader.PropertyToID("_LineWidth");
        private static readonly int SoftnessId   = Shader.PropertyToID("_Softness");

        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        private float _elapsed;
        private float _life;
        private float _freeze;
        private bool  _playing;

        private void Awake() => Cache();

        private void Cache()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            _block ??= new MaterialPropertyBlock();
        }

        /// <summary>
        /// Поставить форму в мир и начать её жизнь. Зовётся сразу после <see cref="PooledVfx.Play"/>:
        /// тот отвечает за пул и sorting, а трансформ и параметры формы приходят отсюда.
        /// </summary>
        public void Apply(in HitFormParams p)
        {
            Cache();

            // Направление приходит готовым: это движение клинка в момент касания, а не хорда «замах →
            // цель». Вырожденный вектор сюда не доезжает — вид ругается и формы не заказывает вовсе.
            Vector2 d2 = p.Dir.sqrMagnitude > 1e-8f ? p.Dir.normalized : Vector2.right;
            float angle = Mathf.Atan2(d2.y, d2.x) * Mathf.Rad2Deg;
            Vector3 dir = new Vector3(d2.x, d2.y, 0f);

            // Где стоит середина quad. Клинок проходит НАВЫЛЕТ, поэтому точка удара лежит в середине
            // формы, и она же центр меша. Форма кончается в цели (булава, удар, принятый щитом) — тогда
            // знак уезжает назад по своему же направлению.
            Vector3 centre = p.EndsAtHit
                ? p.At - dir * (p.Length * 0.5f)
                : p.At;

            // Quad вмещает и форму, и звезду: у дробящего вторая шире первой, и меш растягивается по ней.
            // Шейдер про мировые единицы не знает — ему всё приходит долями полу-quad, поэтому перевод
            // живёт здесь, в одном месте.
            //
            // ОБВОДКА ТРЕБУЕТ ЗАПАСА: контур лежит СНАРУЖИ формы, и на quad, натянутом впритык, он
            // обрезался бы краем меша ровно там, где и должен быть виден.
            float line     = Mathf.Max(0f, p.LineWidth);
            float length   = Mathf.Max(0.001f, p.Length);
            float quadSize = Mathf.Max(length + line * 2f, (p.StarRadius + line) * StarQuadMargin);
            float halfQuad = quadSize * 0.5f;
            float halfLen  = length * 0.5f;

            transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, 0f, angle));
            transform.localScale = new Vector3(quadSize, quadSize, 1f);

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(CoreColorId, p.Core);
            _block.SetColor(RimColorId, p.Rim);
            _block.SetFloat(LenId, Mathf.Clamp(halfLen / halfQuad, 0.05f, 1f));
            _block.SetFloat(ArcId, p.Arc / halfLen);
            _block.SetFloat(HalfThickId, p.HalfThickness / halfLen);
            // Обводка мерится в тех же долях полудлины формы, что и толщина, — не полу-quad: она растёт
            // вместе с формой и вместе с ней же ужимается на слабом ударе. Потолок тот же, что в
            // проперти шейдера: блок свойств диапазон не соблюдает, а контур шире трети полудлины
            // съел бы саму форму.
            _block.SetFloat(LineWidthId, Mathf.Clamp(line / halfLen, 0f, MaxLineShare));
            _block.SetFloat(SoftnessId, Mathf.Clamp01(p.Softness));
            _block.SetFloat(CoreWidthId, p.CoreWidth);
            _block.SetFloat(RoughId, p.Roughness);
            _block.SetFloat(SeedId, p.Seed);
            _block.SetFloat(KindId, (float)p.Kind);
            _block.SetFloat(StarRadiusId, p.StarRadius / halfQuad);
            _block.SetFloat(StarRaysId, p.StarRays);
            _block.SetFloat(ProgressId, 0f);
            _block.SetFloat(GrowId, Mathf.Clamp(p.GrowShare, 0.05f, 1f));
            _block.SetFloat(TailLagId, Mathf.Clamp(p.TailLag, 0.05f, 1f));
            _renderer.SetPropertyBlock(_block);

            _elapsed = 0f;
            _life    = Mathf.Max(0.01f, p.Life);
            _freeze  = Mathf.Max(0f, p.FreezeSeconds);
            _playing = true;
        }

        private void Update()
        {
            if (!_playing) return;

            // Заморозка удара идёт по НЕмасштабированному времени — тому же, на котором hitstop держит
            // тела. Дальше форма живёт обычным временем боя: в slowmo она обязана тормозить вместе с ним.
            if (_freeze > 0f)
            {
                _freeze -= Time.unscaledDeltaTime;
                return;
            }

            _elapsed += Time.deltaTime;

            Cache();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(ProgressId, Mathf.Clamp01(_elapsed / _life));
            _renderer.SetPropertyBlock(_block);

            if (_elapsed >= _life) _playing = false;
        }
    }
}
