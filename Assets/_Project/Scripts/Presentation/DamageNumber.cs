using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>Один всплывающий элемент урона: базовый UI Text + цвет и альфа.</summary>
    public sealed class DamageNumber : MonoBehaviour
    {
        [SerializeField] private Component _text;
        [SerializeField] private CanvasGroup _canvasGroup;

        private Color _color = Color.white;

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                if (_text != null)
                {
                    var textType = _text.GetType();
                    var colorProp = textType.GetProperty("color");
                    if (colorProp != null && colorProp.CanWrite)
                    {
                        colorProp.SetValue(_text, value);
                    }
                }
            }
        }

        public float Alpha
        {
            get => _canvasGroup != null ? _canvasGroup.alpha : _color.a;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (_canvasGroup != null) _canvasGroup.alpha = clamped;
                var next = _color;
                next.a = clamped;
                Color = next;
            }
        }

        public void SetText(string value)
        {
            if (_text == null) return;

            var textType = _text.GetType();
            var textProp = textType.GetProperty("text");
            if (textProp != null && textProp.CanWrite)
            {
                textProp.SetValue(_text, value);
            }
        }

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_text != null)
            {
                var textType = _text.GetType();
                var colorProp = textType.GetProperty("color");
                if (colorProp != null && colorProp.CanRead)
                {
                    object raw = colorProp.GetValue(_text);
                    if (raw is Color color) _color = color;
                }
            }
        }
    }
}
