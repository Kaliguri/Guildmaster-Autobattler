#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.UI.Tooltips;
using UnityEditor;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Каталог превьюабельных экранов для UI-стенда (<c>UiPreviewHost</c>): <c>id → билдер(root)</c>.
    /// Каждая запись собирает конкретный экран в конкретном состоянии со СТЕНДОВЫМИ данными — без DI,
    /// боя и бута. Контент берётся из настоящего <see cref="ContentDatabase"/> (высокая достоверность),
    /// сервисы, которых нет, — заглушками. Добавить экран в стенд = одна запись здесь.
    /// <para>Editor-only: строит из ассетов через <see cref="AssetDatabase"/>.</para>
    /// </summary>
    public static class UiPreviewCatalog
    {
        private const string ContentDbPath = "Assets/_Project/ScriptableObjects/Database/ContentDatabase.asset";

        private static readonly Dictionary<string, Action<VisualElement>> Builders = new()
        {
            ["dev-picker"]   = BuildDevPicker,
            ["reward"]       = BuildReward,
            ["event"]        = BuildEvent,
            ["loadout-inventory"] = BuildLoadoutInventory,
            ["settings"]     = BuildSettings,
            // "map" снят: карта больше не UITK-экран, она живёт в мире (см. WorldMapView) и в UI-стенде
            // не собирается. Смотреть её — дев-командами gm_map_* в игре.
            ["shop"]         = BuildShop,
            ["chest"]        = BuildChest,
            ["outcome"]      = BuildOutcome,
            ["mainmenu"]     = BuildMainMenu,
            ["newgame"]      = BuildNewGame,
            ["guilds"]       = BuildGuildSelect,
            ["hub"]          = BuildHub,
            ["titlecard"]    = BuildTitleCard,
            ["devconsole"]   = BuildDevConsole,
        };

        /// <summary>Все известные цели (для меню/подсказок).</summary>
        public static IEnumerable<string> Ids => Builders.Keys;

        /// <summary>Собрать экран <paramref name="id"/> в <paramref name="root"/>. Неизвестный id → подпись-заглушка.</summary>
        public static void Build(string id, VisualElement root)
        {
            root.Clear();
            if (id != null && Builders.TryGetValue(id, out Action<VisualElement> builder))
            {
                builder(root);
                return;
            }

            var label = new Label($"UI Preview: неизвестная цель '{id}'.\nДоступно: {string.Join(", ", Ids)}");
            label.AddToClassList("gm-dev-empty");
            root.Add(label);
        }

        // ── Записи каталога ──────────────────────────────────────────────────

        private static void BuildDevPicker(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Dev/DevBattlePicker.uxml");
            if (uxml == null) { AddError(root, "DevBattlePicker.uxml не найден"); return; }

            uxml.CloneTree(root);

            // При ручном CloneTree <Style src> из UXML не всегда применяется — подключаем USS явно.
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_Project/UI/Dev/DevBattlePicker.uss");
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            var scroll = root.Q<ScrollView>("gm-dev-scroll");
            if (scroll == null) { AddError(root, "в UXML нет #gm-dev-scroll"); return; }

            // Реальный контент без DI: тот же приём, что в RootLifetimeScope.
            IContentDatabase content = LoadContent();
            DevBattlePickerView.Populate(scroll.contentContainer, content, _ => { }, _ => { });
        }

        private static void BuildReward(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/RewardScreen.uxml");
            if (uxml == null) { AddError(root, "RewardScreen.uxml не найден"); return; }

            // Витрина: первые несколько реликвий из настоящей БД (без RNG — стенду хватает достоверного контента).
            IContentDatabase content = LoadContent();
            var choices = new List<RelicData>();
            if (content != null)
            {
                IReadOnlyList<RelicData> all = content.All<RelicData>();
                for (int i = 0; i < all.Count && choices.Count < RewardService.DefaultChoiceCount; i++)
                    if (all[i] != null && all[i].Id != ContentIds.BaseRelic) choices.Add(all[i]);
            }

            // Без loc-сервиса в стенде: имя = короткий id, статичные подписи берут RU-фолбэк из View.
            VisualElement screen = Guildmaster.UI.RewardScreenView.Build(
                uxml, choices, inventoryFull: false, currentInventory: null,
                nameOf: r => RuName(r?.Id),
                localize: RuValue,
                onTake: (_, __) => { },
                onSkip: () => { },
                // Стенд красит тело тем же путём, что игра: ступень из данных, цвет из палитры проекта.
                palette: LoadFirst<Guildmaster.Data.Definitions.GuildmasterPalette>());
            root.Add(screen);
        }

        private static void BuildEvent(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/EventScreen.uxml");
            if (uxml == null) { AddError(root, "EventScreen.uxml не найден"); return; }

            IContentDatabase content = LoadContent();
            TextEventData ev = null;
            if (content != null)
            {
                IReadOnlyList<TextEventData> all = content.All<TextEventData>();
                if (all != null && all.Count > 0) ev = all[0];
            }
            if (ev == null) { AddError(root, "нет ни одного TextEventData в БД"); return; }

            // Без loc-сервиса: заголовок/тело/варианты покажут id-ключи (структура важнее текста для превью).
            VisualElement screen = Guildmaster.UI.EventScreenView.Build(
                uxml, ev,
                localize: RuValue,
                onChosen: _ => { });
            root.Add(screen);
        }

        private static void BuildLoadoutInventory(VisualElement root)
        {
            var screenUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/LoadoutInventoryScreen.uxml");
            var cardUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/RelicArcanaCard.uxml");
            if (screenUxml == null || cardUxml == null) { AddError(root, "LoadoutInventoryScreen/RelicArcanaCard.uxml не найден"); return; }

            IContentDatabase content = LoadContent();
            var relics = new List<RelicData>();
            if (content != null)
            {
                IReadOnlyList<RelicData> all = content.All<RelicData>();
                for (int i = 0; all != null && i < all.Count; i++)
                    if (all[i] != null && all[i].Id != ContentIds.BaseRelic) relics.Add(all[i]);
            }

            // Статы для стенда — тот же шов, что в игре (DI тут нет, поэтому собираем руками из
            // ассетов конфигов). Не найдены → статблок просто спрячется, а не покажет заглушки.
            var statPreview = new Guildmaster.Combat.UnitStatPreview(
                LoadFirst<StatsConfig>(), LoadFirst<ClassBalanceConfig>());

            // Владеемые релики слева + 3 заблокированных (задел под фильтр по владению, Фаза 5).
            VisualElement screen = Guildmaster.UI.LoadoutInventoryView.Build(
                screenUxml, cardUxml, relics, gold: 100,
                titleOf: r => Guildmaster.UI.ContentTitle.Arcana(r?.Id),
                narrativeOf: r => Coalesce(RuValue((r?.Id) + ".desc"), "«Древний завет, что тлеет в глубине веков…»"),
                localize: RuValue,
                lockedSlots: 3,
                tagsOf: r => UnitTagResolver.Resolve(r, content),
                statsOf: r => statPreview.Basic(r),
                palette: LoadFirst<Guildmaster.Data.Definitions.GuildmasterPalette>());
            root.Add(screen);

            // Глобальная панель забега (app-shell): статичная для стенда, режим «Инвентарь» активен.
            var barUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/RunModeBar.uxml");
            if (barUxml != null)
            {
                var bar = new Guildmaster.UI.RunModeBarView(
                    barUxml, RuValue,
                    () => { }, () => { }, () => { }, () => { }, () => { });
                bar.SetGold(100);
                bar.SetAct(4);
                bar.SetFloor(3, 12);
                bar.SetRestarts(2, 2);
                bar.SetActiveMode(Guildmaster.UI.UiScreen.InventoryModeTag);
                bar.HideBattleCenter();
                root.Add(bar.Root);
            }
        }

        // «flame_swordsman» → «Flame Swordsman» (англ. титул таро-карты из id, когда loc RU не нужен).


        private static void BuildSettings(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/SettingsScreen.uxml");
            if (uxml == null) { AddError(root, "SettingsScreen.uxml не найден"); return; }

            VisualElement screen = uxml.CloneTree();
            VisualElement r = screen.childCount > 0 ? screen[0] : screen;

            // Подписи/значения — как проставляет роутер из VM (стенду хватает статичных).
            SetSliderRow(r, "row-master", "Общий", 0.8f);
            SetSliderRow(r, "row-music",  "Музыка", 0.65f);
            SetSliderRow(r, "row-sfx",    "Звук",  1.0f);

            // Страница «Графика»: реальные режимы монитора, чтобы видеть настоящую длину списков.
            // Значения статичны — стенд не поднимает IDisplayService, он показывает вид, а не поведение.
            SetSelectRow(r, "row-window-mode", "Режим окна",
                new List<string> { "Окно без рамок", "Полноэкранный", "Оконный" }, 0);

            var resolutions = new List<string>();
            foreach (UnityEngine.Resolution res in UnityEngine.Screen.resolutions)
            {
                string item = $"{res.width} x {res.height}";
                if (!resolutions.Contains(item)) resolutions.Add(item);
            }
            SetSelectRow(r, "row-resolution", "Разрешение", resolutions, resolutions.Count - 1);

            var rates = new List<string>();
            foreach (UnityEngine.Resolution res in UnityEngine.Screen.resolutions)
            {
                string item = $"{res.refreshRateRatio.value:0.##} Гц";
                if (!rates.Contains(item)) rates.Add(item);
            }
            SetSelectRow(r, "row-refresh-rate", "Частота обновления", rates, rates.Count - 1);

            // Частота гаснет вне эксклюзивного полноэкранного — показываем именно это состояние,
            // потому что оно и есть по умолчанию (окно без рамок).
            var refreshRow = r.Q<Guildmaster.UI.Components.SelectRow>("row-refresh-rate");
            refreshRow?.SetRowEnabled(false);
            var hint = r.Q<Label>("video-hint");
            if (hint != null) hint.text = "Частоту обновления можно менять только в полноэкранном режиме.";

            // Табы кликабельны — иначе страницу «Графика» в стенде не открыть.
            Guildmaster.UI.MenuRouter.WireSettingsTabs(r);
            root.Add(r);
        }

        private static void SetSelectRow(VisualElement root, string name, string label,
                                         List<string> choices, int selected)
        {
            var row = root.Q<Guildmaster.UI.Components.SelectRow>(name);
            if (row == null) return;
            row.LabelText = label;
            row.SetChoices(choices, selected);
        }

        private static void SetSliderRow(VisualElement root, string name, string label, float value)
        {
            var row = root.Q<Guildmaster.UI.Components.SliderRow>(name);
            if (row == null) return;
            row.LabelText = label;
            row.SetValueWithoutNotify(value);
        }

        private static void BuildShop(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/ShopScreen.uxml");
            if (uxml == null) { AddError(root, "ShopScreen.uxml не найден"); return; }

            IContentDatabase content = LoadContent();
            var relics = new List<RelicData>();
            if (content != null)
            {
                IReadOnlyList<RelicData> all = content.All<RelicData>();
                for (int i = 0; all != null && i < all.Count && relics.Count < 6; i++)
                    if (all[i] != null && all[i].Id != ContentIds.BaseRelic) relics.Add(all[i]);
            }

            var shelf = new List<Guildmaster.Guild.ShopItem>();
            for (int i = 0; i < 4 && i < relics.Count; i++)
                shelf.Add(new Guildmaster.Guild.ShopItem { Relic = relics[i], Price = 50 + i * 30, Sold = i == 1 });
            var stash = new List<Guildmaster.Guild.ShopStashItem>();
            for (int i = 4; i < relics.Count && stash.Count < 3; i++)
                stash.Add(new Guildmaster.Guild.ShopStashItem { Relic = relics[i], SellValue = 15 });

            var shop = new PreviewShop(shelf, stash);
            VisualElement screen = Guildmaster.UI.ShopScreenView.Build(
                uxml, shop, r => RuName(r?.Id), RuValue, () => { });
            root.Add(screen);
        }

        // Фейковый контроллер магазина для стенда: без DI/RunState, только показать раскладку.
        private sealed class PreviewShop : Guildmaster.Guild.IShopController
        {
            private readonly List<Guildmaster.Guild.ShopItem> _shelf;
            private readonly List<Guildmaster.Guild.ShopStashItem> _stash;
            public PreviewShop(List<Guildmaster.Guild.ShopItem> shelf, List<Guildmaster.Guild.ShopStashItem> stash)
            { _shelf = shelf; _stash = stash; }
            public event System.Action Changed;
            public int Gold => 250;
            public int RerollCost => 50;
            public IReadOnlyList<Guildmaster.Guild.ShopItem> Shelf => _shelf;
            public IReadOnlyList<Guildmaster.Guild.ShopStashItem> Stash => _stash;
            public Guildmaster.Guild.ShopBuyOutcome Buy(int index)
            {
                if (index >= 0 && index < _shelf.Count) { _shelf[index].Sold = true; Changed?.Invoke(); }
                return Guildmaster.Guild.ShopBuyOutcome.Bought;
            }
            public bool Reroll() { Changed?.Invoke(); return true; }
            public bool Sell(RelicData relic) { Changed?.Invoke(); return true; }

            // Полка лекаря на стенде пуста: раны живут в забеге, а у стенда забега нет.
            public IReadOnlyList<Guildmaster.Guild.ShopInjury> Injuries =>
                System.Array.Empty<Guildmaster.Guild.ShopInjury>();
            public bool Heal(int slotIndex, string consequenceId) { Changed?.Invoke(); return true; }
        }

        private static void BuildChest(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/ChestScreen.uxml");
            if (uxml == null) { AddError(root, "ChestScreen.uxml не найден"); return; }
            root.Add(Guildmaster.UI.ChestScreenView.Build(uxml, RuValue, () => { }));
        }

        private static void BuildOutcome(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/OutcomeScreen.uxml");
            if (uxml == null) { AddError(root, "OutcomeScreen.uxml не найден"); return; }
            // Стенд показывает победу; поражение — тот же экран с victory:false.
            root.Add(Guildmaster.UI.OutcomeScreenView.Build(uxml, victory: true, RuValue, () => { }));
        }

        private static void BuildMainMenu(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/MainMenuScreen.uxml");
            if (uxml == null) { AddError(root, "MainMenuScreen.uxml не найден"); return; }
            root.Add(Guildmaster.UI.MainMenuScreenView.Build(
                uxml, RuValue, () => { }, () => { }, () => { }, () => { }));
        }

        /// <summary>Экран «Создать игру»: три режима карточками и галочка лобби в футере.</summary>
        private static void BuildNewGame(VisualElement root)
        {
            root.Add(Guildmaster.UI.NewGameScreenView.Build(
                steamReady: true, RuValue, (_, _) => { }, () => { }));
        }

        /// <summary>
        /// Экран выбора дома. Стенд показывает то состояние, в котором экран и встречают, — два дома,
        /// из них один с идущим забегом, и свободные слоты под остальные.
        /// </summary>
        private static void BuildGuildSelect(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/GuildSelectScreen.uxml");
            if (uxml == null) { AddError(root, "GuildSelectScreen.uxml не найден"); return; }

            // Ни профиля, ни диска стенд не трогает: он смотрит РАЗМЕТКУ, а не чужие сохранения.
            var guilds = new System.Collections.Generic.List<Guildmaster.UI.GuildSelectScreenView.GuildEntry>
            {
                new("g1", "Гильдия 1", hasRun: true),
                new("g2", "Гильдия 2", hasRun: false),
            };

            root.Add(Guildmaster.UI.GuildSelectScreenView.Build(
                uxml, guilds, slotLimit: 4, RuValue, _ => { }, () => { }));
        }

        /// <summary>Двор гильдии — пока заглушка с меткой и единственной кнопкой.</summary>
        private static void BuildHub(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/HubScreen.uxml");
            if (uxml == null) { AddError(root, "HubScreen.uxml не найден"); return; }

            root.Add(Guildmaster.UI.HubScreenView.Build(uxml, "Гильдия 1", RuValue, () => { }));
        }

        private static void BuildTitleCard(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/TitleCardScreen.uxml");
            if (uxml == null) { AddError(root, "TitleCardScreen.uxml не найден"); return; }
            root.Add(Guildmaster.UI.TitleCardScreenView.Build(uxml, RuValue, () => { }));
        }

        /// <summary>
        /// Dev-консоль (Трек К) в рабочем состоянии: пара команд в реестре, набранный префикс с открытой
        /// палитрой и вывод всех четырёх видов строк — стенд должен показывать цвета кромок, а не пустую полку.
        /// </summary>
        private static void BuildDevConsole(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/DevConsoleScreen.uxml");
            if (uxml == null) { AddError(root, "DevConsoleScreen.uxml не найден"); return; }

            var registry = new Guildmaster.Core.DevConsole.DevCommandRegistry();
            registry.Register("gm_sep_radius", "Радиус тела на единицу Size (live)", a => "0.45",
                new Guildmaster.Core.DevConsole.DevParam("value", Guildmaster.Core.DevConsole.DevParamType.Float));
            registry.Register("gm_sep_strength", "Сила расталкивания за тик (live)", a => "1.2",
                new Guildmaster.Core.DevConsole.DevParam("value", Guildmaster.Core.DevConsole.DevParamType.Float));
            registry.Register("gm_sep_iters", "Проходов расталкивания за тик (live)", a => "2",
                new Guildmaster.Core.DevConsole.DevParam("value", Guildmaster.Core.DevConsole.DevParamType.Int));
            registry.Register("gm_sep_ally", "Мягкость расталкивания своих (0..1, live)", a => "0.35",
                new Guildmaster.Core.DevConsole.DevParam("value", Guildmaster.Core.DevConsole.DevParamType.Float));
            registry.Register("gm_arena_swap", "Сменить облик арены с анимацией", a => null,
                new Guildmaster.Core.DevConsole.DevParam("skinId", Guildmaster.Core.DevConsole.DevParamType.String));
            registry.Register("gm_spawn_battle", "Запустить тест-бой N юнитов за каждую сторону", a => null,
                new Guildmaster.Core.DevConsole.DevParam("count", Guildmaster.Core.DevConsole.DevParamType.Int, true));

            var log = new Guildmaster.Core.DevConsole.DevConsoleLog();
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Info, "[BattleStartup] - арена собрана: 4 против 3");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Echo, "> gm_arena_swap stone");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Reply, "облик «stone» надет, переход 0.8 с");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Echo, "> gm_sep_radius");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Error, "мало аргументов. Форма: gm_sep_radius <value>");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Warn, "[AudioService] - банк 'sfx_combat' уже загружен");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Info, "[BattleTape] - лента: 1214 событий, показ на тике 342");

            var screen = new Guildmaster.UI.DevConsole.DevConsoleScreen(uxml, registry, log);
            screen.Build(new Guildmaster.UI.UiScreenContext(root, RuValue));
            root.Add(screen.Root);

            // Набранный префикс: палитра раскрывается, ghost дорисовывает общее продолжение.
            var field = screen.Root.Q<TextField>("console-field");
            if (field != null) field.value = "gm_sep";
        }



        // ── Стендовые данные ─────────────────────────────────────────────────

        private static IContentDatabase LoadContent()
        {
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>(ContentDbPath);
            return db != null ? new ContentRegistry(db.Entries) : null;
        }

        /// <summary>Первый ассет типа в проекте (стенд без DI: конфиги-одиночки — StatsConfig и т.п.).</summary>
        private static T LoadFirst<T>() where T : UnityEngine.ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void AddError(VisualElement root, string msg)
        {
            var label = new Label("UI Preview: " + msg);
            label.AddToClassList("gm-dev-empty");
            root.Add(label);
        }

        /// <summary>Короткий id без домена (<c>relic.assassin</c> → <c>assassin</c>) — фолбэк без loc.</summary>

        /// <summary>
        /// Содержимое подсказок для стенда: имя и описание берутся прямо из таблицы <c>Content</c>
        /// (в стенде нет DI и сервиса описаний), поэтому витрина показывает ПОВЕДЕНИЕ окна —
        /// задержку, флип, grace — а не сборку текста. Сборку проверяет живой экран.
        /// </summary>

        // RU-строка из таблицы Content через ContentLocalization (Data.Editor). DevTools — рантайм-асмдеф,
        // editor-асмдеф не сослать напрямую → рефлексия. Только для достоверного превью (реальный UI берёт _loc).
        private static Type _clType;
        private static string RuValue(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_clType == null)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                { _clType = asm.GetType("Guildmaster.Data.Editor.ContentLocalization"); if (_clType != null) break; }
            var m = _clType?.GetMethod("GetValue", new[] { typeof(string), typeof(string) });
            return m?.Invoke(null, new object[] { "ru", key }) as string;
        }

        /// <summary>RU-имя контента по id (<c>{id}.name</c>), фолбэк — короткий id.</summary>
        private static string RuName(string id) => Coalesce(RuValue(id + ".name"), Guildmaster.UI.ContentTitle.WithoutDomain(id));

        private static string Coalesce(string a, string b) => string.IsNullOrEmpty(a) ? b : a;
    }
}
#endif
