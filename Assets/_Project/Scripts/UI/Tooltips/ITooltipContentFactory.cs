using UnityEngine.UIElements;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Сборка содержимого тултипа по запросу (план §II.10.5 п.3). Возвращает элемент, а не строку:
    /// в окно должен уметь встать не только текст, но и стат-строки, а позже — флипбук-демо (II.11).
    /// </summary>
    public interface ITooltipContentFactory
    {
        /// <summary>
        /// Построить содержимое. <paramref name="detailed"/> — подробный режим (Shift, §II.10.4).
        /// <c>null</c> = показывать нечего, окно не открывается.
        /// </summary>
        VisualElement Build(TooltipRequest request, bool detailed);

        /// <summary>
        /// Содержимое живое — числа в нём могут измениться, пока окно открыто (бой идёт).
        /// Только для таких система гоняет рефреш раз в 0.5 с (§II.10.5 п.5); статичный текст
        /// перерисовывать дважды в секунду незачем.
        /// </summary>
        bool IsLive(TooltipRequest request);
    }
}
