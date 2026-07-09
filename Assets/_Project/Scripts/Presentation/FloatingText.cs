using TMPro;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Своя всплывающая боевая цифра (урон/хил) — один общий префаб с TMP-текстом. Всплывает вверх,
    /// во второй половине жизни гаснет, затем сам себя уничтожает. Размер/шрифт настраиваются на TMP
    /// в префабе; <b>цвет задаёт вызывающий</b> (<see cref="CombatPresenter"/> знает семантику: урон/хил),
    /// тайминг всплытия/затухания — поля ниже. Чисто презентация: сим не трогает.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class FloatingText : MonoBehaviour
    {
        [Header("Тайминг (внешний вид — на TMP-компоненте префаба)")]
        [Tooltip("Высота всплытия за жизнь, мировые единицы.")]
        [SerializeField] private float _floatHeight = 1.1f;
        [Tooltip("Длительность жизни, сек.")]
        [SerializeField] private float _duration = 0.8f;
        [Tooltip("Доля жизни [0..1], после которой начинается затухание.")]
        [SerializeField, Range(0f, 1f)] private float _fadeStart = 0.5f;

        private TMP_Text _text;
        private Vector3  _startPosition;
        private Color    _baseColor;
        private float    _elapsed;

        /// <summary>Заспавнить и проиграть цифру из префаба над мировой точкой. Префаб должен нести <see cref="FloatingText"/>.</summary>
        public static void Spawn(GameObject prefab, Transform parent, Vector3 worldPosition, string text, Color color)
        {
            if (prefab == null) return;
            GameObject go = Instantiate(prefab, worldPosition, Quaternion.identity, parent);
            if (go.TryGetComponent(out FloatingText instance)) instance.Play(text, color);
            else Destroy(go);
        }

        /// <summary>Задать текст, цвет и запустить анимацию.</summary>
        public void Play(string text, Color color)
        {
            _text = GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (_text == null) { Destroy(gameObject); return; }
            _text.text     = text;
            _text.color    = color;
            _baseColor     = color;
            _startPosition = transform.position;
            _elapsed       = 0f;
        }

        private void Update()
        {
            if (_text == null) return;

            _elapsed += Time.deltaTime;
            float t = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

            // Всплытие с замедлением (ease-out cubic).
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            transform.position = _startPosition + Vector3.up * (_floatHeight * eased);

            // Затухание после _fadeStart.
            Color c = _baseColor;
            c.a = _fadeStart < 1f ? 1f - Mathf.Clamp01((t - _fadeStart) / (1f - _fadeStart)) : 1f;
            _text.color = c;

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
