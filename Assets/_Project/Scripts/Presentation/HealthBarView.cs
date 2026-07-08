using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Двойной HP-бар (истинный + догоняющий «призрак», как в Dark Souls / MOBA).
    /// Истинная доля ставится мгновенно поллингом из <see cref="UnitView"/>; призрак
    /// плавно догоняет её по рендер-времени (НЕ сим — на чек-сумму не влияет).
    ///
    /// Каждый кадр спереди рисуется МЕНЬШАЯ из двух долей (main, зелёный), сзади — БОЛЬШАЯ
    /// (trail), поэтому видна цветная «дельта»:
    ///   • урон  (target &lt; ghost): дельта [target, ghost] цветом <see cref="_damageTrailColor"/>;
    ///   • хил    (target &gt; ghost): дельта [ghost, target] цветом <see cref="_healTrailColor"/>.
    ///
    /// Левый якорь считается в коде (offset по X от ширины спрайта), поэтому полоса «утекает»
    /// слева независимо от пивота спрайта — без жёсткой привязки к настройке ассета.
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [Header("Слои (X-scale = доля [0..1])")]
        [Tooltip("Передний слой — истинное HP (зелёный/красный).")]
        [SerializeField] private Transform      _mainFill;
        [SerializeField] private SpriteRenderer _mainRenderer;
        [Tooltip("Задний слой — догоняющий «призрак».")]
        [SerializeField] private Transform      _trailFill;
        [SerializeField] private SpriteRenderer _trailRenderer;

        [Header("Цвета main (истинное HP)")]
        [SerializeField] private Color _fullColor      = new Color(0.2f, 0.9f, 0.2f);
        [SerializeField] private Color _lowColor       = new Color(0.9f, 0.2f, 0.2f);
        [Tooltip("Порог низкого HP [0,1] для перехода в _lowColor.")]
        [SerializeField] private float _lowHpThreshold = 0.3f;

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

        // Полная ширина fill при scale.x = 1, в локальных единицах родителя (для левого якоря).
        private float _fullWidth = 1f;

        private void Awake()
        {
            // Авто-калибровка ширины по спрайту (центр-пивот) — левый якорь без ручной настройки.
            if (_mainRenderer != null && _mainRenderer.sprite != null)
                _fullWidth = _mainRenderer.sprite.bounds.size.x;
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

            SetFill(_mainFill, lo);
            SetFill(_trailFill, hi);

            if (_mainRenderer != null)
                _mainRenderer.color = Color.Lerp(_lowColor, _fullColor,
                    Mathf.InverseLerp(0f, _lowHpThreshold, _targetFraction));

            if (_trailRenderer != null)
                _trailRenderer.color = _targetFraction < _trailFraction
                    ? _damageTrailColor   // потеряли HP
                    : _healTrailColor;    // восстановили HP
        }

        // Масштаб по X = доля; позиция по X сдвигается так, чтобы левый край стоял на месте
        // (компенсация центр-пивота): center = _fullWidth * (fraction - 1) / 2.
        private void SetFill(Transform fill, float fraction)
        {
            if (fill == null) return;

            Vector3 scale = fill.localScale;
            scale.x = Mathf.Max(0f, fraction);
            fill.localScale = scale;

            Vector3 pos = fill.localPosition;
            pos.x = _fullWidth * (fraction - 1f) * 0.5f;
            fill.localPosition = pos;
        }
    }
}
