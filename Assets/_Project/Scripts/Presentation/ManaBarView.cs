using Guildmaster.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Бар ресурса (мана/ярость) — по образцу <see cref="HealthBarView"/>, но для
    /// <see cref="RuntimeUnit.CurrentResource"/> / <see cref="Data.Stats.StatType.MaxResource"/>.
    /// Истинная доля ставится мгновенно поллингом из <see cref="UnitView"/>; «призрак» плавно догоняет
    /// по рендер-времени (НЕ сим — на чек-сумму не влияет), показывая свежую трату/добор ресурса.
    ///
    /// В отличие от HP: нет «низкого» красного порога (мало ресурса — не опасность, просто мало) и бар
    /// целиком скрывается для юнитов без ресурса (<c>MaxResource ≤ 0</c> — болванчики, безресурсные реликвии).
    /// </summary>
    public sealed class ManaBarView : MonoBehaviour
    {
        [Header("Слои (Image Filled Horizontal, origin Left; fillAmount = доля [0..1])")]
        [Tooltip("Передний слой — истинный ресурс.")]
        [SerializeField] private Image _mainImage;
        [Tooltip("Задний слой — догоняющий «призрак».")]
        [SerializeField] private Image _trailImage;

        [Header("Цвета")]
        [Tooltip("Основной цвет ресурса.")]
        [SerializeField] private Color _fillColor = new Color(0.3f, 0.55f, 1f);
        [Tooltip("Дельта недавно потраченного ресурса (трата — призрак впереди истинного).")]
        [SerializeField] private Color _spendTrailColor = new Color(0.55f, 0.75f, 1f);
        [Tooltip("Дельта недавно добранного ресурса (добор — истинный впереди призрака).")]
        [SerializeField] private Color _gainTrailColor  = new Color(0.7f, 0.95f, 1f);

        [Header("Анимация догона")]
        [Tooltip("Пауза перед стартом догона, сек.")]
        [SerializeField] private float _trailDelay = 0.15f;
        [Tooltip("Скорость догона, доли ресурса в секунду.")]
        [SerializeField] private float _trailSpeed = 1.2f;

        // Истинная доля (ставится мгновенно) и догоняющая.
        private float _targetFraction = 1f;
        private float _trailFraction  = 1f;
        private float _delayRemaining;

        /// <summary>Привязать к юниту: скрыть для безресурсных, иначе оба слоя — на текущую долю мгновенно.</summary>
        public void Bind(RuntimeUnit unit)
        {
            float max = unit.Stats.Get(Data.Stats.StatType.MaxResource);
            bool hasResource = max > 0f;
            gameObject.SetActive(hasResource);
            if (!hasResource) return;

            _targetFraction = Mathf.Clamp01(unit.CurrentResource / max);
            _trailFraction  = _targetFraction;
            _delayRemaining = 0f;
            Layout();
        }

        /// <summary>Обновить истинную долю ресурса (поллинг из UnitView каждый кадр).</summary>
        public void UpdateBar(float current, float max)
        {
            if (max <= 0f) return;
            float fraction = Mathf.Clamp01(current / max);
            if (!Mathf.Approximately(fraction, _targetFraction))
                _delayRemaining = _trailDelay;   // ресурс изменился — перезапустить паузу догона
            _targetFraction = fraction;
        }

        // Догон призрака идёт по рендер-времени, не по сим-тику.
        private void Update()
        {
            if (!Mathf.Approximately(_trailFraction, _targetFraction))
            {
                if (_delayRemaining > 0f)
                    _delayRemaining -= Time.deltaTime;
                else
                    _trailFraction = Mathf.MoveTowards(
                        _trailFraction, _targetFraction, _trailSpeed * Time.deltaTime);
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
                _mainImage.color = _fillColor;
            }

            if (_trailImage != null)
            {
                _trailImage.fillAmount = hi;
                _trailImage.color = _targetFraction < _trailFraction
                    ? _spendTrailColor   // потратили ресурс
                    : _gainTrailColor;   // добрали ресурс
            }
        }
    }
}
