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
            ["coop"]         = BuildCoop,
            ["titlecard"]    = BuildTitleCard,
            ["devconsole"]   = BuildDevConsole,
            ["gallery"]      = BuildGallery,
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
            // Стенд: hasSave=true (кнопка «Продолжить» активна).
            root.Add(Guildmaster.UI.MainMenuScreenView.Build(
                uxml, hasSave: true, RuValue, () => { }, () => { }, () => { }, () => { }));
        }

        private static void BuildCoop(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/CoopScreen.uxml");
            if (uxml == null) { AddError(root, "CoopScreen.uxml не найден"); return; }

            // Сессии в редакторе нет и быть не должно: стенд смотрит РАЗМЕТКУ, а не сеть. Заглушка отдаёт
            // оффлайн — то состояние, в котором игрок этот экран и открывает.
            root.Add(Guildmaster.UI.CoopScreenView.Build(uxml, new OfflineCoopStub(), RuValue, () => { }));
        }

        /// <summary>Кооп-сессия, которой нет: стенду нужна разметка, а не сеть.</summary>
        private sealed class OfflineCoopStub : Guildmaster.Core.Net.ICoopSessionControl
        {
            public Guildmaster.Core.Net.CoopSessionState State     => Guildmaster.Core.Net.CoopSessionState.Offline;
            public Guildmaster.Core.Net.CoopEndReason    EndReason => Guildmaster.Core.Net.CoopEndReason.None;
            public string EndMessage => string.Empty;

            public event Action<Guildmaster.Core.Net.CoopSessionState> StateChanged
            {
                add { } remove { }
            }

            public bool CanInvite => false;

            public bool StartHost() => false;
            public void InviteFriend() { }
            public bool Join(string address) => false;
            public void Leave() { }
        }

        private static void BuildTitleCard(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/TitleCardScreen.uxml");
            if (uxml == null) { AddError(root, "TitleCardScreen.uxml не найден"); return; }
            var seal = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/_Project/Art/Brand/AppIcon_HappyGuildmasters.png");
            root.Add(Guildmaster.UI.TitleCardScreenView.Build(uxml, seal, RuValue, () => { }));
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
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Info, "[BattleBootstrap] - арена собрана: 4 против 3");
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

        private static void BuildGallery(VisualElement root)
        {
            IContentDatabase content = LoadContent();
            var relics = new List<RelicData>();
            if (content != null)
            {
                IReadOnlyList<RelicData> all = content.All<RelicData>();
                for (int i = 0; all != null && i < all.Count && relics.Count < 6; i++)
                    if (all[i] != null && all[i].Id != ContentIds.BaseRelic) relics.Add(all[i]);
            }
            UnityEngine.Sprite Ico(int i) => i < relics.Count ? relics[i].Icon : null;
            string Nm(int i) => i < relics.Count ? Guildmaster.UI.ContentTitle.WithoutDomain(relics[i].Id) : "—";

            // Пересоздаём риг галереи (прошлые камеры/RT освобождаем).
            _galleryRig?.Dispose();
            _galleryRig = new Guildmaster.UI.Components.RelicCardVisualRig(
                palette: LoadFirst<Guildmaster.Data.Definitions.GuildmasterPalette>());

            root.style.backgroundColor = new UnityEngine.Color(18f / 255f, 16f / 255f, 13f / 255f, 1f);
            root.style.paddingTop = 20; root.style.paddingBottom = 20;
            root.style.paddingLeft = 24; root.style.paddingRight = 24;

            var h1 = new Label("Guildmaster UI — библиотека компонентов");
            h1.AddToClassList("gm-panel__title");
            h1.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
            h1.style.fontSize = 26; h1.style.marginBottom = 2;
            root.Add(h1);
            var sub = new Label("единственный источник и приёмка: атомы = классы, композиты = кастом-контролы");
            sub.AddToClassList("gm-text-muted");
            sub.style.marginBottom = 8;
            root.Add(sub);

            Label Header(string t)
            {
                var l = new Label(t);
                l.AddToClassList("gm-text-muted");
                l.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
                l.style.marginTop = 22; l.style.marginBottom = 8;
                root.Add(l);
                return l;
            }
            VisualElement Row()
            {
                var r = new VisualElement();
                r.style.flexDirection = FlexDirection.Row;
                r.style.flexWrap = Wrap.Wrap;
                r.style.alignItems = Align.FlexStart;
                root.Add(r);
                return r;
            }
            VisualElement Cell(VisualElement content1, string caption)
            {
                var c = new VisualElement();
                c.style.alignItems = Align.Center;
                c.style.marginRight = 16; c.style.marginBottom = 14;
                c.Add(content1);
                var cap = new Label(caption);
                cap.AddToClassList("gm-text-muted");
                cap.style.marginTop = 4;
                c.Add(cap);
                return c;
            }

            // ── АТОМЫ (классы) ──
            Header("Атомы — стилевые классы (менять вид = один класс)");
            var btnRow = Row();
            Button Btn(string txt, bool primary, bool enabled)
            {
                var b = new Button { text = txt };
                b.AddToClassList("gm-button");
                if (primary) b.AddToClassList("gm-button--primary");
                b.SetEnabled(enabled);
                b.style.marginRight = 12;
                return b;
            }
            btnRow.Add(Btn("Обычная", false, true));
            btnRow.Add(Btn("Primary", true, true));
            btnRow.Add(Btn("Disabled", false, false));

            var tabRow = Row();
            var tabbar = new VisualElement();
            tabbar.AddToClassList("gm-tabbar");
            string[] tabs = { "Релик", "Предметы", "Улучшения" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = new Button { text = tabs[i] };
                tab.AddToClassList("gm-tab");
                if (i == 0) tab.AddToClassList("gm-tab--active");
                if (i == 2) tab.SetEnabled(false);
                tabbar.Add(tab);
            }
            tabRow.Add(tabbar);

            // ── КОМПОЗИТЫ (кастом-контролы) ──
            Header("Slot — рамка-квадрат с опц. иконкой");
            var slotRow = Row();
            var sSm = new Guildmaster.UI.Components.Slot { Size = Guildmaster.UI.Components.Slot.SlotSize.Sm };
            slotRow.Add(Cell(sSm, "sm · пустой"));
            var sSmF = new Guildmaster.UI.Components.Slot { Size = Guildmaster.UI.Components.Slot.SlotSize.Sm };
            sSmF.SetIcon(Ico(0));
            slotRow.Add(Cell(sSmF, "sm · иконка"));
            var sMd = new Guildmaster.UI.Components.Slot { Size = Guildmaster.UI.Components.Slot.SlotSize.Md };
            slotRow.Add(Cell(sMd, "md · пустой"));
            var sMdF = new Guildmaster.UI.Components.Slot { Size = Guildmaster.UI.Components.Slot.SlotSize.Md };
            sMdF.SetIcon(Ico(1));
            slotRow.Add(Cell(sMdF, "md · иконка"));

            Header("RelicCard — карточка реликвии + состояния");
            var cardRow = Row();
            var c0 = new Guildmaster.UI.Components.RelicCard { RelicName = Nm(0) };
            c0.SetSprite(Ico(0));
            cardRow.Add(Cell(c0, "default"));
            var c1 = new Guildmaster.UI.Components.RelicCard { RelicName = Nm(1), Current = true };
            c1.SetSprite(Ico(1));
            cardRow.Add(Cell(c1, "current (надета)"));
            var c2 = new Guildmaster.UI.Components.RelicCard { RelicName = Nm(2), Selected = true };
            c2.SetSprite(Ico(2));
            cardRow.Add(Cell(c2, "selected (выбор)"));
            var c3 = new Guildmaster.UI.Components.RelicCard { RelicName = "—" };
            cardRow.Add(Cell(c3, "пустая"));

            Header("RelicCard — ЖИВАЯ (анимированный спрайт из боевого рига, план 10 §5)");
            var liveRow = Row();
            if (relics.Count > 0)
            {
                var live = new Guildmaster.UI.Components.RelicCard { RelicName = RuName(relics[0].Id) };
                live.style.width = 100; live.style.height = 136;
                live.SetVisual(_galleryRig.Acquire(relics[0]));
                liveRow.Add(Cell(live, "idle · реальный UnitView в RT"));
            }

            Header("VesselCard — сосуд команды (имя + надетый релик)");
            var vRow = Row();
            for (int i = 0; i < 3; i++)
            {
                var v = new Guildmaster.UI.Components.VesselCard { VesselName = Nm(i), RelicName = Nm(i) };
                v.SetRelicIcon(Ico(i));
                v.style.marginRight = 12;
                vRow.Add(v);
            }

            Header("SliderRow — строка настройки (подпись + слайдер + %)");
            var srWrap = new VisualElement();
            srWrap.AddToClassList("gm-panel");
            srWrap.style.maxWidth = 380;
            srWrap.Add(new Guildmaster.UI.Components.SliderRow { LabelText = "Общий", Value = 0.8f });
            srWrap.Add(new Guildmaster.UI.Components.SliderRow { LabelText = "Музыка", Value = 0.65f });
            srWrap.Add(new Guildmaster.UI.Components.SliderRow { LabelText = "Звук", Value = 1f });
            root.Add(srWrap);

            BuildTooltipShowcase(root, relics, Header, Row, Cell);
        }

        // Витрина тултипов (Трек Т, план шаг 9). Стенд без DI, поэтому система собирается вручную.
        // Ввод даём НАСТОЯЩИЙ (InputService самодостаточен): без него Shift на стенде не работал бы,
        // и витрина показывала бы половину поведения, молча расходясь с игрой.
        private static void BuildTooltipShowcase(VisualElement root, List<RelicData> relics,
            Func<string, Label> header, Func<VisualElement> row, Func<VisualElement, string, VisualElement> cell)
        {
            header("Tooltip — подсказки (Трек Т): наведи курсор");

            var layer = new VisualElement { name = "layer-tooltip", pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
            root.Add(layer);

            _gallerySystem?.Dispose();
            _galleryInput?.Dispose();
            _galleryStyle?.Detach();
            _galleryInput = new Guildmaster.Game.Input.InputService();
            _galleryStyle = new Guildmaster.UI.Tooltips.KeywordStyle(LoadContent());
            _galleryStyle.Attach(layer); // доноры цвета: те же классы .gm-kw--*, что в игре
            _gallerySystem = new Guildmaster.UI.Tooltips.TooltipSystem(
                new PreviewTooltipContent(LoadContent(), _galleryStyle), null, _galleryInput, null);
            _gallerySystem.Attach(root, layer);

            var hint = new Label("Shift — подробности · Alt+клик — закрепить окно (внутри работают ссылки, «‹ › ×»)");
            hint.AddToClassList("gm-text-muted");
            hint.style.marginBottom = 8;
            root.Add(hint);

            VisualElement tipRow = row();

            var textTarget = new Button { text = "текст" };
            textTarget.AddToClassList("gm-button");
            textTarget.WithTooltip(Guildmaster.UI.Tooltips.TooltipRequest.Plain(
                "Подсказка", "Готовая строка: свёрнутые теги, короткое пояснение."));
            tipRow.Add(cell(textTarget, "Text"));

            string relicId = relics.Count > 0 ? relics[0].Id : null;
            var relicTarget = new Button { text = relicId != null ? Guildmaster.UI.ContentTitle.WithoutDomain(relicId) : "реликвия" };
            relicTarget.AddToClassList("gm-button");
            relicTarget.WithTooltip(Guildmaster.UI.Tooltips.TooltipRequest.Relic(relicId));
            tipRow.Add(cell(relicTarget, "Relic (Shift — статы, Alt+клик — закрепить)"));

            var edgeTarget = new Button { text = "у правого края" };
            edgeTarget.AddToClassList("gm-button");
            edgeTarget.style.alignSelf = Align.FlexEnd;
            edgeTarget.WithTooltip(Guildmaster.UI.Tooltips.TooltipRequest.Plain(
                "Флип", "Окно у края панели зеркалится влево и не вылезает за границу."));
            var edgeWrap = new VisualElement();
            edgeWrap.style.width = 320;
            edgeWrap.Add(edgeTarget);
            tipRow.Add(cell(edgeWrap, "кламп/флип"));

            // Ключевое слово в тексте: разметку разворачивает тот же код, что и в игре, формы слов
            // берутся из таблицы Content — стенд показывает ровно то, что увидит игрок.
            string sample = Guildmaster.Data.Descriptions.KeywordMarkup.Render(
                "Накладывает [kw:burn:acc] и снимает стак [kw:shield:gen].",
                (id, caseTag) =>
                {
                    string key = id + "." + Guildmaster.Data.Definitions.ContentKeys.FormSuffix(caseTag);
                    string form = RuValue(key);
                    return string.IsNullOrEmpty(form) ? RuValue(id + ".name") : form;
                },
                _galleryStyle);

            var kwLabel = new Label(sample);
            kwLabel.style.maxWidth = 320;
            kwLabel.style.whiteSpace = WhiteSpace.Normal;
            kwLabel.WithKeywordTooltips();
            tipRow.Add(cell(kwLabel, "Keyword в тексте"));
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

        // Живой риг для галереи: держим статикой (камеры URP рендерят каждый кадр), пересоздаём при ребилде.
        private static Guildmaster.UI.Components.RelicCardVisualRig _galleryRig;

        // Система тултипов витрины: живёт между ребилдами стенда, пересоздаётся вместе с галереей.
        private static Guildmaster.UI.Tooltips.TooltipSystem _gallerySystem;

        // Ввод для витрины: настоящий сервис, чтобы Shift на стенде вёл себя как в игре.
        private static Guildmaster.Game.Input.InputService _galleryInput;

        // Цвет терминов витрины: те же USS-доноры, что в игре.
        private static Guildmaster.UI.Tooltips.KeywordStyle _galleryStyle;

        /// <summary>
        /// Содержимое подсказок для стенда: имя и описание берутся прямо из таблицы <c>Content</c>
        /// (в стенде нет DI и сервиса описаний), поэтому витрина показывает ПОВЕДЕНИЕ окна —
        /// задержку, флип, grace — а не сборку текста. Сборку проверяет живой экран.
        /// </summary>
        private sealed class PreviewTooltipContent : Guildmaster.UI.Tooltips.ITooltipContentFactory
        {
            private readonly IContentDatabase _content;
            private readonly Guildmaster.Combat.UnitStatPreview _stats;
            private readonly Guildmaster.UI.Tooltips.KeywordStyle _style;

            public PreviewTooltipContent(IContentDatabase content, Guildmaster.UI.Tooltips.KeywordStyle style)
            {
                _content = content;
                _style = style;
                _stats = new Guildmaster.Combat.UnitStatPreview(
                    LoadFirst<StatsConfig>(), LoadFirst<ClassBalanceConfig>());
            }

            // Формы слов берём прямо из таблицы Content: стенд без DI, но текст обязан выглядеть так же,
            // как в игре — иначе витрина показывает сырую разметку и врёт о результате.
            private string Rendered(string raw) => Guildmaster.Data.Descriptions.KeywordMarkup.Render(
                raw,
                (id, caseTag) =>
                {
                    string form = RuValue(id + "." + Guildmaster.Data.Definitions.ContentKeys.FormSuffix(caseTag));
                    return string.IsNullOrEmpty(form) ? RuValue(id + ".name") : form;
                },
                _style);

            public bool IsLive(Guildmaster.UI.Tooltips.TooltipRequest request) => false;

            public VisualElement Build(Guildmaster.UI.Tooltips.TooltipRequest request, bool detailed)
            {
                var card = new Guildmaster.UI.Components.TooltipCard();
                switch (request.Kind)
                {
                    case Guildmaster.UI.Tooltips.TooltipKind.Text:
                        card.SetTitle(request.Title);
                        card.SetDesc(request.Text);
                        return card;

                    case Guildmaster.UI.Tooltips.TooltipKind.Relic:
                        if (_content == null || !_content.TryGet(request.Id, out RelicData relic) || relic == null)
                            return null;
                        card.SetTitle(RuValue(relic.Id + ".name") ?? Guildmaster.UI.ContentTitle.WithoutDomain(relic.Id));
                        card.SetDesc(RuValue(relic.Id + ".desc"));
                        if (detailed)
                        {
                            // Те же числа, что рисует панель деталей инвентаря — из общего каскада,
                            // а не из полей ассета: подсказка не имеет права считать по-своему (§II.10.1).
                            var lines = _stats.Basic(relic);
                            for (int i = 0; lines != null && i < lines.Count; i++)
                                card.AddLine(lines[i].LabelFallback, lines[i].Value);
                        }
                        return card;

                    case Guildmaster.UI.Tooltips.TooltipKind.Keyword:
                        string kwId = Guildmaster.Data.Descriptions.KeywordMarkup.FullId(request.Id);
                        string kwName = RuValue(kwId + ".name");
                        if (string.IsNullOrEmpty(kwName)) return null;
                        card.SetTitle(kwName);
                        card.SetDesc(Rendered(RuValue(kwId + (detailed ? ".desc.full" : ".desc"))));
                        return card;

                    default:
                        return null;
                }
            }
        }

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
