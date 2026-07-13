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

        [Header("Feel — поп-масштаб и боковой разлёт (сочность цифры)")]
        [Tooltip("Длительность поп-масштаба на спавне, сек (0 = без попа, сразу целевой размер).")]
        [SerializeField] private float _popDuration = 0.14f;
        [Tooltip("Пик перелёта попа (OutBack): 0.9 ≈ вырастает выше цели и оседает. 0 = без перелёта.")]
        [SerializeField] private float _popOvershoot = 0.9f;
        [Tooltip("Боковой разлёт за жизнь, мировые ед. (± случайно) — чтобы совпавшие цифры не слипались. 0 = строго вверх.")]
        [SerializeField] private float _spreadX = 0.35f;

        private TMP_Text _text;
        private Vector3  _startPosition;
        private Color    _baseColor;
        private float    _elapsed;
        private System.Action<FloatingText> _onComplete;

        private Vector3 _baseScale = Vector3.one; // масштаб префаба (снимаем один раз) — база для поп/величины
        private bool    _baseScaleCaptured;
        private Vector3 _targetScale = Vector3.one; // база × множитель величины удара (куда оседает поп)
        private float   _driftX;                    // боковой разлёт этого экземпляра

        /// <summary>Заспавнить и проиграть цифру из префаба над мировой точкой. Префаб должен нести <see cref="FloatingText"/>.</summary>
        public static void Spawn(GameObject prefab, Transform parent, Vector3 worldPosition, string text, Color color)
        {
            if (prefab == null) return;
            GameObject go = Instantiate(prefab, worldPosition, Quaternion.identity, parent);
            if (go.TryGetComponent(out FloatingText instance)) instance.Play(text, color);
            else Destroy(go);
        }

        /// <summary>Задать текст, цвет и запустить анимацию.</summary>
        public void Play(string text, Color color) => Play(text, color, 1f, null);

        /// <summary>Пул-версия без множителя величины (масштаб = базовый префабный).</summary>
        public void Play(string text, Color color, System.Action<FloatingText> onComplete)
            => Play(text, color, 1f, onComplete);

        /// <summary>
        /// Пул-версия: по завершении зовёт <paramref name="onComplete"/> (возврат в пул) вместо Destroy.
        /// Позицию выставляет вызывающий ДО вызова. <paramref name="sizeScale"/> — множитель размера по
        /// величине удара (1 = базовый; крупный урон = крупнее цифра), поверх префабного масштаба.
        /// </summary>
        public void Play(string text, Color color, float sizeScale, System.Action<FloatingText> onComplete)
        {
            _onComplete = onComplete;
            if (_text == null) _text = GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (_text == null) { Finish(); return; }

            // База масштаба = префабный localScale, снятый до первого поп-эффекта (иначе перезатёрли бы нулём).
            if (!_baseScaleCaptured) { _baseScale = transform.localScale; _baseScaleCaptured = true; }

            _text.text     = text;
            _text.color    = color;
            _baseColor     = color;
            _startPosition = transform.position;
            _elapsed       = 0f;
            _targetScale   = _baseScale * Mathf.Max(0.01f, sizeScale);
            _driftX        = _spreadX > 0f ? Random.Range(-_spreadX, _spreadX) : 0f;

            // Старт попа с нуля (вырастает); без попа — сразу целевой.
            transform.localScale = _popDuration > 0f ? Vector3.zero : _targetScale;
        }

        private void Update()
        {
            if (_text == null) return;

            _elapsed += Time.deltaTime;
            float t = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

            // Поп-масштаб на старте (OutBack — вырастает выше цели и оседает); затем держим целевой.
            if (_popDuration > 0f)
            {
                float p = Mathf.Clamp01(_elapsed / _popDuration);
                transform.localScale = Vector3.LerpUnclamped(Vector3.zero, _targetScale, EaseOutBack(p, _popOvershoot));
            }

            // Всплытие с замедлением (ease-out cubic) + боковой разлёт.
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            transform.position = _startPosition + new Vector3(_driftX * eased, _floatHeight * eased, 0f);

            // Затухание после _fadeStart.
            Color c = _baseColor;
            c.a = _fadeStart < 1f ? 1f - Mathf.Clamp01((t - _fadeStart) / (1f - _fadeStart)) : 1f;
            _text.color = c;

            if (t >= 1f) Finish();
        }

        // easeOutBack: 0→(перелёт выше 1)→1. overshoot масштабирует перелёт (0 = обычный ease-out без перелёта).
        private static float EaseOutBack(float x, float overshoot)
        {
            float c1 = Mathf.Max(0f, overshoot);
            float c3 = c1 + 1f;
            float p  = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        /// <summary>Завершение: вернуть в пул, если задан колбэк; иначе уничтожить (не-пул путь).</summary>
        private void Finish()
        {
            if (_onComplete != null) _onComplete(this);
            else Destroy(gameObject);
        }
    }
}
