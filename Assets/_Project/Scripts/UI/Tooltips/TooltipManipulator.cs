using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Декларативное «у этого элемента есть тултип» (план §II.10.5 п.2). Ловит наведение и просит
    /// систему показать запрос; задержка, grace и позиционирование живут в системе — здесь только жест.
    /// </summary>
    /// <remarks>
    /// Запрос берётся функцией, а не фиксируется при навешивании: подпись свёрнутого хвоста тегов
    /// («+3») меняется на лету при пересборке ряда, и запомненный при создании запрос успел бы
    /// протухнуть к первому же наведению.
    /// </remarks>
    public sealed class TooltipManipulator : Manipulator
    {
        private readonly Func<TooltipRequest> _request;

        public TooltipManipulator(Func<TooltipRequest> request) => _request = request;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerEnterEvent>(OnEnter);
            target.RegisterCallback<PointerLeaveEvent>(OnLeave);
            // Показ по ФОКУСУ (план §II.10.5 п.7): для клавиатуры и геймпада «навести» нечем, и без
            // этой пары подсказки существовали бы только для мыши.
            target.RegisterCallback<FocusInEvent>(OnFocusIn);
            target.RegisterCallback<FocusOutEvent>(OnFocusOut);
            // Снятый с панели элемент курсор не покинет — событие Leave до него уже не дойдёт, и окно
            // осталось бы висеть после закрытия экрана.
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerEnterEvent>(OnEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnLeave);
            target.UnregisterCallback<FocusInEvent>(OnFocusIn);
            target.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        private void OnFocusIn(FocusInEvent _) => Show();

        private void OnFocusOut(FocusOutEvent _) => SendHide();

        private void OnEnter(PointerEnterEvent _) => Show();

        private void Show()
        {
            TooltipRequest request = _request != null ? _request() : default;
            if (request.IsEmpty) return;
            using (TooltipShowEvent e = TooltipShowEvent.GetPooled(request, target))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        private void OnLeave(PointerLeaveEvent _) => SendHide();

        private void OnDetach(DetachFromPanelEvent _) => SendHide();

        private void SendHide()
        {
            using (TooltipHideEvent e = TooltipHideEvent.GetPooled(target))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }
    }

    /// <summary>Синтаксис навешивания: <c>card.WithTooltip(TooltipRequest.Relic(relic.Id))</c>.</summary>
    public static class TooltipElementExtensions
    {
        /// <summary>Постоянный запрос: контент элемента не меняется за его жизнь.</summary>
        public static T WithTooltip<T>(this T element, TooltipRequest request) where T : VisualElement
        {
            if (element == null || request.IsEmpty) return element;
            element.AddManipulator(new TooltipManipulator(() => request));
            return element;
        }

        /// <summary>Ленивый запрос: содержимое элемента пересобирается (грид, свёрнутые теги).</summary>
        public static T WithTooltip<T>(this T element, Func<TooltipRequest> request) where T : VisualElement
        {
            if (element == null || request == null) return element;
            element.AddManipulator(new TooltipManipulator(request));
            return element;
        }
    }
}
