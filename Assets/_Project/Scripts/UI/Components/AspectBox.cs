using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Блок, который сам держит соотношение сторон: высота выводится из фактической ширины.
    /// <para>
    /// Зачем: в UI Toolkit нет <c>aspect-ratio</c>, и до этого контрола пропорции считались руками —
    /// в USS стояло <c>width: 320px; height: 180px;</c> с комментарием «320 × 9/16 = 180».
    /// Такой договор живёт в комментарии, а не в механике: стоило поменять ширину панели, и высоту
    /// приходилось пересчитывать заново (а пока не пересчитал — вставка переставала быть 16:9).
    /// Здесь ширину задаёт вёрстка, высоту — контрол, и пропорция не может разъехаться.
    /// </para>
    /// <example><code>
    /// &lt;gm:AspectBox Aspect="16:9" class="gm-loadout__video" /&gt;
    /// </code></example>
    /// </summary>
    [UxmlElement]
    public partial class AspectBox : VisualElement
    {
        /// <summary>Порог в пикселях, ниже которого высота не переписывается.</summary>
        /// <remarks>
        /// Присваивание высоты внутри обработчика геометрии само вызывает новый прогон лэйаута.
        /// Без порога это дало бы бесконечное дрожание на дробных значениях; с ним — сходится за шаг.
        /// </remarks>
        private const float Epsilon = 0.5f;

        private const float DefaultRatio = 16f / 9f;

        private float _ratio = DefaultRatio;
        private string _aspect = "16:9";

        /// <summary>
        /// Соотношение в виде «ширина:высота» — например <c>16:9</c>, <c>4:3</c>, <c>1:1</c>.
        /// Записано словами, а не числом, чтобы в разметке читалось намерение, а не 1.7777.
        /// Нераспознанное значение откатывается к 16:9 и пишет предупреждение.
        /// </summary>
        [UxmlAttribute]
        public string Aspect
        {
            get => _aspect;
            set
            {
                _aspect = value;
                _ratio = Parse(value);
                ApplyHeight(resolvedStyle.width);
            }
        }

        public AspectBox()
        {
            // Инвариант контрола, а не оформление: с flex-shrink по умолчанию (1) родитель ужимает
            // блок по высоте, и посчитанная пропорция молча теряется — на 420px ширины получалось
            // 220 вместо 236 (1.91 вместо 16:9). Держать это в USS нельзя: любой экран, забывший
            // строчку, снова получил бы «почти 16:9».
            style.flexShrink = 0f;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => ApplyHeight(evt.newRect.width);

        private void ApplyHeight(float width)
        {
            if (float.IsNaN(width) || width <= 0f) return;

            float target = width / _ratio;

            // Сравниваем с УЖЕ НАЗНАЧЕННОЙ высотой, а не с resolvedStyle: resolved учитывает
            // ограничения родителя, и на нём порог срабатывал бы вхолостую.
            // Заданному числу UITK оставляет keyword == Undefined, поэтому «есть значение» проверяем
            // по StyleKeyword.Null (его выставляет сброс), а не наоборот.
            bool hasValue = style.height.keyword == StyleKeyword.Undefined;
            if (hasValue && Mathf.Abs(style.height.value.value - target) < Epsilon) return;

            style.height = target;
        }

        private static float Parse(string aspect)
        {
            if (!string.IsNullOrWhiteSpace(aspect))
            {
                string[] parts = aspect.Split(':');
                if (parts.Length == 2
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float w)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float h)
                    && w > 0f && h > 0f)
                {
                    return w / h;
                }
            }

            Debug.LogWarning($"AspectBox: не разобрано соотношение '{aspect}', ожидается «ширина:высота» (например 16:9). Взято 16:9.");
            return DefaultRatio;
        }
    }
}
