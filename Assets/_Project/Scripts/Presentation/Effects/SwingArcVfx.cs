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

        /// <summary>
        /// Куда положить дугу в порядке отрисовки: узел, внутри которого сортируется тело, и порядок
        /// относительно бьющей части. Спрашивается один раз при запуске.
        /// </summary>
        /// <returns><c>false</c> — места внутри тела нет; дуга остаётся там, куда её положили данные.</returns>
        bool TryGetArcAnchor(out Transform parent, out int sortingOrder);
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

        /// <summary>Мягкость переходов МЕЖДУ ступенями. Внешней границы не касается — она резкая.</summary>
        public readonly float Softness;

        /// <summary>Рванность краёв следа: шум по углу и сиду взмаха.</summary>
        public readonly float Roughness;

        public SwingArcStyle(Color core, float coreShare, float colourShare, float opaque,
                             bool profile, float tailSharpness, float softness, float roughness)
        {
            Core          = core;
            CoreShare     = Mathf.Clamp01(coreShare);
            ColourShare   = Mathf.Clamp01(colourShare);
            Opaque        = Mathf.Clamp01(opaque);
            ProfileOn     = profile ? 1f : 0f;
            TailSharpness = Mathf.Clamp(tailSharpness, 0.15f, 2f);
            Softness      = Mathf.Clamp(softness, 0f, 0.6f);
            Roughness     = Mathf.Clamp01(roughness);
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
        private static readonly int SoftnessId    = Shader.PropertyToID("_Softness");
        private static readonly int RoughId       = Shader.PropertyToID("_Rough");

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
        private float _trailSeconds;   // сколько живёт точка следа — ДЛИНА хвоста, см. Remember
        private float _maxSpan;        // страховка от сектора длиннее оборота, радианы
        private float _fadeLeft;
        private float _fadeStart;      // угол начала сектора на момент конца взмаха — от него след съедается
        private bool  _swinging;
        private bool  _playing;

        /// <summary>
        /// След клинка ВО ВРЕМЕНИ: где остриё было в последние <see cref="_trailSeconds"/>. Кольцевой
        /// буфер пар «угол + момент», ровно как точки внутри <c>TrailRenderer</c>.
        /// </summary>
        /// <remarks>
        /// До 07.08.2026 длина хвоста задавалась В ГРАДУСАХ, и это была неверная единица (заметил Макс):
        /// укол и тяжёлый замах оставляли одинаковый след, потому что «сколько градусов держим» ничего не
        /// знает о скорости. След — это остаточное изображение, его длина есть скорость × время; поэтому
        /// быстрый взмах обязан тянуть длинный хвост, а медленный короткий, и вес удара читается сам
        /// собой. Так устроены все трейлы: у точки есть время жизни, а не пройденный путь.
        /// <para>
        /// Хранятся УГЛЫ, а не мировые точки: замер клипа «Атака 1» показал, что остриё идёт вокруг
        /// плеча по окружности с разбросом радиуса в 6% — хранить полные позиции было бы нечего.
        /// </para>
        /// </remarks>
        private readonly float[] _trailAngle = new float[TrailCapacity];
        private readonly float[] _trailTime  = new float[TrailCapacity];
        private int   _trailHead;      // куда писать следующую пробу
        private int   _trailCount;
        private float _elapsed;        // часы этой дуги; идут по ЯВНОМУ времени Tick, а не по Time.time

        /// <summary>
        /// Сколько проб помнит след. Одна проба на кадр: при 144 Гц и полусекундном хвосте нужно 72,
        /// вдвое меньший буфер молча укоротил бы хвост — поэтому берём с запасом, он стоит полкилобайта.
        /// </summary>
        private const int TrailCapacity = 128;

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
                          float fadeOutSeconds, float trailSeconds, float maxSpanDeg, in SwingArcStyle style)
        {
            Cache();

            _source       = source;
            _innerShare   = Mathf.Clamp01(innerShare);
            _tailBias     = Mathf.Max(0.2f, tailBias);
            _fadeOut      = Mathf.Max(0.01f, fadeOutSeconds);
            _trailSeconds = Mathf.Max(0.01f, trailSeconds);
            _maxSpan      = Mathf.Max(0.1f, maxSpanDeg) * Mathf.Deg2Rad;
            _fadeLeft     = _fadeOut;
            _swinging     = true;
            _playing      = true;

            _trailHead  = 0;
            _trailCount = 0;
            _elapsed    = 0f;

            Anchor(source);

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
            _block.SetFloat(SoftnessId, style.Softness);
            _block.SetFloat(RoughId, style.Roughness);
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

        // LateUpdate, а не Update: плечо двигает Animator, и в Update дуга отставала бы от руки на кадр.
        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// Шаг жизни дуги: подтянуться за клинком либо догореть. Публичный и с ЯВНЫМ временем, потому
        /// что в редакторных стендах кадров нет — а показывать они обязаны ровно то же, что бой.
        /// </summary>
        /// <remarks>
        /// До 06.08.2026 стенд вёл дугу сам: свой угол, своё затухание. Получалась вторая правда о
        /// взмахе, и она разошлась с боевой на первом же кадре — там нет ни разворота угла, ни клампа
        /// сектора, ни утекающего хвоста. Единственный способ показать «как в игре» — звать этот
        /// самый код.
        /// </remarks>
        public void Tick(float deltaTime)
        {
            if (!_playing) return;

            _elapsed += Mathf.Max(0f, deltaTime);

            if (_swinging && _source != null &&
                _source.TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out float _))
            {
                Vector2 arm = tip - pivot;
                if (arm.sqrMagnitude > 1e-8f)
                {
                    // Радиус СЛЕДУЕТ за рукой, а не запоминает самый широкий размах. Максимум держался
                    // до 07.08.2026 и давал вот что: после удара локоть сгибается, клинок идёт ближе к
                    // телу, а кольцо остаётся на прежнем радиусе — след «висит» дальше меча и перестаёт
                    // читаться как его движение. Замер по клипу «Атака 1»: за окно взмаха радиус гуляет
                    // на 6% (1.29 → 1.365), так что дыхание сектора глазом не ловится, а отрыв от
                    // клинка — ловится сразу.
                    _radius = arm.magnitude;

                    // Угол РАЗВОРАЧИВАЕТСЯ: клинок за взмах проходит больше пи, и наивный atan2 дал бы
                    // скачок, от которого сектор схлопнулся бы в ничто ровно на середине удара.
                    float raw   = Mathf.Atan2(arm.y, arm.x);
                    float delta = Mathf.DeltaAngle(_angleTo * Mathf.Rad2Deg, raw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    _angleTo += delta;

                    // След — это путь клинка за ПОСЛЕДНИЕ _trailSeconds. Начало хвоста берётся из буфера
                    // проб: где остриё было столько-то назад. Так длина следа сама зависит от скорости —
                    // укол оставит короткий росчерк, размашистый удар длинный, и это разница, которую
                    // видно.
                    Remember(_angleTo);
                    _angleFrom = OldestLiveAngle();

                    // Страховка: сектор длиннее оборота перекрывает сам себя, а шейдер считает долю от
                    // начала следа до клинка и на таком секторе врёт. Упирается в неё только непрерывное
                    // вращение (поток «Вихря»), обычному взмаху до неё не дотянуться.
                    float span = _angleTo - _angleFrom;
                    if (Mathf.Abs(span) > _maxSpan)
                        _angleFrom = _angleTo - Mathf.Sign(span) * _maxSpan;
                }

                Place(pivot);
                Write(1f);
                return;
            }

            // Взмах кончился — след УТЕКАЕТ ЗА КЛИНКОМ, а не гаснет весь разом.
            //
            // До 06.08.2026 догорание было одной общей прозрачностью: сектор оставался во всю длину и
            // просто тускнел — «исчезает сразу», как это и читалось с экрана. След же принадлежит
            // движению, поэтому уходить обязан с того конца, где клинок был РАНЬШЕ: начало сектора
            // подтягивается к его концу, и хвост съедается сам собой.
            if (_swinging)
            {
                _swinging  = false;
                _fadeStart = _angleFrom;   // откуда начинать съедать — угол на момент конца взмаха
            }

            _fadeLeft -= deltaTime;
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

            float left = _fadeLeft / _fadeOut;              // 1 в начале догорания, 0 в конце
            _angleFrom = Mathf.Lerp(_angleTo, _fadeStart, left);

            // Прозрачность держится до последней четверти: гасить одновременно с укорачиванием значит
            // отнять у следа то самое движение, ради которого он и укорачивается.
            Write(Mathf.Clamp01(left / 0.25f));
        }

        /// <summary>Запомнить, где клинок сейчас: одна проба следа за шаг.</summary>
        private void Remember(float angle)
        {
            _trailAngle[_trailHead] = angle;
            _trailTime[_trailHead]  = _elapsed;

            _trailHead = (_trailHead + 1) % TrailCapacity;
            if (_trailCount < TrailCapacity) _trailCount++;
        }

        /// <summary>
        /// Угол начала хвоста: где остриё было <see cref="_trailSeconds"/> назад. Пробы старше этого
        /// срока следу больше не принадлежат — ровно как точки, отжившие своё в <c>TrailRenderer</c>.
        /// </summary>
        /// <remarks>
        /// Ищем ЛИНЕЙНО от самой старой пробы к новым, а не бинарно: живых проб десяток-другой, и цикл по
        /// ним дешевле развесистого поиска. Буфер переполнился — самая старая уже затёрта, и хвост
        /// оказывается короче заказанного; отсюда запас в <see cref="TrailCapacity"/>.
        /// </remarks>
        private float OldestLiveAngle()
        {
            if (_trailCount == 0) return _angleTo;

            float cutoff = _elapsed - _trailSeconds;
            int oldest = (_trailHead - _trailCount + TrailCapacity) % TrailCapacity;

            for (int i = 0; i < _trailCount; i++)
            {
                int idx = (oldest + i) % TrailCapacity;
                if (_trailTime[idx] >= cutoff) return _trailAngle[idx];
            }

            // Все пробы старше среза: клинок стоит на месте дольше времени жизни следа — хвоста нет.
            return _angleTo;
        }

        /// <summary>
        /// Переехать ВНУТРЬ тела бьющего: дуге положено место между мечом и остальными частями, а снаружи
        /// такого места не бывает — группа сортировки читается снаружи как один объект.
        /// </summary>
        /// <remarks>
        /// Источник, который не даёт якоря (статика стенда, тело без группы), оставляет дугу там, куда её
        /// положил спавн: слой и порядок из <c>VfxData</c>. Это не фолбэк-догадка, а честное «внутрь
        /// вкладывать некуда».
        /// </remarks>
        private void Anchor(ISwingArcSource source)
        {
            if (source == null || !source.TryGetArcAnchor(out Transform parent, out int order)) return;
            if (parent == null) return;

            transform.SetParent(parent, worldPositionStays: true);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                r.sortingLayerID = 0;      // внутри группы слой ребёнка не значит ничего, кроме путаницы
                r.sortingOrder   = order;
            }
        }

        private void Place(Vector3 pivot)
        {
            transform.position = pivot;

            // Quad вмещает круг радиусом _radius: половина стороны и есть радиус. Масштаб задаётся МИРОВОЙ,
            // потому что у дуги теперь бывает родитель — тело бьющего. Делением на масштаб родителя
            // приходит и его зеркало: при отрицательном X локальный масштаб тоже становится отрицательным,
            // и сектор в мире остаётся тем же, каким его посчитали по мировым плечу и острию.
            float d = _radius * 2f;
            Vector3 s = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(d / NonZero(s.x), d / NonZero(s.y), 1f);
        }

        private static float NonZero(float v) => Mathf.Abs(v) > 1e-5f ? v : 1f;

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
