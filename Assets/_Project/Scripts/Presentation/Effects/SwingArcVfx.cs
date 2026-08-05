using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Кто машет: источник геометрии взмаха для дуги за клинком. Шов существует ради того, что эффект
    /// живёт весь взмах и обязан следовать за плечом — а значит спрашивает состояние каждый кадр, в
    /// отличие от формы удара, которой хватает двух точек в момент рождения.
    /// </summary>
    public interface ISwingArcSource
    {
        /// <summary>
        /// Геометрия текущего взмаха: вокруг чего он идёт, где сейчас кончик оружия, насколько прошёл.
        /// </summary>
        /// <returns><c>false</c> — взмах кончился (или его нечем показывать), и дуге пора гаснуть.</returns>
        bool TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out float progress);
    }

    /// <summary>
    /// Как след ОКРАШЕН и какой он формы поперёк — в отличие от геометрии взмаха, которая приходит от
    /// источника каждый кадр. Собирается один раз при спавне из feel-конфига.
    /// </summary>
    /// <remarks>
    /// Приём принят 06.08.2026 по кадру из Cult of the Lamb (канон
    /// <c>gdd/70-gamefeel/vfx-language</c> §«След — росчерк»): вместо ровного кольца одного цвета след
    /// получает <b>ступени поперёк</b> (пересвет в середине, цвет к краям, чёрная кромка) и
    /// <b>профиль ширины вдоль</b> (полумесяц). Тёмное живёт КРАЙНЕЙ СТУПЕНЬЮ собственного градиента,
    /// а не слоем поверх, — поэтому второго прохода нет, но premultiplied шейдеру всё равно нужен:
    /// перекрывать сложением нельзя.
    /// </remarks>
    public readonly struct SwingArcStyle
    {
        /// <summary>Цвет пересвета в середине следа. Белый по той же причине, что у формы удара.</summary>
        public readonly Color Core;

        /// <summary>Доля полутолщины под пересвет.</summary>
        public readonly float CoreShare;

        /// <summary>Доля полутолщины под цвет. Всё, что снаружи, — чёрная кромка.</summary>
        public readonly float ColourShare;

        /// <summary>Сила перекрытия кромкой. Ноль — прежний чистый аддитив, тёмного нет вовсе.</summary>
        public readonly float Opaque;

        /// <summary>Включён ли профиль ширины: 0 — ровное кольцо, 1 — полумесяц.</summary>
        public readonly float ProfileOn;

        /// <summary>Резкость сужения у хвоста: меньше — сужается быстрее к самому концу.</summary>
        public readonly float TailSharpness;

        public SwingArcStyle(Color core, float coreShare, float colourShare, float opaque,
                             bool profile, float tailSharpness)
        {
            Core          = core;
            CoreShare     = Mathf.Clamp01(coreShare);
            ColourShare   = Mathf.Clamp01(colourShare);
            Opaque        = Mathf.Clamp01(opaque);
            ProfileOn     = profile ? 1f : 0f;
            TailSharpness = Mathf.Clamp(tailSharpness, 0.15f, 2f);
        }

        /// <summary>
        /// Максимум профиля — им шейдер нормирует ширину, чтобы «острее» не означало заодно «тоньше».
        /// Считается аналитически: производная <c>t^p (1 - 0.45 t³)</c> обращается в ноль при
        /// <c>t³ = p / (0.45 (p + 3))</c>.
        /// </summary>
        public float ProfilePeak
        {
            get
            {
                float p = TailSharpness;
                float t = Mathf.Pow(p / (0.45f * (p + 3f)), 1f / 3f);
                return Mathf.Max(1e-4f, Mathf.Pow(t, p) * (1f - 0.45f * t * t * t));
            }
        }
    }

    /// <summary>
    /// Дуга за клинком: сектор с центром в плече, заметающий УЖЕ пройденный угол. Живёт на strike-фазе
    /// взмаха и гаснет сразу после неё.
    /// </summary>
    /// <remarks>
    /// <b>Рисуется на каждом взмахе, включая промах и холостой замах.</b> Дуга сообщает только «клинок
    /// прошёл здесь» — это правда в любом исходе. Заявление «удар состоялся» несёт форма, и вот она на
    /// промахе не появляется вовсе.
    /// <para>
    /// <b>Почему не трейл клинка.</b> Трейл — более слабая версия того же (формулировка Макса): он тянется
    /// за кончиком ниткой, тогда как дуга показывает ЗАМЕТЁННЫЙ УГОЛ, то есть работу всей руки.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Renderer))]
    public sealed class SwingArcVfx : MonoBehaviour
    {
        private static readonly int ColorId       = Shader.PropertyToID("_Color");
        private static readonly int AngleFromId   = Shader.PropertyToID("_AngleFrom");
        private static readonly int AngleToId     = Shader.PropertyToID("_AngleTo");
        private static readonly int RadiusInnerId = Shader.PropertyToID("_RadiusInner");
        private static readonly int RadiusOuterId = Shader.PropertyToID("_RadiusOuter");
        private static readonly int FadeId        = Shader.PropertyToID("_Fade");
        private static readonly int TailBiasId    = Shader.PropertyToID("_TailBias");
        private static readonly int CoreColorId   = Shader.PropertyToID("_CoreColor");
        private static readonly int CoreShareId   = Shader.PropertyToID("_CoreShare");
        private static readonly int ColourShareId = Shader.PropertyToID("_ColourShare");
        private static readonly int OpaqueId      = Shader.PropertyToID("_Opaque");
        private static readonly int ProfileOnId   = Shader.PropertyToID("_ProfileOn");
        private static readonly int TailSharpId   = Shader.PropertyToID("_TailSharp");
        private static readonly int ProfilePeakId = Shader.PropertyToID("_ProfilePeak");

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private PooledVfx _pooled;

        private ISwingArcSource _source;
        private float _angleFrom;      // радианы, РАЗВЁРНУТЫЕ: без скачка через ±пи
        private float _angleTo;
        private float _radius;         // плечо → кончик, мировые единицы
        private float _innerShare;
        private float _tailBias;
        private float _fadeOut;        // сколько дуга гаснет после конца взмаха, сек
        private float _maxSpan;        // максимальная угловая длина следа, радианы
        private float _fadeLeft;
        private bool  _swinging;
        private bool  _playing;

        private void Awake() => Cache();

        private void Cache()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_pooled == null) TryGetComponent(out _pooled);
            _block ??= new MaterialPropertyBlock();
        }

        /// <summary>Начать дугу этого взмаха.</summary>
        /// <param name="source">Кто машет — у него спрашивается геометрия каждый кадр.</param>
        /// <param name="colour">Цвет свечения (HDR-палитра бьющего).</param>
        /// <param name="innerShare">Доля радиуса, с которой начинается свечение: у самого плеча его нет.</param>
        /// <param name="tailBias">Насколько быстро гаснет хвост дуги.</param>
        /// <param name="fadeOutSeconds">Сколько дуга догорает после конца взмаха.</param>
        /// <param name="style">Как след окрашен и какой он формы поперёк — см. <see cref="SwingArcStyle"/>.</param>
        public void Begin(ISwingArcSource source, Color colour, float innerShare, float tailBias,
                          float fadeOutSeconds, float maxSpanDeg, in SwingArcStyle style)
        {
            Cache();

            _source     = source;
            _innerShare = Mathf.Clamp01(innerShare);
            _tailBias   = Mathf.Max(0.2f, tailBias);
            _fadeOut    = Mathf.Max(0.01f, fadeOutSeconds);
            _maxSpan    = Mathf.Max(0.1f, maxSpanDeg) * Mathf.Deg2Rad;
            _fadeLeft   = _fadeOut;
            _swinging   = true;
            _playing    = true;

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, colour);
            _block.SetFloat(RadiusInnerId, _innerShare);
            _block.SetFloat(RadiusOuterId, 0.95f);
            _block.SetFloat(TailBiasId, _tailBias);
            _block.SetFloat(FadeId, 1f);
            _block.SetColor(CoreColorId, style.Core);
            _block.SetFloat(CoreShareId, style.CoreShare);
            _block.SetFloat(ColourShareId, style.ColourShare);
            _block.SetFloat(OpaqueId, style.Opaque);
            _block.SetFloat(ProfileOnId, style.ProfileOn);
            _block.SetFloat(TailSharpId, style.TailSharpness);
            _block.SetFloat(ProfilePeakId, style.ProfilePeak);
            _renderer.SetPropertyBlock(_block);

            // Первый кадр задаёт начало дуги: дальше угол только доезжает до текущего клинка.
            if (source != null && source.TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out _))
            {
                Vector2 arm = tip - pivot;
                _radius    = Mathf.Max(0.01f, arm.magnitude);
                _angleFrom = Mathf.Atan2(arm.y, arm.x);
                _angleTo   = _angleFrom;
                Place(pivot);
            }
            else
            {
                _swinging = false;   // источник уже ничего не показывает — дуга сразу уходит в затухание
            }
        }

        /// <summary>Оборвать дугу и погасить сразу (сброс боя).</summary>
        public void Cancel()
        {
            _swinging = false;
            _playing  = false;
            _fadeLeft = 0f;
        }

        private void LateUpdate()
        {
            if (!_playing) return;

            // LateUpdate, а не Update: плечо двигает Animator, и в Update дуга отставала бы от руки на кадр.
            if (_swinging && _source != null &&
                _source.TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out float _))
            {
                Vector2 arm = tip - pivot;
                if (arm.sqrMagnitude > 1e-8f)
                {
                    _radius = Mathf.Max(_radius, arm.magnitude);   // рука разгибается — сектор растёт с ней

                    // Угол РАЗВОРАЧИВАЕТСЯ: клинок за взмах проходит больше пи, и наивный atan2 дал бы
                    // скачок, от которого сектор схлопнулся бы в ничто ровно на середине удара.
                    float raw   = Mathf.Atan2(arm.y, arm.x);
                    float delta = Mathf.DeltaAngle(_angleTo * Mathf.Rad2Deg, raw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    _angleTo += delta;

                    // След — это ПОСЛЕДНИЕ N градусов пути, а не весь путь: начало дуги едет за клинком,
                    // когда сектор перерос свою длину. Иначе непрерывный взмах (поток «Вихря») замкнул бы
                    // круг и пошёл по второму, а сектор длиннее полного оборота перекрывает сам себя —
                    // шейдер считает долю от начала до текущего клинка и на таком секторе врёт.
                    float span = _angleTo - _angleFrom;
                    if (Mathf.Abs(span) > _maxSpan)
                        _angleFrom = _angleTo - Mathf.Sign(span) * _maxSpan;
                }

                Place(pivot);
                Write(1f);
                return;
            }

            // Взмах кончился — догораем на месте: дуга принадлежит движению, а движения больше нет.
            _swinging  = false;
            _fadeLeft -= Time.deltaTime;
            if (_fadeLeft <= 0f)
            {
                _playing = false;
                Write(0f);
                // Длину взмаха заранее не знает никто: скраб ведёт его по сим-тикам, и растяжение
                // зависит от скорости атаки. Поэтому в пул дуга возвращается САМА, догорев, а не по
                // сроку, назначенному при спавне.
                if (_pooled != null) _pooled.Cancel();
                return;
            }

            Write(_fadeLeft / _fadeOut);
        }

        private void Place(Vector3 pivot)
        {
            // Quad вмещает круг радиусом _radius: половина стороны и есть радиус.
            transform.position   = pivot;
            transform.localScale = new Vector3(_radius * 2f, _radius * 2f, 1f);
        }

        private void Write(float fade)
        {
            Cache();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(AngleFromId, _angleFrom);
            _block.SetFloat(AngleToId, _angleTo);
            _block.SetFloat(FadeId, Mathf.Clamp01(fade));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
