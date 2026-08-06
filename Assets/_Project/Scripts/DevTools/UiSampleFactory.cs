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
            // Текст. Образцы пишутся ТАК, КАК ЭТИ СТРОКИ ЛЕЖАТ В ЛОКАЛИЗАЦИИ — по-человечески, а не
            // прописными. Регистр с 06.08.2026 приходит из USS (`--gm-text-case`, см. UiTextCase),
            // поэтому «Начать забег» в образце покажется на кадре как «НАЧАТЬ ЗАБЕГ» ровно потому же,
            // почему и в игре. Написать здесь капс руками значило бы снова завести второго владельца
            // регистра — и витрина перестала бы ловить его пропажу.
            // Кириллица — везде, где она бывает в игре: гарнитуру она показывает честнее латиницы.
            // Образцы РОЛЕЙ. Каждый — просто строка с классом роли: роль на то и роль, что несёт
            // весь свой вид сама и не зависит от предка. Прежние образцы брали представителей
            // (gm-tooltip__desc и подобных), и часть из них приходилось собирать вместе с
            // родителем, потому что кегль задавала кнопка, а гарнитуру — консоль.
            ["gm-text-display"] = () => Text("GUILDMASTERS"),
            ["gm-text-title"]   = () => Text("Настройки"),
            ["gm-text-name"]    = () => Text("Клинок Рассвета"),
            ["gm-text-body"]    = () => Text("Сложность"),
            ["gm-text-caption"] = () => Text("Дом хранит прогресс забега"),
            // Коротко намеренно: с метками у роли до шести ячеек в ряду, и длинная фраза разносила
            // ряд на две строки — на кадре это читалось поломкой листа, а не образцом.
            ["gm-text-note"]    = () => Text("Поджигает цель на 3 секунды."),
            ["gm-text-label"]   = () => Text("урон в секунду"),
            ["gm-text-code"]    = () => Text("gm.spawn(\"goblin\", 3)"),
            // Словарь: шесть категорий одной строкой — так видно, различимы ли они между собой.
            // Цвет живёт на модификаторе, поэтому образец блока показывает сразу все варианты.
            ["gm-kw"]                  = () => Keywords(),
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
                // Класс роли дописывается, только если его нет НИГДЕ в образце. Проверять один
                // корень мало: у роли, набираемой предком, класс висит на подписи внутри, и корень
                // получал бы вторую копию — то есть образец с классом там, где в игре его не бывает.
                if (!element.ClassListContains(entry.Block) && element.Q(className: entry.Block) == null)
                {
                    element.AddToClassList(entry.Block);
                }
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
        /// <remarks>
        /// Привязывается к <see cref="UiTextCase"/>: регистр — такое же свойство роли, как гарнитура,
        /// и голый <see cref="Label"/> сам его не применяет. Без привязки витрина показывала бы
        /// строчными то, что игра произносит прописными, — то есть ровно тем способом, каким она уже
        /// один раз соврала на кегле подписи.
        /// </remarks>
        private static Label Text(string sample)
        {
            var label = new Label(sample);
            UiTextCase.Bind(label);
            return label;
        }

        /// <summary>
        /// Образец, которому нужен ПРЕДОК: часть ролей набирается правилами родителя, а не своими.
        /// </summary>
        /// <remarks>
        /// Регистр сюда приходит тем же путём, что и кегль: custom-свойства USS наследуются, поэтому
        /// <c>--gm-text-case</c> с родительского <c>.gm-button</c> доходит до подписи внутри.
        /// </remarks>
        private static VisualElement Within(string parentClass, string ownClass, string sample)
        {
            var host = new VisualElement();
            host.AddToClassList(parentClass);

            Label label = Text(sample);
            label.AddToClassList(ownClass);
            host.Add(label);
            return host;
        }

        /// <summary>
        /// Словарь подсказки: по слову на каждую категорию, все в одной строке.
        /// </summary>
        /// <remarks>
        /// Одной строкой намеренно — вопрос к словарю не «какого цвета статус», а «отличается ли
        /// статус от защиты, когда стоят рядом». Порознь этого не видно, а в игре они и встречаются
        /// в одном абзаце подсказки.
        /// </remarks>
        private static VisualElement Keywords()
        {
            var host = new VisualElement();
            host.AddToClassList("gm-tooltip__desc");
            host.style.flexDirection = FlexDirection.Row;
            host.style.flexWrap = Wrap.Wrap;

            AddKeyword(host, "горение", "gm-kw--status");
            AddKeyword(host, "рубящий", "gm-kw--damage");
            AddKeyword(host, "броня", "gm-kw--defense");
            AddKeyword(host, "угроза", "gm-kw--behaviour");
            AddKeyword(host, "привал", "gm-kw--run");
            AddKeyword(host, "прочее", "gm-kw--other");
            return host;
        }

        private static void AddKeyword(VisualElement host, string word, string cls)
        {
            var label = new Label(word);
            label.AddToClassList(cls);
            label.style.marginRight = 12;
            host.Add(label);
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
