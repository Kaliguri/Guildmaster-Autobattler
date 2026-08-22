using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Выбор вариантом: на экране стоит ОДНА кнопка с тем, что выбрано, а весь набор показывается
    /// окошком выше — и только пока выбирают.
    /// </summary>
    /// <remarks>
    /// <b>Заказ Макса 22.08.2026</b> дословно: «Добавь новый элемент UI - открывается небольшое окно
    /// выше в котором можно выбрать варианты чего-либо… У нас должна быть в UI одна кнопка для цвета,
    /// курсора, знака гильдии, цвета и тд - та, что нами выбрана. Для смены - мы нажимаем на неё…
    /// Делаем так, чтобы вариации не жрали место и внимание».
    /// <para><b>Чем это не выпадающий список.</b> `DropdownField` умеет только строки, а выбирают здесь
    /// глазом: цвет, курсор, знак. Поэтому вариант — это ОБРАЗЕЦ (заливка, картинка или подпись), и
    /// набор раскладывается сеткой, а не столбиком строк.</para>
    /// <para><b>Окно живёт в корне панели, а не внутри кнопки.</b> Родитель почти всегда обрезает
    /// содержимое (панель, ScrollView), и всплывающее окно, выросшее из кнопки, обрезалось бы вместе с
    /// ней. Поэтому окно кладётся в корень и позиционируется по мировым координатам кнопки.</para>
    /// <para><b>Закрывается кликом мимо себя</b> — единственный жест, который не нужно объяснять. Клик
    /// ловится на корне в фазе перехвата: до того, как его получит кнопка под окном.</para>
    /// </remarks>
    [UxmlElement]
    public partial class PickerButton : VisualElement
    {
        /// <summary>Один вариант набора: чем он выглядит и как называется.</summary>
        public readonly struct Option
        {
            /// <summary>Чем вариант зовётся в данных (id цвета, id скина, id знака).</summary>
            public readonly string Id;

            /// <summary>Заливка образца. <c>null</c> — образец не цветной.</summary>
            public readonly Color? Swatch;

            /// <summary>Картинка образца (курсор, знак). <c>null</c> — картинки нет.</summary>
            public readonly Texture2D Image;

            /// <summary>Подпись — для вариантов, которые не узнать глазом. Может быть пустой.</summary>
            public readonly string Label;

            /// <summary>Каким цветом красить картинку. <c>null</c> — цветом самой картинки.</summary>
            public readonly Color? Tint;

            public Option(string id, Color? swatch = null, Texture2D image = null,
                          string label = null, Color? tint = null)
            {
                Id     = id;
                Swatch = swatch;
                Image  = image;
                Label  = label;
                Tint   = tint;
            }
        }

        private readonly Button _current;   // то, что выбрано: кнопка, открывающая окно
        private readonly VisualElement _popover;

        private readonly List<Option> _options = new List<Option>();
        private Action<string> _onPick;
        private string _selectedId;
        private EventCallback<PointerDownEvent> _outsideClick;

        /// <summary>Что выбрано сейчас. Пусто — набор не задан.</summary>
        public string SelectedId => _selectedId;

        public PickerButton()
        {
            AddToClassList("gm-picker");

            _current = new Button(Toggle) { name = "picker-current" };
            _current.AddToClassList("gm-picker__current");
            Add(_current);

            _popover = new VisualElement { name = "picker-popover" };
            _popover.AddToClassList("gm-picker__popover");
            // Окно не участвует в раскладке экрана: оно всплывает над ним и держится координатами.
            _popover.style.position = Position.Absolute;
        }

        /// <summary>
        /// Задать набор и текущий выбор. Зовётся заново на каждую пересборку экрана — состояния окна
        /// компонент между вызовами не хранит.
        /// </summary>
        public void SetOptions(IReadOnlyList<Option> options, string selectedId, Action<string> onPick)
        {
            _options.Clear();
            if (options != null) _options.AddRange(options);
            _onPick = onPick;

            Select(selectedId, notify: false);
        }

        /// <summary>Выбрать вариант по id. <paramref name="notify"/> — сообщать ли заказчику.</summary>
        public void Select(string selectedId, bool notify = true)
        {
            _selectedId = selectedId;
            DressCurrent();
            if (notify) _onPick?.Invoke(selectedId);
        }

        /// <summary>Открыто ли окно набора.</summary>
        public bool IsOpen => _popover.parent != null;

        private void Toggle()
        {
            if (IsOpen) Close();
            else        Open();
        }

        private void Open()
        {
            VisualElement root = panel?.visualTree;
            if (root == null) return;

            BuildOptions();
            root.Add(_popover);
            Place(root);

            // Клик мимо окна закрывает его. Ловим В ФАЗЕ ПЕРЕХВАТА и на корне: иначе клик сначала
            // достался бы элементу под окном, и «закрыть» случилось бы уже после чужого действия.
            _outsideClick = evt =>
            {
                if (evt.target is VisualElement hit && (hit == _current || _popover.Contains(hit) || Contains(hit)))
                    return;
                Close();
            };
            root.RegisterCallback(_outsideClick, TrickleDown.TrickleDown);

            AddToClassList("gm-picker--open");
        }

        private void Close()
        {
            if (_outsideClick != null)
            {
                panel?.visualTree?.UnregisterCallback(_outsideClick, TrickleDown.TrickleDown);
                _outsideClick = null;
            }

            _popover.RemoveFromHierarchy();
            RemoveFromClassList("gm-picker--open");
        }

        /// <summary>
        /// Поставить окно НАД кнопкой, прижав к её левому краю, и удержать в кадре.
        /// </summary>
        /// <remarks>
        /// «Выше» — из заказа: набор всплывает над кнопкой, а не роняет экран вниз. У нижней кромки
        /// места сверху может не хватить — тогда окно уходит под кнопку, потому что обрезанный набор
        /// хуже нарушенного правила.
        /// </remarks>
        private void Place(VisualElement root)
        {
            Rect button = this.worldBound;
            Rect view   = root.worldBound;

            // Размеры окна известны только после раскладки — считаем позицию, когда движок их посчитал.
            _popover.RegisterCallback<GeometryChangedEvent>(OnPopoverMeasured);

            void OnPopoverMeasured(GeometryChangedEvent _)
            {
                _popover.UnregisterCallback<GeometryChangedEvent>(OnPopoverMeasured);

                float height = _popover.resolvedStyle.height;
                float width  = _popover.resolvedStyle.width;

                float top = button.yMin - height - Gap;
                if (top < view.yMin) top = button.yMax + Gap;   // сверху не влезло — открываем вниз

                float left = Mathf.Clamp(button.xMin, view.xMin, Mathf.Max(view.xMin, view.xMax - width));

                _popover.style.left = left - view.xMin;
                _popover.style.top  = top - view.yMin;
            }
        }

        /// <summary>Зазор между кнопкой и окном. Не токен: это расстояние принадлежит компоненту.</summary>
        private const float Gap = 8f;

        private void BuildOptions()
        {
            _popover.Clear();

            for (int i = 0; i < _options.Count; i++)
            {
                Option option = _options[i];
                string id = option.Id;

                var tile = new Button(() => { Select(id); Close(); }) { name = "option-" + id };
                tile.AddToClassList("gm-picker__option");
                Dress(tile, option);
                if (id == _selectedId) tile.AddToClassList("gm-picker__option--picked");

                _popover.Add(tile);
            }
        }

        /// <summary>Показать на кнопке то, что выбрано. Ничего не выбрано — кнопка пуста, но на месте.</summary>
        private void DressCurrent()
        {
            _current.Clear();
            _current.text = string.Empty;

            for (int i = 0; i < _options.Count; i++)
            {
                if (_options[i].Id != _selectedId) continue;
                Dress(_current, _options[i]);
                return;
            }
        }

        /// <summary>Одеть кнопку или плитку образцом варианта: заливка, картинка, подпись.</summary>
        private static void Dress(VisualElement target, in Option option)
        {
            target.Clear();

            var sample = new VisualElement { pickingMode = PickingMode.Ignore };
            sample.AddToClassList("gm-picker__sample");

            if (option.Swatch.HasValue) sample.style.backgroundColor = option.Swatch.Value;
            if (option.Image != null)
            {
                sample.style.backgroundImage = new StyleBackground(option.Image);
                if (option.Tint.HasValue) sample.style.unityBackgroundImageTintColor = option.Tint.Value;
            }

            target.Add(sample);

            if (string.IsNullOrEmpty(option.Label)) return;

            var caption = new Label(option.Label) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("gm-text-caption");
            caption.AddToClassList("gm-picker__label");
            target.Add(caption);
        }
    }
}
