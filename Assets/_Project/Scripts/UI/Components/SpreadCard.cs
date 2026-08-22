using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Разворот книги: окно поверх экрана с видимой кромкой подложки, шапкой, лентой табов и двумя
    /// страницами, разделёнными корешком.
    /// </summary>
    /// <remarks>
    /// <b>Форма задана Максом 22.08.2026</b> и общая у карточки «Сосуда» и карточки Реликвии: «не надо
    /// все в одну умещать. Сверху должны быть табы». Поэтому каркас — компонент, а содержимое приходит
    /// колбэком: два похожих окна, написанных врозь, разошлись бы на первой правке.
    /// <para><b>Кромка подложки видна намеренно</b> — окно занимает 86% кадра. Край говорит «ты
    /// заглянул, а не ушёл с экрана»: закрыл и вернулся туда же, откуда пришёл.</para>
    /// <para><b>Корешок — линия, а не зазор.</b> Зазор читался бы как два окна рядом, а это одно, и
    /// переход между страницами должен выглядеть перелистыванием.</para>
    /// </remarks>
    public static class SpreadCard
    {
        public const string RootClass    = "gm-spread";
        public const string TabClass     = "gm-spread__tab";
        public const string TabOnClass   = "gm-spread__tab--on";
        public const string PageClass    = "gm-spread__page";
        public const string SpineClass   = "gm-spread__spine";
        public const string SectionClass = "gm-spread__section";

        /// <summary>Наполнение одной страницы разворота.</summary>
        public delegate void FillPage(VisualElement left, VisualElement right);

        /// <param name="tabs">Подписи табов по порядку.</param>
        /// <param name="active">Какой таб открыт.</param>
        /// <param name="fill">Заполняет левую и правую страницы для активного таба.</param>
        /// <param name="onTab">Смена таба: экран пересобирает карточку с другим индексом.</param>
        /// <param name="onClose">Закрытие (Esc, кнопка, повторный ПКМ).</param>
        public static VisualElement Build(
            string title,
            string subtitle,
            IReadOnlyList<string> tabs,
            int active,
            FillPage fill,
            Action<int> onTab = null,
            Action onClose = null)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClass);

            var head = new VisualElement();
            head.AddToClassList("gm-spread__head");
            head.Add(Label(title, "gm-spread__title"));
            if (!string.IsNullOrEmpty(subtitle)) head.Add(Label(subtitle, "gm-spread__subtitle"));
            root.Add(head);

            var close = new PlateButton { text = "Esc" };
            close.AddToClassList("gm-spread__close");
            close.clicked += () => onClose?.Invoke();
            head.Add(close);

            if (tabs != null && tabs.Count > 0)
            {
                var bar = new VisualElement();
                bar.AddToClassList("gm-spread__tabs");
                for (int i = 0; i < tabs.Count; i++)
                {
                    int index = i;
                    var tab = new PlateButton { text = tabs[i] };
                    tab.AddToClassList(TabClass);
                    tab.EnableInClassList(TabOnClass, i == active);
                    tab.clicked += () => onTab?.Invoke(index);
                    bar.Add(tab);
                }
                root.Add(bar);
            }

            var body = new VisualElement();
            body.AddToClassList("gm-spread__body");

            var left = new VisualElement();
            left.AddToClassList(PageClass);
            var spine = new VisualElement();
            spine.AddToClassList(SpineClass);
            var right = new VisualElement();
            right.AddToClassList(PageClass);

            body.Add(left);
            body.Add(spine);
            body.Add(right);
            root.Add(body);

            fill?.Invoke(left, right);
            return root;
        }

        /// <summary>Заголовок секции внутри страницы: подпись без рамки — «рамка в рамке» запрещена языком.</summary>
        public static Label Section(string title) => Label(title, SectionClass);

        /// <summary>Клетка-секция: своя рамка, свой заголовок. Раскладка «Клетки на развороте».</summary>
        public static VisualElement Cell(string title)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gm-spread__cell");
            cell.Add(Label(title, SectionClass));
            return cell;
        }

        public static Label Label(string text, string className)
        {
            var label = new UnityEngine.UIElements.Label(text ?? string.Empty);
            if (!string.IsNullOrEmpty(className)) label.AddToClassList(className);
            return label;
        }
    }
}
