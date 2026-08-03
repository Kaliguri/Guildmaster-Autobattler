using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Ключевые слова в тексте: наведение на подчёркнутый термин открывает подсказку-статью
    /// (план §II.10.5 п.4).
    /// </summary>
    /// <remarks>
    /// Единственное место в проекте, которое знает про <c>UnityEngine.UIElements.Experimental</c>:
    /// события ссылок пока экспериментальные, и их сигнатура может поменяться. За фасадом смена
    /// стоит правки одного файла, а без него разъехалась бы по всем экранам с описаниями
    /// (ограничения проверены 2026-07-20, план §II.10.5).
    /// </remarks>
    public static class KeywordLinks
    {
        /// <summary>
        /// Включить подсказки по ссылкам в тексте элемента. Rich text обязателен — без него UITK
        /// не разбирает <c>&lt;link&gt;</c> и событий не будет.
        /// </summary>
        public static T WithKeywordTooltips<T>(this T text) where T : TextElement
        {
            if (text == null) return text;
            text.enableRichText = true;

            text.RegisterCallback<PointerOverLinkTagEvent>(e =>
            {
                TooltipRequest request = TooltipRequest.Keyword(e.linkID);
                if (request.IsEmpty) return;
                using (TooltipShowEvent show = TooltipShowEvent.GetPooled(request, text))
                {
                    show.target = text;
                    text.SendEvent(show);
                }
            });

            text.RegisterCallback<PointerOutLinkTagEvent>(_ =>
            {
                using (TooltipHideEvent hide = TooltipHideEvent.GetPooled(text))
                {
                    hide.target = text;
                    text.SendEvent(hide);
                }
            });

            return text;
        }
    }
}
