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
        /// <summary>Точка A — откуда пришёл удар (кончик оружия в начале взмаха либо старт снаряда).</summary>
        public readonly Vector3 From;

        /// <summary>Точка B — куда пришёл удар: середина формы либо её конец, см. <see cref="EndsAtHit"/>.</summary>
        public readonly Vector3 To;

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

        public HitFormParams(Vector3 from, Vector3 to, HitFormKind kind, bool endsAtHit,
            float length, float halfThickness,
            float arc, float roughness, float starRadius, int starRays, float seed,
            Color core, Color rim, float life, float growShare, float tailLag, float coreWidth,
            float freezeSeconds)
        {
            From = from; To = to; Kind = kind; EndsAtHit = endsAtHit;
            Length = length; HalfThickness = halfThickness; Arc = arc; Roughness = roughness;
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

            Vector3 axis = p.To - p.From;
            // Вырожденный случай (A совпал с B) не должен ронять поворот в NaN: тогда форма ложится
            // горизонтально — это честнее, чем не показать удар вовсе.
            float angle = axis.sqrMagnitude > 1e-8f
                ? Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg
                : 0f;

            Vector3 dir = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.right;

            // Где стоит середина quad. Клинок проходит НАВЫЛЕТ, поэтому точка хита лежит в середине формы;
            // если удар кончился в цели (булава, принявший щит) — она конечная, и форма уезжает назад.
            Vector3 centre = p.EndsAtHit
                ? p.To - dir * (p.Length * 0.5f)
                : p.To;

            // Quad вмещает и форму, и звезду: у дробящего вторая шире первой, и меш растягивается по ней.
            // Шейдер про мировые единицы не знает — ему всё приходит долями полу-quad, поэтому перевод
            // живёт здесь, в одном месте.
            float length   = Mathf.Max(0.001f, p.Length);
            float quadSize = Mathf.Max(length, p.StarRadius * StarQuadMargin);
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
