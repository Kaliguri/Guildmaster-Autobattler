using Guildmaster.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Двойной HP-бар (истинный + догоняющий «призрак», как в Dark Souls / MOBA) на uGUI.
    /// Истинная доля ставится мгновенно поллингом из <see cref="UnitView"/>; призрак плавно
    /// догоняет её по рендер-времени (НЕ сим — на чек-сумму не влияет).
    ///
    /// Оба слоя — <see cref="Image"/> с типом Filled (Horizontal, origin Left): доля = <c>fillAmount</c>.
    /// Порядок в иерархии: Background → TrailFill → MainFill (main рисуется поверх trail). Спереди
    /// видна МЕНЬШАЯ доля (main, зелёный/красный), сзади — БОЛЬШАЯ (trail), поэтому видна цветная «дельта»:
    ///   • урон (target &lt; ghost): дельта [target, ghost] цветом <see cref="_damageTrailColor"/>;
    ///   • хил   (target &gt; ghost): дельта [ghost, target] цветом <see cref="_healTrailColor"/>.
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [Header("Слои (Image Filled Horizontal, origin Left; fillAmount = доля [0..1])")]
        [Tooltip("Передний слой — истинное HP (зелёный/красный).")]
        [SerializeField] private Image _mainImage;
        [Tooltip("Задний слой — догоняющий «призрак».")]
        [SerializeField] private Image _trailImage;

        [Header("Цвет main (истинное HP)")]
        [Tooltip("Фолбэк-цвет основного слоя, если презентация не подала цвет по принадлежности " +
                 "(CombatColorPalette). В бою цвет задаётся из палитры (ally/enemy), см. SetMainColor.")]
        [SerializeField] private Color _fallbackColor = new Color(0.2f, 0.9f, 0.2f);

        // Цвет основного слоя по принадлежности к смотрящему (ally/enemy) — подаёт презентация из
        // CombatColorPalette (первый SO дизайн-системы). Пока не подан — используем _fallbackColor.
        private Color _mainColor;
        private bool  _hasMainColor;

        [Header("Цвета trail (догоняющая дельта)")]
        [Tooltip("Дельта недавно потерянного HP (урон).")]
        [SerializeField] private Color _damageTrailColor = new Color(0.95f, 0.85f, 0.2f);
        [Tooltip("Дельта недавно восстановленного HP (хил).")]
        [SerializeField] private Color _healTrailColor   = new Color(0.6f, 1f, 0.7f);

        [Header("Анимация догона")]
        [Tooltip("Пауза перед стартом догона, сек.")]
        [SerializeField] private float _trailDelay = 0.25f;
        [Tooltip("Скорость догона, доли HP в секунду.")]
        [SerializeField] private float _trailSpeed = 0.8f;

        // Истинная доля (ставится мгновенно) и догоняющая.
        private float _targetFraction = 1f;
        private float _trailFraction  = 1f;
        private float _delayRemaining;

        /// <summary>
        /// Задать цвет основного слоя по принадлежности к смотрящему (из <c>CombatColorPalette</c>).
        /// Пока не вызван — бар использует <see cref="_fallbackColor"/>.
        /// </summary>
        public void SetMainColor(Color color)
        {
            _mainColor    = color;
            _hasMainColor = true;
            Layout();
        }

        /// <summary>Привязать к юниту: оба слоя — на текущую долю мгновенно.</summary>
        public void Bind(RuntimeUnit unit)
        {
            float max = unit.Stats.Get(Data.Stats.StatType.MaxHP);
            _targetFraction = max > 0f ? Mathf.Clamp01(unit.CurrentHP / max) : 0f;
            _trailFraction  = _targetFraction;
            _delayRemaining = 0f;
            Layout();
        }

        /// <summary>Обновить истинную долю HP (поллинг из UnitView каждый кадр).</summary>
        public void UpdateBar(float currentHp, float maxHp)
        {
            float fraction = maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
            if (!Mathf.Approximately(fraction, _targetFraction))
                _delayRemaining = _trailDelay;   // HP изменилось — перезапустить паузу догона
            _targetFraction = fraction;
        }

        // Догон призрака идёт по рендер-времени, не по сим-тику.
        private void Update()
        {
            if (!Mathf.Approximately(_trailFraction, _targetFraction))
            {
                if (_delayRemaining > 0f)
                {
                    _delayRemaining -= Time.deltaTime;
                }
                else
                {
                    _trailFraction = Mathf.MoveTowards(
                        _trailFraction, _targetFraction, _trailSpeed * Time.deltaTime);
                }
            }

            Layout();
        }

        // Спереди — меньшая доля (main, истинный цвет), сзади — большая (trail, цвет по направлению).
        private void Layout()
        {
            float lo = Mathf.Min(_targetFraction, _trailFraction);
            float hi = Mathf.Max(_targetFraction, _trailFraction);

            if (_mainImage != null)
            {
                _mainImage.fillAmount = lo;
                _mainImage.color = _hasMainColor ? _mainColor : _fallbackColor;
            }

            if (_trailImage != null)
            {
                _trailImage.fillAmount = hi;
                _trailImage.color = _targetFraction < _trailFraction
                    ? _damageTrailColor   // потеряли HP
                    : _healTrailColor;    // восстановили HP
            }
        }
    }
}
