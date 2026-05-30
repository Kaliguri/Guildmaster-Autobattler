using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// HP-бар через масштаб fill-объекта, без сторонних зависимостей.
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [Tooltip("Fill transform with local X scale in [0..1].")]
        [SerializeField] private Transform _fill;
        [Tooltip("Цвет заполненного HP.")]
        [SerializeField] private Color _fullColor  = new Color(0.2f, 0.9f, 0.2f);
        [Tooltip("Цвет при низком HP.")]
        [SerializeField] private Color _lowColor   = new Color(0.9f, 0.2f, 0.2f);
        [Tooltip("Порог низкого HP [0, 1].")]
        [SerializeField] private float _lowHpThreshold = 0.3f;
        [SerializeField] private SpriteRenderer _fillRenderer;

        private float  _currentFraction = 1f;

        /// <summary>Привязать к юниту для первоначальной отрисовки.</summary>
        public void Bind(RuntimeUnit unit)
        {
            float max = unit.Stats.Get(Data.Stats.StatType.MaxHP);
            UpdateBar(unit.CurrentHP, max);
        }

        /// <summary>Обновить отображаемую долю HP.</summary>
        public void UpdateBar(float currentHp, float maxHp)
        {
            float fraction = maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
            _currentFraction = fraction;

            if (_fill != null)
            {
                var scale = _fill.localScale;
                scale.x = Mathf.Max(0f, fraction);
                _fill.localScale = scale;
            }

            if (_fillRenderer != null)
            {
                _fillRenderer.color = Color.Lerp(_lowColor, _fullColor,
                    Mathf.InverseLerp(0f, _lowHpThreshold, fraction));
            }
        }
    }
}
