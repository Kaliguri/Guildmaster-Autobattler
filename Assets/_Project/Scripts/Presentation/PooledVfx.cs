using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Корневой компонент VFX-префаба: Play в мировой точке → авто-возврат в пул по окончании.
    /// Визуал живёт на префабе; sorting layer + base order приходят из <see cref="Data.Definitions.VfxData"/>.
    /// Относительный order детей (Flash над Sparks) запечён в префабе и сохраняется.
    /// </summary>
    public sealed class PooledVfx : MonoBehaviour
    {
        [Tooltip("Жёсткий потолок жизни, сек (0 = ждать ParticleSystem.IsAlive или 2с фолбэк).")]
        [SerializeField] private float _maxLifetime = 0f;

        [Tooltip("Системы ПОТОКА ВСКРЫТОГО: медленные красные частицы, которые говорят, ЧТО вскрыли, а не " +
                 "чем ударили. Свой цвет они держат сами (палитра бьющего их не красит), а на блоке не " +
                 "эмитятся вовсе — щит принял удар, тело целое.")]
        [SerializeField] private ParticleSystem[] _woundStreams;

        private ParticleSystem[] _particles;
        private ParticleSystem.Burst[][] _baseBursts;   // эталонные бёрсты префаба (см. CacheBaseBursts)
        private ParticleSystem.MinMaxGradient[] _baseColors; // эталонные цвета префаба (см. CacheBaseColors)
        private Renderer[]       _renderers;
        private int[]            _relativeOrders; // order ребёнка минус min по префабу
        private float            _elapsed;
        private float            _life;
        private bool             _playing;
        private Vector3          _baseScale = Vector3.one;
        private bool             _baseScaleCaptured;
        // Самый крупный startSize среди систем префаба — эталон, к которому приводится SizeUnits.
        // Префабные размеры значат ПРОПОРЦИЮ (искра меньше вспышки), абсолют приходит из VfxData.
        private float            _maxBaseStartSize;
        private System.Action<PooledVfx> _onComplete;

        private void Awake() => Cache();

        private void Cache()
        {
            if (_particles == null)
                _particles = GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
                BakeRelativeOrders();
            }
            if (!_baseScaleCaptured)
            {
                _baseScale = transform.localScale;
                _baseScaleCaptured = true;
                _maxBaseStartSize = MeasureMaxStartSize();
            }
        }

        /// <summary>
        /// Крупнейший <c>startSize</c> префаба — знаменатель пропорции. Ноль означает «частиц нет»
        /// (эффект собран из спрайтов или мешей): тогда размер остаётся префабным, потому что приводить
        /// к абсолюту нечего.
        /// </summary>
        private float MeasureMaxStartSize()
        {
            if (_particles == null) return 0f;

            float max = 0f;
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null) continue;
                float size = ps.main.startSize.constantMax;
                if (size > max) max = size;
            }
            return max;
        }

        /// <summary>
        /// Множитель трансформа, приводящий пропорции префаба к запрошенному размеру. Масштабируем
        /// трансформом, а не <c>startSize</c> каждой системы: тогда вместе с частицами едут разлёт,
        /// скорости и радиус формы — эффект становится больше целиком, а не распухает на месте.
        /// </summary>
        private float ResolveTransformScale(float sizeUnits)
        {
            if (_maxBaseStartSize <= 0f) return 1f;
            return Mathf.Max(0.001f, sizeUnits) / _maxBaseStartSize;
        }

        /// <summary>
        /// Относительный order детей: min→0, остальные — дельта. Так SO задаёт base, префаб — стек внутри.
        /// </summary>
        private void BakeRelativeOrders()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _relativeOrders = System.Array.Empty<int>();
                return;
            }

            int min = int.MaxValue;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                if (_renderers[i].sortingOrder < min) min = _renderers[i].sortingOrder;
            }
            if (min == int.MaxValue) min = 0;

            _relativeOrders = new int[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                _relativeOrders[i] = r != null ? r.sortingOrder - min : 0;
            }
        }

        /// <summary>
        /// Проиграть в мировой точке. Sorting: <paramref name="sortingLayerId"/> +
        /// <paramref name="baseSortingOrder"/> + относительный order ребёнка из префаба.
        /// </summary>
        /// <param name="sizeUnits">
        /// Размер крупнейшей частицы в МИРОВЫХ единицах (из <c>VfxData.SizeUnits</c>, помноженный на
        /// множитель силы удара). Не безразмерный коэффициент: сколько эффект займёт на экране, видно
        /// прямо здесь — 0.2 при юните ростом 1.6 это примерно пятнадцать пикселей в боевом кадре.
        /// </param>
        /// <param name="lifeOverride">
        /// Явная жизнь эффекта, сек. Нужна тем, у кого её нельзя вывести из частиц: у формы удара частиц
        /// нет вовсе, а её срок вдобавок сдвигается заморозкой hitstop. 0 = как раньше, по префабу.
        /// </param>
        /// <param name="wound">
        /// Показывать ли поток вскрытого (медленные красные). <c>false</c> — удар в тело не вошёл: щит его
        /// принял, вскрывать нечего.
        /// </param>
        public void Play(Vector3 worldPos, float sizeUnits, float dirDeg,
            int sortingLayerId, int baseSortingOrder, System.Action<PooledVfx> onComplete,
            float countScale = 1f, Gradient tint = null, float lifeOverride = 0f, bool wound = true)
        {
            Cache();
            _onComplete = onComplete;
            _elapsed = 0f;
            _playing = true;

            transform.position = worldPos;
            transform.rotation = Quaternion.Euler(0f, 0f, dirDeg);
            transform.localScale = _baseScale * ResolveTransformScale(sizeUnits);

            ApplySorting(sortingLayerId, baseSortingOrder);
            ApplyEmissionCount(countScale, wound);
            ApplyTint(tint);

            if (_particles != null)
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ParticleSystem ps = _particles[i];
                    if (ps == null) continue;
                    ps.Clear(true);
                    ps.Play(true);
                }
            }

            _life = lifeOverride > 0f ? lifeOverride : ResolveLife();
        }

        /// <summary>
        /// Множит КОЛИЧЕСТВО частиц в бёрстах, не трогая префабные значения (они запоминаются при первом
        /// проигрыше и остаются эталоном). Сила удара должна читаться частотой искр, а не их размером:
        /// крупная искра говорит «большой эффект», а частая — «сильный удар».
        /// </summary>
        private void ApplyEmissionCount(float countScale, bool wound)
        {
            if (_particles == null || _particles.Length == 0) return;
            CacheBaseBursts();
            if (_baseBursts == null) return;

            float k = Mathf.Max(0.05f, countScale);
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null || _baseBursts[i] == null) continue;

                // Поток вскрытого на блоке гасится в ноль, а не уменьшается: «половина красных» читалась бы
                // как «немножко пробил», а щит либо принял удар, либо нет.
                bool isWound = IsWoundStream(ps);
                if (isWound && !wound) k = 0f;
                else k = Mathf.Max(0.05f, countScale);

                ParticleSystem.Burst[] bursts = _baseBursts[i];
                if (bursts.Length == 0) continue;

                var scaled = new ParticleSystem.Burst[bursts.Length];
                for (int b = 0; b < bursts.Length; b++)
                {
                    ParticleSystem.Burst burst = bursts[b];
                    ParticleSystem.MinMaxCurve count = burst.count;
                    // Нижняя граница в одну частицу — чтобы слабый удар не остался вовсе без искр. При
                    // нулевом множителе она НЕ действует: ноль здесь означает «этого потока не будет».
                    count.constantMin = k <= 0f ? 0f : Mathf.Max(1f, count.constantMin * k);
                    count.constantMax = k <= 0f ? 0f : Mathf.Max(1f, count.constantMax * k);
                    burst.count = count;
                    scaled[b] = burst;
                }

                ParticleSystem.EmissionModule emission = ps.emission;
                emission.SetBursts(scaled);
            }
        }

        /// <summary>
        /// Эта система — поток вскрытого? Их различает не цвет, а РОЛЬ: красный поток говорит «что
        /// вскрыли», остальные — «чем ударили», и живут они по-разному даже при близких оттенках.
        /// </summary>
        private bool IsWoundStream(ParticleSystem ps)
        {
            if (_woundStreams == null) return false;
            for (int i = 0; i < _woundStreams.Length; i++)
                if (_woundStreams[i] == ps) return true;
            return false;
        }

        /// <summary>
        /// Красит эффект палитрой ВЛАДЕЛЬЦА. Палитра — ДИАПАЗОН: каждая частица получает случайный оттенок
        /// между концами градиента, и рой искр выходит живым (жёлто-белым вразнобой), а не одноцветным.
        /// Так один префаб служит всем: криомант вспыхивает своим холодом, пастырь — своим светом.
        /// null = как в префабе.
        /// </summary>
        private void ApplyTint(Gradient palette)
        {
            if (_particles == null || _particles.Length == 0) return;
            CacheBaseColors();
            if (_baseColors == null) return;

            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null) continue;

                ParticleSystem.MainModule main = ps.main;
                // Поток вскрытого палитрой бьющего НЕ красится: внутренности красные у всех, кто бы ни
                // ударил, — это факт о цели, а не об атакующем.
                if (palette == null || IsWoundStream(ps)) { main.startColor = _baseColors[i]; continue; }

                // Два конца палитры как границы рандома — Unity сама разбрасывает частицы между ними.
                main.startColor = new ParticleSystem.MinMaxGradient(palette.Evaluate(0f), palette.Evaluate(1f));
            }
        }

        // Эталонные цвета префаба — по той же причине, что и бёрсты: множить уже помноженное нельзя.
        private void CacheBaseColors()
        {
            if (_baseColors != null || _particles == null) return;

            _baseColors = new ParticleSystem.MinMaxGradient[_particles.Length];
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                _baseColors[i] = ps != null ? ps.main.startColor : new ParticleSystem.MinMaxGradient(Color.white);
            }
        }

        // Эталонные бёрсты префаба: множитель всегда считается от них, иначе за десяток слабых ударов
        // подряд количество частиц сползло бы к нулю (каждый следующий множил уже уменьшенное).
        private void CacheBaseBursts()
        {
            if (_baseBursts != null || _particles == null) return;

            _baseBursts = new ParticleSystem.Burst[_particles.Length][];
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null) { _baseBursts[i] = System.Array.Empty<ParticleSystem.Burst>(); continue; }

                ParticleSystem.EmissionModule emission = ps.emission;
                var copy = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(copy);
                _baseBursts[i] = copy;
            }
        }

        /// <summary>Прервать и вернуть в пул (battle reset).</summary>
        public void Cancel()
        {
            if (!_playing) { Finish(); return; }
            StopParticles();
            Finish();
        }

        private void Update()
        {
            if (!_playing) return;

            _elapsed += Time.deltaTime;

            if (_life > 0f)
            {
                if (_elapsed >= _life) { StopParticles(); Finish(); }
                return;
            }

            if (_elapsed >= 2f || !AnyAlive())
            {
                StopParticles();
                Finish();
            }
        }

        private float ResolveLife()
        {
            if (_maxLifetime > 0f) return _maxLifetime;

            float max = 0f;
            if (_particles != null)
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ParticleSystem ps = _particles[i];
                    if (ps == null) continue;
                    var main = ps.main;
                    float d = main.duration;
                    if (main.startLifetime.mode == ParticleSystemCurveMode.Constant)
                        d += main.startLifetime.constant;
                    else if (main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
                        d += main.startLifetime.constantMax;
                    if (d > max) max = d;
                }
            }

            return max > 0f ? max : 0f;
        }

        private bool AnyAlive()
        {
            if (_particles == null || _particles.Length == 0) return false;
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps != null && ps.IsAlive(true)) return true;
            }
            return false;
        }

        private void StopParticles()
        {
            if (_particles == null) return;
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ApplySorting(int sortingLayerId, int baseSortingOrder)
        {
            if (_renderers == null) return;
            if (_relativeOrders == null) BakeRelativeOrders();

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.sortingLayerID = sortingLayerId;
                int rel = _relativeOrders != null && i < _relativeOrders.Length ? _relativeOrders[i] : 0;
                r.sortingOrder = baseSortingOrder + rel;
            }
        }

        private void Finish()
        {
            _playing = false;
            System.Action<PooledVfx> cb = _onComplete;
            _onComplete = null;
            cb?.Invoke(this);
        }
    }
}
