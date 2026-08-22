using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Панель сообщества в главном меню: обращение, ссылки, приглашение в список желаемого.
    /// </summary>
    /// <remarks>
    /// <b>Правая сторона кадра — приём обоих рефов</b> (заказ Макса 22.08.2026). Замер по кадрам
    /// 1280x720: у Guildrun панель 20.7% ширины x 50% высоты в правом верхнем углу плюс отдельный
    /// блок желаемого внизу; у Heroes панель 19% x 41% ниже середины плюс ряд значков под ней.
    /// Наша раскладка берёт от обоих то, что у них общее: панель-обращение, ряд ссылок, просьба
    /// отдельным блоком.
    /// <para><b>Ссылка без адреса не показывается.</b> В ассете сообщества заготовки под каналы,
    /// которых ещё нет, лежат заранее — но значок, ведущий в никуда, хуже отсутствия значка: игрок
    /// жмёт и ничего не происходит.</para>
    /// <para><b>Панель ничего не знает про Steam.</b> Открыть ссылку просят у службы за интерфейсом:
    /// оверлей это будет или браузер — не вопрос UI.</para>
    /// </remarks>
    public static class CommunityPanelView
    {
        /// <param name="root">Корень меню — панель ищется внутри по имени.</param>
        /// <param name="config">Ассет сообщества. <c>null</c> — блока не будет вовсе.</param>
        /// <param name="onLink">Открыть адрес ссылки.</param>
        /// <param name="onWishlist">Открыть страницу игры в магазине.</param>
        /// <param name="showWishlist">
        /// Внешнее слово про желаемое: даже когда ассет разрешает, показывать блок бессмысленно там,
        /// где страницы магазина нет (например, в превью-стенде).
        /// </param>
        public static void Fill(
            VisualElement root,
            CommunityConfig config,
            Func<string, string> localize,
            Action<string> onLink = null,
            Action onWishlist = null,
            bool showWishlist = true)
        {
            if (root == null) return;

            var block = root.Q<VisualElement>("community");
            if (block == null) return;

            // Ассета нет — панели нет. Это законное состояние: игра запускается и без сообщества,
            // а пустая рамка справа читалась бы как «не догрузилось».
            if (config == null)
            {
                block.style.display = DisplayStyle.None;
                return;
            }

            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            var headline = block.Q<Label>("community-headline");
            var body     = block.Q<VisualElement>("community-body");
            var links    = block.Q<VisualElement>("community-links");
            var bug      = block.Q<Button>("btn-bugreport");
            var wishlist = block.Q<VisualElement>("community-wishlist");

            var button   = block.Q<Button>("btn-wishlist");

            if (headline != null) headline.text = L(config.HeadlineKey, config.HeadlineFallback);

            FillBody(body, config.Paragraphs, L);
            FillLinks(links, config.Links, L, onLink);

            // «Отчёт об ошибке» ПОКА ПОГАШЕН (заказ Макса 22.08.2026: «завести кнопку, сделать не
            // активной… но они у нас БУДУТ»). Собирать отчёт игра будет сама — своим окном с
            // описанием, версией и логом; до тех пор кнопка занимает своё место и молчит.
            if (bug != null)
            {
                bug.text = L("ui.community.bugreport", "Отчёт об ошибке");
                bug.SetEnabled(false);
            }

            bool wish = showWishlist && config.ShowWishlist;
            if (wishlist != null) wishlist.style.display = wish ? DisplayStyle.Flex : DisplayStyle.None;

            if (wish)
            {
                if (button != null)
                {
                    button.text = L("ui.community.wishlist.button", "В список желаемого");
                    button.clicked += () => onWishlist?.Invoke();
                }
            }
        }

        /// <summary>Абзацы обращения — каждый своей строкой, тихим цветом.</summary>
        private static void FillBody(VisualElement body,
                                     IReadOnlyList<CommunityConfig.Paragraph> paragraphs,
                                     Func<string, string, string> L)
        {
            if (body == null) return;
            body.Clear();

            for (int i = 0; paragraphs != null && i < paragraphs.Count; i++)
            {
                CommunityConfig.Paragraph p = paragraphs[i];
                string text = L(p.Key, p.Fallback);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var line = new Label(text) { pickingMode = PickingMode.Ignore };
                line.AddToClassList("gm-text-caption");
                line.AddToClassList("gm-text--muted");
                line.AddToClassList("gm-community__paragraph");
                body.Add(line);
            }
        }

        /// <summary>
        /// Ряд значков: по значку на канал. Канал без адреса ПОКАЗЫВАЕТСЯ погашенным.
        /// </summary>
        /// <remarks>
        /// Прятать было первым решением и оказалось хуже: «Я вообще НЕ увидел где тут иконки и места
        /// для сообщение об ошибке и youtube» (Макс, 22.08.2026). Пустое место не говорит ничего, а
        /// погашенный значок говорит «канал будет здесь» — ровно как неактивная кнопка компендиума
        /// в колонке слева.
        /// </remarks>
        private static void FillLinks(VisualElement row,
                                      IReadOnlyList<CommunityConfig.LinkEntry> entries,
                                      Func<string, string, string> L,
                                      Action<string> onLink)
        {
            if (row == null) return;
            row.Clear();

            int shown = 0;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                CommunityConfig.LinkEntry entry = entries[i];

                string url = entry.Url;
                bool live = !string.IsNullOrWhiteSpace(url);
                var link = new Components.PlateButton { name = "btn-link-" + entry.Id };
                link.AddToClassList("gm-button");
                link.AddToClassList("gm-button--icon");
                link.tooltip = L(entry.LabelKey, entry.LabelFallback);
                link.SetEnabled(live);
                if (live) link.clicked += () => onLink?.Invoke(url);

                // Значок — ребёнок, а не background кнопки: фон пластины рисует сам контрол мешем,
                // и картинка в background-image ушла бы ПОД него.
                //
                // ВЕКТОР, а не текстура: встроенный импортёр SVG отдаёт VectorImage (svgType 3), и он
                // же единственный формат, который тянется на любой кегль без размытия. Тинтом такую
                // картинку красит сам UITK — цвет живёт в теме, не здесь.
                if (entry.Icon != null)
                {
                    var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                    mark.AddToClassList("gm-button__icon");
                    mark.style.backgroundImage = new StyleBackground(entry.Icon);
                    link.Add(mark);
                }

                row.Add(link);
                shown++;
            }

            // Ряд прячется, только если каналов нет В САМОМ АССЕТЕ: показывать нечего вовсе. Пустой
            // адрес — не это; такой значок стоит погашенным.
            row.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
