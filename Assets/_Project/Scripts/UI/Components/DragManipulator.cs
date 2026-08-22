using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Что именно тащат. Плоская запись, а не объект домена: перетаскивание одинаково носит «Сосуда»
    /// с места отряда и вещь из слота, и обе стороны шва обязаны различать их без приведения типов.
    /// </summary>
    public readonly struct DragPayload
    {
        /// <summary>Что это за груз: «vessel» или «item». Строкой, потому что перечень растёт от экранов.</summary>
        public readonly string Kind;

        /// <summary>Место, откуда взяли: индекс слота ростера.</summary>
        public readonly int SlotIndex;

        /// <summary>Второй индекс, если груз лежал внутри слота (номер слота предмета); иначе −1.</summary>
        public readonly int SubIndex;

        /// <summary>Content id груза, если он есть.</summary>
        public readonly string Id;

        public DragPayload(string kind, int slotIndex, int subIndex = -1, string id = null)
        {
            Kind      = kind;
            SlotIndex = slotIndex;
            SubIndex  = subIndex;
            Id        = id;
        }

        /// <summary>Пустой груз = тащить нечего. Проверяется до старта жеста.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Kind);
    }

    /// <summary>
    /// Перетаскивание как ОБЩИЙ контрол: источник объявляет, что несёт, цель — что принимает.
    /// <para>Живёт в компонентах, а не внутри экрана, по прямому решению: обе страницы подготовки
    /// стоят на перетаскивании (в бой, в запас, местами, надеть, снять), и жест, написанный внутри
    /// первой, пришлось бы переписывать на второй.</para>
    /// </summary>
    /// <remarks>
    /// <b>Призрак вместо переноса самого элемента.</b> Таскается лёгкая копия-подсказка в слое поверх
    /// панели, а исходный элемент остаётся на месте и лишь помечается классом. Двигать сам элемент
    /// значило бы вынуть его из раскладки: соседи схлопнулись бы, и цель под курсором уехала бы
    /// из-под пальца ровно в тот момент, когда игрок в неё целится.
    /// <para><b>Цель ищется на отпускании, а не запоминается на входе.</b> Курсор может пройти над
    /// несколькими зонами; та, что под ним в момент отпускания, и есть выбор игрока.</para>
    /// </remarks>
    public sealed class DragManipulator : PointerManipulator
    {
        /// <summary>Класс на источнике, пока его тащат.</summary>
        public const string DraggingClass = "gm-drag--dragging";

        /// <summary>Класс на призраке, который едет за курсором.</summary>
        public const string GhostClass = "gm-drag__ghost";

        /// <summary>Класс на зоне, готовой принять текущий груз.</summary>
        public const string DropValidClass = "gm-drop--valid";

        /// <summary>Класс на зоне, которая этот груз не берёт.</summary>
        public const string DropInvalidClass = "gm-drop--invalid";

        /// <summary>Класс на зоне прямо под курсором.</summary>
        public const string DropHoverClass = "gm-drop--hover";

        /// <summary>Сколько пикселей нужно увести курсор, чтобы жест считался перетаскиванием, а не кликом.</summary>
        private const float DragThreshold = 6f;

        private readonly Func<DragPayload> _payload;
        private readonly Func<VisualElement> _ghostFactory;

        private VisualElement _ghost;
        private Vector2 _startPosition;
        private bool _pressed;
        private bool _dragging;
        private DragPayload _current;
        private VisualElement _hovered;

        /// <param name="payload">Что понесём. Функция, а не значение: содержимое слота меняется между жестами.</param>
        /// <param name="ghostFactory">Чем показать груз в воздухе. Пусто — простая плашка с подписью.</param>
        public DragManipulator(Func<DragPayload> payload, Func<VisualElement> ghostFactory = null)
        {
            _payload      = payload ?? throw new ArgumentNullException(nameof(payload));
            _ghostFactory = ghostFactory;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerUpEvent>(OnUp);
            // Элемент могут снять с панели прямо посреди жеста (экран закрылся, ряд пересобрался).
            // Без этого призрак остался бы висеть, а панель — с классами захвата.
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;

            DragPayload payload = _payload();
            if (payload.IsEmpty) return; // пустое место тащить нечего

            _current       = payload;
            _pressed       = true;
            _startPosition = evt.position;
            target.CapturePointer(evt.pointerId);
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (!_pressed) return;

            if (!_dragging)
            {
                // Порог существует ради ЛКМ: клик по «Сосуду» открывает панель осмотра, и дрожание
                // руки на нажатии не должно превращать его в перетаскивание.
                if (Vector2.Distance(evt.position, _startPosition) < DragThreshold) return;
                BeginDrag();
            }

            MoveGhost(evt.position);
            Highlight(FindZoneUnder(evt.position));
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (!_pressed) return;

            bool dragged = _dragging;
            Vector2 position = evt.position;
            EndDrag();

            if (target.HasPointerCapture(evt.pointerId)) target.ReleasePointer(evt.pointerId);
            if (!dragged) return; // это был клик — пусть его обработает тот, кто слушает клики

            DropZoneManipulator zone = FindZoneUnder(position);
            if (zone == null || !zone.Accepts(_current)) return;
            zone.Drop(_current);
        }

        private void OnDetach(DetachFromPanelEvent _) => EndDrag();

        private void BeginDrag()
        {
            _dragging = true;
            target.AddToClassList(DraggingClass);

            _ghost = _ghostFactory != null ? _ghostFactory() : DefaultGhost(_current);
            _ghost.AddToClassList(GhostClass);
            _ghost.pickingMode = PickingMode.Ignore; // призрак не должен закрывать собой цель
            _ghost.style.position = Position.Absolute;
            target.panel?.visualTree.Add(_ghost);

            MarkZones(true);
        }

        private void EndDrag()
        {
            if (_ghost != null)
            {
                _ghost.RemoveFromHierarchy();
                _ghost = null;
            }
            if (_dragging) MarkZones(false);
            Highlight(null);

            target.RemoveFromClassList(DraggingClass);
            _dragging = false;
            _pressed  = false;
        }

        private void MoveGhost(Vector2 position)
        {
            if (_ghost == null) return;
            _ghost.style.left = position.x - _ghost.resolvedStyle.width / 2f;
            _ghost.style.top  = position.y - _ghost.resolvedStyle.height / 2f;
        }

        /// <summary>Подсветить все зоны панели: какая берёт этот груз, какая нет. Гасится по концу жеста.</summary>
        private void MarkZones(bool on)
        {
            VisualElement root = target.panel?.visualTree;
            if (root == null) return;

            foreach (VisualElement element in root.Query<VisualElement>().Build())
            {
                DropZoneManipulator zone = DropZoneManipulator.Of(element);
                if (zone == null) continue;

                bool accepts = on && zone.Accepts(_current);
                element.EnableInClassList(DropValidClass, accepts);
                element.EnableInClassList(DropInvalidClass, on && !accepts);
            }
        }

        private void Highlight(DropZoneManipulator zone)
        {
            VisualElement next = zone?.Element;
            if (_hovered == next) return;
            _hovered?.RemoveFromClassList(DropHoverClass);
            _hovered = next;
            _hovered?.AddToClassList(DropHoverClass);
        }

        private DropZoneManipulator FindZoneUnder(Vector2 position)
        {
            VisualElement picked = target.panel?.Pick(position);
            while (picked != null)
            {
                DropZoneManipulator zone = DropZoneManipulator.Of(picked);
                if (zone != null) return zone;
                picked = picked.parent;
            }
            return null;
        }

        private static VisualElement DefaultGhost(DragPayload payload)
        {
            var ghost = new Label(string.IsNullOrEmpty(payload.Id) ? payload.Kind : payload.Id);
            return ghost;
        }
    }

    /// <summary>
    /// Зона, которая принимает перетаскиваемое. Второй половина шва: источник знает, что несёт, зона —
    /// что берёт, и ни одна не знает про экран.
    /// </summary>
    public sealed class DropZoneManipulator : Manipulator
    {
        // Слабая таблица, а не userData: userData на элементах занимают сами экраны (там лежит
        // модель строки), и зона отняла бы у них единственное поле. Слабая — чтобы снятый с панели
        // элемент не держался в памяти из-за нашей же записи.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, DropZoneManipulator> Zones = new();

        private readonly Func<DragPayload, bool> _accepts;
        private readonly Action<DragPayload> _drop;

        /// <summary>Элемент, на котором висит зона. Нужен подсветке.</summary>
        public VisualElement Element => target;

        public DropZoneManipulator(Func<DragPayload, bool> accepts, Action<DragPayload> drop)
        {
            _accepts = accepts ?? throw new ArgumentNullException(nameof(accepts));
            _drop    = drop ?? throw new ArgumentNullException(nameof(drop));
        }

        /// <summary>
        /// Зона этого элемента или <c>null</c>. Ищется по таблице, а не по типу манипулятора: перечня
        /// навешенных манипуляторов UI Toolkit наружу не отдаёт вовсе.
        /// </summary>
        public static DropZoneManipulator Of(VisualElement element)
        {
            if (element == null) return null;
            return Zones.TryGetValue(element, out DropZoneManipulator zone) ? zone : null;
        }

        public bool Accepts(DragPayload payload) => !payload.IsEmpty && _accepts(payload);

        public void Drop(DragPayload payload)
        {
            if (Accepts(payload)) _drop(payload);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            Zones.Remove(target);
            Zones.Add(target, this);
        }

        protected override void UnregisterCallbacksFromTarget() => Zones.Remove(target);
    }
}
