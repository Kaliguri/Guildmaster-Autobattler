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
        public void Begin(ISwingArcSource source, Color colour, float innerShare, float tailBias, float fadeOutSeconds)
        {
            Cache();

            _source     = source;
            _innerShare = Mathf.Clamp01(innerShare);
            _tailBias   = Mathf.Max(0.2f, tailBias);
            _fadeOut    = Mathf.Max(0.01f, fadeOutSeconds);
            _fadeLeft   = _fadeOut;
            _swinging   = true;
            _playing    = true;

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, colour);
            _block.SetFloat(RadiusInnerId, _innerShare);
            _block.SetFloat(RadiusOuterId, 0.95f);
            _block.SetFloat(TailBiasId, _tailBias);
            _block.SetFloat(FadeId, 1f);
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
