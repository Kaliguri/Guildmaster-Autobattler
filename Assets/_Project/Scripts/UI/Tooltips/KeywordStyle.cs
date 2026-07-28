using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Descriptions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Оформление терминов в тексте: полужирный плюс цвет раздела глоссария.
    /// </summary>
    /// <remarks>
    /// <b>Палитра НЕ дублируется в коде.</b> Цвета живут в USS (<c>.gm-kw--*</c>), а rich text читать
    /// USS-переменные не умеет — поэтому значения снимаются с самих правил: в слой кладутся
    /// невидимые элементы-доноры с этими классами, и после первой раскладки их
    /// <c>resolvedStyle.color</c> переводится в hex. Перекрасить глоссарий по-прежнему можно в
    /// одном месте — в теме, не трогая ни строки C#.
    /// <para>Цвет не срезолвился (панель ещё не разложена) — термин выходит просто полужирным.
    /// Это честный фолбэк: лучше без цвета, чем с выдуманным.</para>
    /// </remarks>
    public sealed class KeywordStyle : IKeywordStyle
    {
        private const string Bold = "<b>";
        private const string BoldClose = "</b>";

        private readonly IContentDatabase _content;
        private readonly Dictionary<KeywordCategory, string> _cache = new();
        private VisualElement _probes;

        public KeywordStyle(IContentDatabase content) => _content = content;

        /// <summary>Повесить доноров на слой UI. Без этого стиль отдаёт только полужирный.</summary>
        public void Attach(VisualElement host)
        {
            if (host == null) return;
            Detach();

            _probes = new VisualElement { name = "kw-color-probes", pickingMode = PickingMode.Ignore };
            _probes.style.position = Position.Absolute;
            _probes.style.width = 0;
            _probes.style.height = 0;
            _probes.style.overflow = Overflow.Hidden;

            foreach (KeywordCategory category in System.Enum.GetValues(typeof(KeywordCategory)))
            {
                var probe = new VisualElement { name = "kw-probe-" + category, pickingMode = PickingMode.Ignore };
                probe.AddToClassList(ClassFor(category));
                _probes.Add(probe);
            }

            host.Add(_probes);
            _cache.Clear();
        }

        public void Detach()
        {
            _probes?.RemoveFromHierarchy();
            _probes = null;
            _cache.Clear();
        }

        public string Open(string keywordId)
        {
            string hex = HexFor(CategoryOf(keywordId));
            return string.IsNullOrEmpty(hex) ? Bold : Bold + "<color=#" + hex + ">";
        }

        public string Close(string keywordId)
        {
            string hex = HexFor(CategoryOf(keywordId));
            return string.IsNullOrEmpty(hex) ? BoldClose : "</color>" + BoldClose;
        }

        /// <summary>Класс-донор раздела: он же задаёт цвет термина в теме.</summary>
        public static string ClassFor(KeywordCategory category) => category switch
        {
            KeywordCategory.Status    => "gm-kw--status",
            KeywordCategory.Damage    => "gm-kw--damage",
            KeywordCategory.Defense   => "gm-kw--defense",
            KeywordCategory.Behaviour => "gm-kw--behaviour",
            KeywordCategory.Run       => "gm-kw--run",
            _                         => "gm-kw--other",
        };

        private KeywordCategory CategoryOf(string keywordId)
        {
            if (_content != null && _content.TryGet(KeywordMarkup.FullId(keywordId), out KeywordDefinition kw) && kw != null)
                return kw.Category;
            return KeywordCategory.Other;
        }

        private string HexFor(KeywordCategory category)
        {
            if (_cache.TryGetValue(category, out string cached)) return cached;
            if (_probes == null) return null;

            foreach (VisualElement probe in _probes.Children())
            {
                if (!probe.ClassListContains(ClassFor(category))) continue;

                Color color = probe.resolvedStyle.color;
                // До первой раскладки цвет приходит нулевым — не кешируем, попробуем в следующий раз.
                if (color.a <= 0f) return null;

                string hex = ColorUtility.ToHtmlStringRGB(color);
                _cache[category] = hex;
                return hex;
            }
            return null;
        }
    }
}
