using Guildmaster.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Надголовный HP/щит-бар на uGUI (world-space). Один <see cref="Image"/> с кастомным шейдером
    /// <c>Guildmaster/UI/SegmentedHealthBar</c> рисует за проход HP, щит, пустоту, chip-дельту урона/хила и
    /// насечки. Насечки — фиксированного ЗНАЧЕНИЯ (<see cref="_tickValue"/> EHP на насечку): их частота
    /// (<c>scale / tickValue</c>) растёт вместе с суммарным EHP, ширина бара при этом не меняется (трюк LoL).
    ///
    /// <para>Нормировка: <c>scale = max(maxHP, HP+щит, trail)</c>. Без щита <c>scale = maxHP</c> — насечки
    /// как в LoL; щит появился — сгустились, доля цвета показывает соотношение HP↔щит.</para>
    ///
    /// <para>Разделение источников (чтобы материал был песочницей): <b>код</b> гонит только динамику
    /// (доли, плотность насечек) и цвета HP/щита из палитры; <b>материал</b> владеет статичным видом
    /// (цвета пустоты/урона/хила/насечек, толщина насечек) — крути его слайдеры в edit-mode и видишь бар
    /// живьём, эти же значения идут в бой. Всё гонится в per-instance материал по рендер-времени
    /// (НЕ по сим-тику — на чек-сумму не влияет).</para>
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [Header("Рендер")]
        [Tooltip("Единственный Image бара (тип Simple, на всю ширину, белый vertex-цвет). На него ставится инстанс материала.")]
        [SerializeField] private Image _fillImage;

        [Tooltip("Шаблон материала (шейдер SegmentedHealthBar) — задаёт статичный вид (цвета пустоты/урона/" +
                 "хила/насечек, толщину). В рантайме клонируется per-instance. ОБЯЗАТЕЛЕН.")]
        [SerializeField] private Material _barMaterial;

        [Header("Насечки (плотность — код; вид — материал)")]
        [Tooltip("Сколько EHP на одну (минорную) насечку. Тюнер под диапазон HP. Больше — реже насечки.")]
        [SerializeField] private float _tickValue = 200f;

        [Tooltip("Через сколько EHP идёт ЖИРНАЯ насечка (якорь абсолюта, напр. каждые 1000). Кратно tickValue.")]
        [SerializeField] private float _majorTickValue = 1000f;

        // Цвета HP и щита сюда ПОДАЮТСЯ (SetMainColor/SetShieldColor) из CombatColorPalette — единственного
        // владельца. Своих копий бар не держит: прежние поля-фолбэки повторяли те же значения третьим
        // местом (после SO и префаба) и разъехались бы на первой же правке палитры. Цвет не подан — значит
        // разводка сцены сломана, и бар честно покажет цвет материала (аудит 2026-07-26, T-12/T-13).

        [Header("Анимация chip-дельты")]
        [Tooltip("Пауза перед стартом догона, сек.")]
        [SerializeField] private float _trailDelay = 0.25f;
        [Tooltip("Скорость догона в долях scale в секунду.")]
        [SerializeField] private float _trailSpeed = 0.8f;

        // Абсолютное состояние (в EHP), из него каждый кадр считаются доли для шейдера.
        private float _maxHp = 1f;
        private float _hp;
        private float _shield;
        private float _trailEhp;      // догоняющий combined (HP+щит), в абсолюте
        private float _delayRemaining;

        private Material _mat;
        private bool _hasHpColor, _hasShieldColor;
        private Color _hpColor, _shieldColor;
        private Vector3 _baseLocalScale = Vector3.one;
        private bool _baseScaleCaptured;
        private float _punchRemaining;
        private float _punchDuration;
        private float _punchAmount;

        private float _lowHpThreshold;   // доля HP, ниже которой полоса начинает тревожно дышать
        private float _lowHpPeriod = 0.9f;
        private float _lowHpAmount;

        private static readonly int IdHpFrac       = Shader.PropertyToID("_HpFrac");
        private static readonly int IdCombinedFrac = Shader.PropertyToID("_CombinedFrac");
        private static readonly int IdTrailFrac    = Shader.PropertyToID("_TrailFrac");
        private static readonly int IdSegments     = Shader.PropertyToID("_Segments");
        private static readonly int IdMajorEvery   = Shader.PropertyToID("_MajorEvery");
        private static readonly int IdHpColor      = Shader.PropertyToID("_HpColor");
        private static readonly int IdShieldColor  = Shader.PropertyToID("_ShieldColor");

        private void Awake() => EnsureMaterial();

        private void EnsureMaterial()
        {
            if (_mat != null) return;

            // Материал ОБЯЗАТЕЛЕН. Прежний фолбэк через Shader.Find был страховкой, которая ни разу не
            // срабатывала (префабы всегда подают материал) и не сработала бы в билде: шейдера нет в Always
            // Included Shaders, поэтому пустой слот дал бы белую полосу только у игрока, а в редакторе всё
            // выглядело бы целым (аудит фолбэков 2026-07-26, п.6).
            if (_barMaterial == null)
            {
                Debug.LogError($"[HealthBarView] - {name}: не назначен _barMaterial → полоса здоровья не будет отрисована");
                return;
            }

            _mat = new Material(_barMaterial);          // клон шаблона — статичный вид берётся из него
            if (_fillImage != null) _fillImage.material = _mat;

            // Плотность насечек: жирная каждые majorTickValue/tickValue минорных.
            _mat.SetFloat(IdMajorEvery, Mathf.Max(1f, _majorTickValue / Mathf.Max(0.0001f, _tickValue)));
            // Цвета — только те, что подали из палитры. Не подали — оставляем материалу его собственные.
            if (_hasHpColor)     _mat.SetColor(IdHpColor,     _hpColor);
            if (_hasShieldColor) _mat.SetColor(IdShieldColor, _shieldColor);
        }

        /// <summary>Цвет HP по принадлежности к смотрящему (из <c>CombatColorPalette</c>).</summary>
        public void SetMainColor(Color color)
        {
            _hpColor = color;
            _hasHpColor = true;
            if (_mat != null) _mat.SetColor(IdHpColor, color);
        }

        /// <summary>Цвет щита (из <c>CombatColorPalette</c>, общий для всех).</summary>
        public void SetShieldColor(Color color)
        {
            _shieldColor = color;
            _hasShieldColor = true;
            if (_mat != null) _mat.SetColor(IdShieldColor, color);
        }

        /// <summary>Привязать к юниту: доли — на текущее состояние мгновенно, trail без догона.</summary>
        public void Bind(in Combat.Tape.UnitSnapshot unit)
        {
            EnsureMaterial();
            _maxHp  = Mathf.Max(1f, unit.MaxHP);
            _hp     = Mathf.Clamp(unit.CurrentHP, 0f, _maxHp);
            _shield = Mathf.Max(0f, unit.CurrentShield);
            _trailEhp = _hp + _shield;
            _delayRemaining = 0f;
            PushDynamicProps();
        }

        /// <summary>Обновить состояние (поллинг из UnitView каждый кадр).</summary>
        public void UpdateBar(float currentHp, float maxHp, float shield)
        {
            float newMax = Mathf.Max(1f, maxHp);
            float newHp  = Mathf.Clamp(currentHp, 0f, newMax);
            float newSh  = Mathf.Max(0f, shield);

            float prevCombined = _hp + _shield;
            float nextCombined = newHp + newSh;
            if (!Mathf.Approximately(prevCombined, nextCombined))
                _delayRemaining = _trailDelay;   // EHP изменилось — перезапустить паузу догона

            _maxHp  = newMax;
            _hp     = newHp;
            _shield = newSh;
        }

        /// <summary>Микро-punch масштаба бара при уроне (ghost/trail уже в Update).</summary>
        public void Punch(float amount, float duration)
        {
            if (!_baseScaleCaptured)
            {
                _baseLocalScale = transform.localScale;
                _baseScaleCaptured = true;
            }
            _punchAmount = Mathf.Max(0f, amount);
            _punchDuration = Mathf.Max(0.01f, duration);
            _punchRemaining = _punchDuration;
        }

        // Догон по рендер-времени; доли считаются от текущего scale.
        private void Update()
        {
            float target = _hp + _shield;
            if (!Mathf.Approximately(_trailEhp, target))
            {
                if (_delayRemaining > 0f)
                    _delayRemaining -= Time.deltaTime;
                else
                {
                    float scale = CurrentScale();
                    _trailEhp = Mathf.MoveTowards(_trailEhp, target, _trailSpeed * scale * Time.deltaTime);
                }
            }

            if (_punchRemaining > 0f)
            {
                _punchRemaining -= Time.unscaledDeltaTime;
                float t = 1f - Mathf.Clamp01(_punchRemaining / _punchDuration);
                // triangle punch 0→1→0
                float w = t < 0.5f ? t * 2f : (1f - t) * 2f;
                float s = 1f + _punchAmount * w;
                transform.localScale = _baseLocalScale * s;
                if (_punchRemaining <= 0f) transform.localScale = _baseLocalScale;
            }

            PushDynamicProps();
        }

        private float CurrentScale() => Mathf.Max(_maxHp, _hp + _shield, _trailEhp, 1f);

        /// <summary>
        /// Порог и форма тревожного пульса на низком HP. Подаёт <c>UnitView</c> из feel-конфига —
        /// своих чисел бар не держит (см. заметку про цвета выше). Порог ≤ 0 = пульса нет.
        /// </summary>
        public void SetLowHpPulse(float threshold, float period, float amount)
        {
            _lowHpThreshold = Mathf.Clamp01(threshold);
            _lowHpPeriod    = Mathf.Max(0.05f, period);
            _lowHpAmount    = Mathf.Max(0f, amount);
        }

        private void PushDynamicProps()
        {
            if (_mat == null) return;

            float scale = CurrentScale();
            _mat.SetFloat(IdHpFrac,       _hp / scale);
            _mat.SetFloat(IdCombinedFrac, (_hp + _shield) / scale);
            _mat.SetFloat(IdTrailFrac,    _trailEhp / scale);
            _mat.SetFloat(IdSegments,     Mathf.Max(1f, scale / Mathf.Max(0.0001f, _tickValue)));

            PushHpColor();
        }

        /// <summary>
        /// Полоса на исходе дышит светом. Это не украшение, а сведения: в свалке из восьми бойцов
        /// «кто вот-вот умрёт» иначе читается только сравнением длин полосок.
        /// <para>Пульсируем ЯРКОСТЬЮ, а не масштабом: масштаб уже занят punch'ем от урона, и два эффекта
        /// на одном канале дрались бы. Время unscaled — тревога не должна застывать в slowmo.</para>
        /// </summary>
        private void PushHpColor()
        {
            if (!_hasHpColor) return;

            float frac = _maxHp > 0f ? _hp / _maxHp : 1f;
            if (_lowHpThreshold <= 0f || _lowHpAmount <= 0f || frac > _lowHpThreshold || _hp <= 0f)
            {
                _mat.SetColor(IdHpColor, _hpColor);
                return;
            }

            // Ближе к нулю — тревожнее: у самой смерти пульс на полную, у порога едва заметен.
            float urgency = 1f - Mathf.Clamp01(frac / _lowHpThreshold);
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / _lowHpPeriod));
            float boost = 1f + _lowHpAmount * urgency * wave;

            Color pulsed = _hpColor * boost;
            pulsed.a = _hpColor.a;
            _mat.SetColor(IdHpColor, pulsed);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
