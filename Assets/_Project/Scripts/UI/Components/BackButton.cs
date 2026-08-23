using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Кнопка «Вернуться» — единственная дверь наружу, одинаковая на всех экранах.
    /// </summary>
    /// <remarks>
    /// <b>Заведена ради ОДНОГО МЕСТА, а не ради нового вида</b> (правило Макса 22.08.2026: «Кнопка
    /// "Вернуться" (а не "назад" или "отмена")! - должна быть ВСЕГДА В ОДНОМ МЕСТЕ… чтобы мы могли её
    /// менять в одном месте и в нужных экранах она тоже менялась»). До неё каждый экран заводил свою:
    /// «Назад» на профиле, «Назад» на гильдиях, «Закрыть» в лоадауте, «Отмена» в настройках — четыре
    /// слова для одного жеста и четыре ключа локализации.
    /// <para><b>Что здесь общего:</b> слово (<see cref="LocKey"/>), место (класс
    /// <c>gm-button--back</c> уводит кнопку в левый край футера) и вид (пластина из набора). Что
    /// остаётся за экраном — куда именно она ведёт.</para>
    /// <para><b>Это модификатор кнопки, а не второй вид кнопки:</b> состояния, фаска и голос приходят
    /// от <see cref="PlateButton"/>. Заведи она свой вид — в наборе стало бы две кнопки, отличающиеся
    /// только назначением.</para>
    /// </remarks>
    [UxmlElement]
    public partial class BackButton : PlateButton
    {
        /// <summary>Ключ подписи. Один на всю игру: жест один, и слово у него одно.</summary>
        public const string LocKey = "ui.common.back";

        /// <summary>RU-литерал на случай незаведённого ключа — как у остального нового UI.</summary>
        public const string RuFallback = "Вернуться";

        public BackButton() : this(null) { }

        public BackButton(Action onBack) : base(() => onBack?.Invoke())
        {
            AddToClassList("gm-button");
            AddToClassList("gm-button--back");
            text = RuFallback;
        }

        /// <summary>
        /// Поставить подпись из локализации. Экран знает про службу перевода, контрол — нет; ключ при
        /// этом остаётся здесь, иначе «Вернуться» снова разъедется по экранам.
        /// </summary>
        public BackButton Localize(Func<string, string> localize)
        {
            string translated = localize?.Invoke(LocKey);
            text = string.IsNullOrEmpty(translated) ? RuFallback : translated;
            return this;
        }

        /// <summary>
        /// Поставить возврат на экран. ЕДИНСТВЕННЫЙ способ завести кнопку «Вернуться»: место она берёт
        /// у кромки кадра, и кадром для неё может быть только корень экрана.
        /// </summary>
        /// <remarks>
        /// <b>Почему метод, а не элемент в разметке.</b> Место кнопке задаёт <c>gm-button--back</c>
        /// — <c>position: absolute</c> у левого нижнего края. В UI Toolkit всякий элемент по умолчанию
        /// <c>relative</c>, поэтому абсолют считается от НЕПОСРЕДСТВЕННОГО родителя, а не от кадра:
        /// положили кнопку в колонку панели — она встала у кромки колонки, положили в ряд по центру —
        /// встала посреди экрана поверх карточки. Именно это Макс видел 23.08.2026 на кадрах выбора
        /// режима, выбора гильдии и настроек: «вернуться находится не у левого края экрана, как мы
        /// обсуждали. Почему?»
        /// <para>Разметка выбор родителя не ограничивает никак, а метод — ограничивает: он кладёт
        /// кнопку в корень экрана и никуда больше. Гейт <c>BackButtonPlacementTests</c> следит, что
        /// второго пути не завелось.</para>
        /// </remarks>
        /// <param name="screenRoot">Корень экрана — тот элемент, что занимает весь кадр.</param>
        /// <param name="onBack">Куда ведёт возврат. <c>null</c> — двери нет, и кнопки тоже.</param>
        /// <returns>Поставленная кнопка или <c>null</c>, если возвращаться некуда.</returns>
        public static BackButton PlaceOn(VisualElement screenRoot, Action onBack, Func<string, string> localize)
        {
            if (screenRoot == null || onBack == null) return null;

            var button = new BackButton(onBack);
            button.Localize(localize);
            screenRoot.Add(button);
            return button;
        }
    }
}
