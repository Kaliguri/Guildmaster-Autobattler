using UnityEngine.UIElements;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// «Наведено на элемент, у которого есть тултип» — всплывающее событие панели.
    /// </summary>
    /// <remarks>
    /// Доставка событием, а не ссылкой на систему, выбрана из-за того, как собраны наши экраны:
    /// они статические <c>Build(...)</c>-методы с делегатами и живут без DI (тем же кодом их строит
    /// превью-стенд). Тащить в каждую сборку экрана ещё один аргумент-сервис — шум; всплывающее
    /// событие даёт элементу право попросить тултип, ничего не зная о том, кто его покажет
    /// и есть ли этот кто-то вообще (на стенде слушателя нет — и это не ошибка).
    /// </remarks>
    public sealed class TooltipShowEvent : EventBase<TooltipShowEvent>
    {
        /// <summary>Что показать (по данным, §II.10.5 п.2).</summary>
        public TooltipRequest Request { get; private set; }

        /// <summary>Элемент-якорь: относительно него считается позиция окна.</summary>
        public VisualElement Anchor { get; private set; }

        public static TooltipShowEvent GetPooled(TooltipRequest request, VisualElement anchor)
        {
            // Базовый GetPooled() скрыт этой перегрузкой (C# ищет имя начиная с производного типа),
            // поэтому зовём его явно через базовый тип.
            TooltipShowEvent e = EventBase<TooltipShowEvent>.GetPooled();
            e.Request = request;
            e.Anchor  = anchor;
            return e;
        }

        protected override void Init()
        {
            base.Init();
            bubbles = true;        // всплывает до корня панели, где сидит система
            tricklesDown = false;
            Request = default;
            Anchor  = null;
        }
    }

    /// <summary>
    /// «Закрепи подсказку на этом содержимом» — Alt-клик по якорю или клик по термину внутри уже
    /// закреплённого окна (план §II.10.5, слой 3).
    /// </summary>
    /// <remarks>
    /// Отдельное событие, а не флаг в <see cref="TooltipShowEvent"/>: показ и закрепление — разные
    /// намерения с разной судьбой. Показ живёт, пока курсор на месте; закрепление переживает уход
    /// курсора и закрывается только явно.
    /// </remarks>
    public sealed class TooltipPinEvent : EventBase<TooltipPinEvent>
    {
        public TooltipRequest Request { get; private set; }
        public VisualElement Anchor { get; private set; }

        public static TooltipPinEvent GetPooled(TooltipRequest request, VisualElement anchor)
        {
            TooltipPinEvent e = EventBase<TooltipPinEvent>.GetPooled();
            e.Request = request;
            e.Anchor  = anchor;
            return e;
        }

        protected override void Init()
        {
            base.Init();
            bubbles = true;
            tricklesDown = false;
            Request = default;
            Anchor  = null;
        }
    }

    /// <summary>«Курсор ушёл с элемента с тултипом» — просьба убрать окно (если оно про этот якорь).</summary>
    public sealed class TooltipHideEvent : EventBase<TooltipHideEvent>
    {
        public VisualElement Anchor { get; private set; }

        public static TooltipHideEvent GetPooled(VisualElement anchor)
        {
            TooltipHideEvent e = EventBase<TooltipHideEvent>.GetPooled();
            e.Anchor = anchor;
            return e;
        }

        protected override void Init()
        {
            base.Init();
            bubbles = true;
            tricklesDown = false;
            Anchor = null;
        }
    }
}
