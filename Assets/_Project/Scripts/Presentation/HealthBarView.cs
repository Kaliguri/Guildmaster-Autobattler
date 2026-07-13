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
        private const string ShaderName = "Guildmaster/UI/SegmentedHealthBar";

        [Header("Рендер")]
        [Tooltip("Единственный Image бара (тип Simple, на всю ширину, белый vertex-цвет). На него ставится инстанс материала.")]
        [SerializeField] private Image _fillImage;

        [Tooltip("Шаблон материала (шейдер SegmentedHealthBar) — задаёт статичный вид (цвета пустоты/урона/" +
                 "хила/насечек, толщину). В рантайме клонируется per-instance. Пусто → Shader.Find (для билда " +
                 "шейдер должен быть Always Included).")]
        [SerializeField] private Material _barMaterial;

        [Header("Насечки (плотность — код; вид — материал)")]
        [Tooltip("Сколько EHP на одну (минорную) насечку. Тюнер под диапазон HP. Больше — реже насечки.")]
        [SerializeField] private float _tickValue = 200f;

        [Tooltip("Через сколько EHP идёт ЖИРНАЯ насечка (якорь абсолюта, напр. каждые 1000). Кратно tickValue.")]
        [SerializeField] private float _majorTickValue = 1000f;

        [Header("Цвета HP/щита (фолбэк; в бою — из CombatColorPalette)")]
        [SerializeField] private Color _fallbackHpColor = new Color(0.30f, 0.85f, 0.35f);
        [SerializeField] private Color _fallbackShieldColor = new Color(0.62f, 0.86f, 1.0f);

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

            if (_barMaterial != null)
                _mat = new Material(_barMaterial);      // клон шаблона — статичный вид берётся из него
            else
            {
                Shader sh = Shader.Find(ShaderName);
                if (sh != null) _mat = new Material(sh);
            }

            if (_mat == null) return;
            if (_fillImage != null) _fillImage.material = _mat;

            // Плотность насечек: жирная каждые majorTickValue/tickValue минорных.
            _mat.SetFloat(IdMajorEvery, Mathf.Max(1f, _majorTickValue / Mathf.Max(0.0001f, _tickValue)));
            // Цвета HP/щита — палитра, если подана; иначе фолбэк.
            _mat.SetColor(IdHpColor,     _hasHpColor ? _hpColor : _fallbackHpColor);
            _mat.SetColor(IdShieldColor, _hasShieldColor ? _shieldColor : _fallbackShieldColor);
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
        public void Bind(RuntimeUnit unit)
        {
            EnsureMaterial();
            _maxHp  = Mathf.Max(1f, unit.Stats.Get(Data.Stats.StatType.MaxHP));
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

            PushDynamicProps();
        }

        private float CurrentScale() => Mathf.Max(_maxHp, _hp + _shield, _trailEhp, 1f);

        private void PushDynamicProps()
        {
            if (_mat == null) return;

            float scale = CurrentScale();
            _mat.SetFloat(IdHpFrac,       _hp / scale);
            _mat.SetFloat(IdCombinedFrac, (_hp + _shield) / scale);
            _mat.SetFloat(IdTrailFrac,    _trailEhp / scale);
            _mat.SetFloat(IdSegments,     Mathf.Max(1f, scale / Mathf.Max(0.0001f, _tickValue)));
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
