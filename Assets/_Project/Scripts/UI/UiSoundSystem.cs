using Guildmaster.Core.Audio;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Единый шов UI-звука: один слушатель на КОРНЕ панели вместо вызова в каждом экране (тот же приём,
    /// что <see cref="Tooltips.TooltipSystem"/> — события UITK всплывают до корня, и там их можно
    /// разобрать по типу элемента и USS-классу).
    ///
    /// Почему так, а не базовый класс кнопки: базы кнопки в проекте нет — везде голый
    /// <see cref="Button"/> плюс класс <c>gm-button</c>. Один корневой обработчик озвучивает все клики
    /// и наведения разом и не требует правок ни в одном экране; экраны, которым нужен СВОЙ звук
    /// (награда, магазин, карта), зовут <see cref="IAudioService"/> точечно поверх этого слоя.
    ///
    /// Ключи — канон <c>{contentId}.{action}</c> с действием <c>ui</c>: <c>ui.click.ui</c>,
    /// <c>ui.hover.ui</c>, <c>ui.tab.ui</c>… Нет записи в каталоге — резолвер падает на дефолт
    /// действия, то есть тишины не будет.
    /// </summary>
    public sealed class UiSoundSystem
    {
        private readonly IAudioService _audio;

        // Последний озвученный ховер: UITK шлёт PointerEnter и на детях, звук иначе дублируется.
        private VisualElement _lastHovered;

        public UiSoundSystem(IAudioService audio) => _audio = audio;

        public void Attach(VisualElement root)
        {
            if (root == null) return;
            root.RegisterCallback<ClickEvent>(OnClick);
            root.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            root.RegisterCallback<ChangeEvent<float>>(OnSliderChanged);
            root.RegisterCallback<WheelEvent>(OnWheel);
        }

        public void Detach(VisualElement root)
        {
            if (root == null) return;
            root.UnregisterCallback<ClickEvent>(OnClick);
            root.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            root.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            root.UnregisterCallback<ChangeEvent<float>>(OnSliderChanged);
            root.UnregisterCallback<WheelEvent>(OnWheel);
        }

        /// <summary>Проиграть UI-звук по короткому имени (<c>click</c> → <c>ui.click.ui</c>).</summary>
        public void PlayUi(string name)
        {
            if (_audio == null || string.IsNullOrEmpty(name)) return;
            _audio.Play("ui." + name + ".ui");
        }

        private void OnClick(ClickEvent evt)
        {
            if (evt.target is not VisualElement element) return;
            VisualElement interactive = FindInteractive(element);
            if (interactive == null) return;
            PlayUi(ClickSound(interactive));
        }

        private void OnPointerEnter(PointerEnterEvent evt)
        {
            if (evt.target is not VisualElement element) return;
            VisualElement interactive = FindInteractive(element);
            if (interactive == null || ReferenceEquals(interactive, _lastHovered)) return;
            _lastHovered = interactive;
            if (interactive.enabledInHierarchy) PlayUi("hover");
        }

        // Слайдер тянут — щелчок на шаг. Частоту держит cooldown события в FMOD (30-40 мс): по кадру
        // на каждое движение мыши иначе слилось бы в шум.
        private void OnSliderChanged(ChangeEvent<float> evt)
        {
            if (evt.target is Slider) PlayUi("slider");
        }

        private void OnWheel(WheelEvent evt) => PlayUi("scroll");

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (evt.target is VisualElement element && ReferenceEquals(FindInteractive(element), _lastHovered))
                _lastHovered = null;
        }

        /// <summary>
        /// Ближайший интерактивный предок (или сам элемент): клик по иконке внутри кнопки должен звучать
        /// как кнопка. Возвращает null для «мяса» — фона, лейблов, контейнеров.
        /// </summary>
        private static VisualElement FindInteractive(VisualElement element)
        {
            for (VisualElement e = element; e != null; e = e.parent)
            {
                if (e is Button || e is Toggle || e is Slider || e is SliderInt) return e;
                if (e.ClassListContains("gm-button") || e.ClassListContains("gm-chip")
                    || e.ClassListContains("gm-card") || e.ClassListContains("gm-slot")
                    || e.ClassListContains("gm-arcana-card") || e.ClassListContains("gm-event-choice"))
                    return e;
            }
            return null;
        }

        /// <summary>
        /// Какой именно звук у клика. Разводим по типу и по имени элемента: «назад/закрыть/отмена»
        /// звучат мягче подтверждения — это единственная разница, которую игрок реально слышит.
        /// </summary>
        private static string ClickSound(VisualElement element)
        {
            if (!element.enabledInHierarchy) return "disabled";
            if (element is Toggle || element.ClassListContains("gm-toggle-row")) return "toggle";
            if (element.ClassListContains("gm-chip")) return "tab";

            string name = element.name ?? string.Empty;
            if (name.Contains("back") || name.Contains("close") || name.Contains("cancel") || name.Contains("return"))
                return "back";
            return "click";
        }
    }
}
