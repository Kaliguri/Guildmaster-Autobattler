using System;
using System.Collections.Generic;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Строит образец элемента по его классу — ровно так, как этот элемент собирается В ИГРЕ.
    /// </summary>
    /// <remarks>
    /// <b>Почему таблица, а не догадка по имени класса.</b> Прежняя витрина
    /// (<c>UiPreviewCatalog</c>) собирала вкладку из голого <see cref="Button"/> с классом
    /// <c>gm-tab</c>, тогда как экраны используют <see cref="PlateButton"/>. Все состояния активной
    /// вкладки висят на custom-свойствах <c>--gm-plate-*</c>, которых обычная кнопка не читает, —
    /// то есть витрина показывала работающим то, что в игре не работало. Здесь тип каждого образца
    /// выписан явно и с адресом, откуда он взят.
    ///
    /// <para><b>Это второй владелец правды, и он это признаёт.</b> Как собран элемент, знают двое:
    /// экран (UXML или код вью) и эта таблица. Расхождение ловит не тест, а глаз — на контактном
    /// листе неверно собранный образец выглядит мёртвым: без отклика, без градиента, без кромки.
    /// Поэтому мёртвая ячейка на листе читается как «фабрика врёт», а не как «состояние не
    /// реализовано», и проверять надо сначала её.</para>
    /// </remarks>
    public static class UiSampleFactory
    {
        private const string Sample = "Образец";

        /// <summary>
        /// Как собрать образец. Ключ — блок из <see cref="UiComponentRegistry"/>, значение — сборка,
        /// повторяющая игру; сопутствующие классы (например <c>gm-button</c> у кнопок) вешаются здесь же.
        /// </summary>
        private static readonly Dictionary<string, Func<VisualElement>> Builders = new()
        {
            // Кнопки. В UXML экраны пишут `<gm:PlateButton class="gm-button ...">`: класс несёт вид,
            // контрол — форму пластины. Поэтому образец обязан иметь оба.
            // РОЛИ. Класс роли всегда идёт вместе с gm-button: роль уточняет кнопку, а не заменяет её.
            ["gm-button"]              = () => PlateBtn(),
            ["gm-button--primary"]     = () => PlateBtn("gm-button--primary"),
            ["gm-button--danger"]      = () => PlateBtn("gm-button--danger"),
            ["gm-plate-button"]        = () => new PlateButton { text = Sample },   // техническая форма

            // Вкладки и чипы.
            ["gm-tab"]                 = () => Plate("gm-tab"),                     // SettingsScreen.uxml:13
            ["gm-chip"]                = () => new Chip { Text = Sample },
            ["gm-chip--slanted"]       = () => new SlantedChip { Text = Sample },
            // Ленту собирают чипы, а не свой контрол: класс --collapsible несёт сворачивание подписи.
            ["gm-runbar__tab"]         = () => Chip_("gm-chip--collapsible", "gm-runbar__tab"),
            ["gm-filter-tab"]          = () => Chip_("gm-filter-tab"),              // LoadoutInventoryScreen.uxml:25

            // Карточки и слоты.
            ["gm-card"]                = () => new RelicCard { RelicName = Sample },
            ["gm-hub-vessel"]          = () => new VesselCard { VesselName = Sample, RelicName = "Реликвия" },
            ["gm-slot"]                = () => new Slot(),
            ["gm-arcana-card"]         = () => Box("gm-arcana-card"),               // RelicArcanaCard.uxml:6
            ["gm-shop__card"]          = () => Box("gm-shop__card"),                // ShopScreenView.cs:69
            ["gm-shop__stash-row"]     = () => Box("gm-shop__stash-row"),           // ShopScreenView.cs:106
            ["gm-reward-drop__row"]    = () => Box("gm-reward-drop__row"),
            ["gm-stat"]                = () => Box("gm-stat"),                      // LoadoutInventoryView.cs:515
            ["gm-profile__swatch"]     = () => Box("gm-profile__swatch"),
            ["gm-profile__cursor"]     = () => Box("gm-profile__cursor"),
            ["gm-chest__lid"]          = () => Box("gm-chest__lid"),                // ChestScreen.uxml:9

            // Строки настроек.
            ["gm-toggle-row"]          = () => new ToggleRow { LabelText = Sample },
            ["gm-select-row"]          = () => new SelectRow { LabelText = Sample },
            ["gm-slider-row"]          = () => new SliderRow { LabelText = Sample },

            // Панели и декор.
            ["gm-panel"]               = () => Box("gm-panel", "gm-panel--dialog"),
            ["gm-panel__frame"]        = () => new PanelFrame(),
            ["gm-slant"]               = () => new SlantedPanel(),
            ["gm-edge-veil"]           = () => new EdgeVeil(),

            // Поверх всего.
            ["gm-tooltip"]             = () => Box("gm-tooltip"),
            ["gm-tooltip__card"]       = () => Tooltip(),

            // Дев-тулинг.
            ["gm-console__tool"]       = () => Btn("gm-console__tool"),             // DevConsoleScreen.uxml:26
            ["gm-console__hit"]        = () => Box("gm-console__hit"),              // DevConsoleScreen.cs:415
            ["gm-picker__row"]         = () => Box("gm-picker__row"),               // DevBattleBrowser.uxml:18
            ["gm-picker__head-cell"]   = () => Box("gm-picker__head-cell"),
        };

        /// <summary>
        /// Образцы ТЕКСТОВЫХ ролей — отдельной таблицей.
        /// </summary>
        /// <remarks>
        /// Разведены не для порядка: <c>gm-tab</c> живёт в обеих группах — как вкладка (пластина) и
        /// как её подпись (строка), — и в одном словаре ключи столкнулись бы. Группа записи и решает,
        /// из какой таблицы брать образец.
        /// </remarks>
        private static readonly Dictionary<string, Func<VisualElement>> TextBuilders = new()
        {
            // Текст. Образцы написаны ТАК, КАК ЭТИ СТРОКИ ПИШУТСЯ В ИГРЕ: регистр в UI Toolkit не
            // задаётся стилем (`text-transform` отсутствует), его задают сами буквы. Кириллица —
            // везде, где она бывает в игре: гарнитуру она показывает честнее латиницы.
            ["gm-title__main"]         = () => Text("GUILDMASTERS"),
            ["gm-panel__title"]        = () => Text("Настройки"),
            ["gm-card__name"]          = () => Text("Клинок Рассвета"),
            ["gm-tooltip__desc"]       = () => Text("Наносит 12 урона всем врагам в дуге и поджигает их на 3 секунды."),
            // Подпись действия и код набираются В СВОЁМ КОНТЕКСТЕ: кегль подписи задаёт сама кнопка,
            // а моноширинную гарнитуру консоли — правило `.gm-console .unity-text-element`, то есть
            // ПРЕДОК. Без обёртки образец показывал 14px почти чёрным и FiraSans вместо Fira Mono —
            // враньё, которое поймал замер, а не глаз.
            ["gm-plate-button__label"] = () => Within("gm-button", "gm-plate-button__label", "НАЧАТЬ ЗАБЕГ"),
            ["gm-stat__value"]         = () => Text("128"),
            ["gm-text-muted"]          = () => Text("Приглушённая подпись"),
            ["gm-console__keys"]       = () => Within("gm-console", "gm-console__keys", "gm.spawn(\"goblin\", 3)"),
        };

        /// <summary>
        /// Образец элемента. Неизвестный блок отдаётся заметной пустышкой, а не тихой коробкой:
        /// дыра в таблице обязана бросаться в глаза на самом листе.
        /// </summary>
        public static VisualElement Build(UiComponentEntry entry)
        {
            Dictionary<string, Func<VisualElement>> table =
                entry.Group == UiComponentGroup.Typography ? TextBuilders : Builders;

            if (table.TryGetValue(entry.Block, out Func<VisualElement> build))
            {
                VisualElement element = build();
                if (!element.ClassListContains(entry.Block)) element.AddToClassList(entry.Block);
                return element;
            }

            var unknown = new Label($"?? {entry.Block}");
            unknown.AddToClassList("gm-sheet__unknown");
            return unknown;
        }

        private static PlateButton PlateBtn(params string[] extraClasses)
        {
            var button = new PlateButton { text = Sample };
            button.AddToClassList("gm-button");
            for (int i = 0; i < extraClasses.Length; i++) button.AddToClassList(extraClasses[i]);
            return button;
        }

        /// <summary>Пластина без <c>gm-button</c>: так стоят вкладка и кнопка старта забега.</summary>
        private static PlateButton Plate(params string[] classes)
        {
            var button = new PlateButton { text = Sample };
            for (int i = 0; i < classes.Length; i++) button.AddToClassList(classes[i]);
            return button;
        }

        private static Button Btn(string cls)
        {
            var button = new Button { text = Sample };
            button.AddToClassList(cls);
            return button;
        }

        private static Chip Chip_(params string[] classes)
        {
            var chip = new Chip { Text = Sample };
            for (int i = 0; i < classes.Length; i++) chip.AddToClassList(classes[i]);
            return chip;
        }

        /// <summary>Образец текстовой роли: строка, у которой вид целиком приходит с класса.</summary>
        private static Label Text(string sample) => new(sample);

        /// <summary>
        /// Образец, которому нужен ПРЕДОК: часть ролей набирается правилами родителя, а не своими.
        /// </summary>
        private static VisualElement Within(string parentClass, string ownClass, string sample)
        {
            var host = new VisualElement();
            host.AddToClassList(parentClass);

            var label = new Label(sample);
            label.AddToClassList(ownClass);
            host.Add(label);
            return host;
        }

        private static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement();
            for (int i = 0; i < classes.Length; i++) box.AddToClassList(classes[i]);
            return box;
        }

        private static TooltipCard Tooltip()
        {
            var card = new TooltipCard();
            card.SetTitle(Sample);
            card.SetMeta("подсказка");
            return card;
        }
    }
}
