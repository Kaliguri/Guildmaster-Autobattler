#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.UI.Tooltips;
using UnityEditor;
using UnityEngine;
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
            ["reward"]       = BuildReward,
            ["event"]        = BuildEvent,
            ["loadout-inventory"] = BuildLoadoutInventory,
            ["party"]        = BuildParty,
            ["items"]        = BuildItems,
            ["vessel-card"]  = BuildVesselCard,
            ["settings"]     = BuildSettings,
            ["loadout"]      = BuildLoadout,
            ["camp"]         = BuildCamp,
            ["profile"]      = BuildProfile,
            ["slotcreate"]   = BuildSlotCreate,
            ["pause"]        = BuildPause,
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
            ["dev-log"]      = BuildDevLog,
            ["dev-battles"]  = BuildDevBattles,
        };

        /// <summary>
        /// Порядок показа: по ПУТИ ИГРОКА, а не по тому, что раньше дописали.
        /// </summary>
        /// <remarks>
        /// <b>Свой список, а не ключи словаря.</b> Заказ Макса 23.08.2026: «Надо видеть сначала главные,
        /// основные экраны, а лишь потом менее важные, а не как сейчас». До этого порядок кадров задавал
        /// порядок вставки в словарь — и витрина открывалась мёртвым дев-выбором боя. Порядок словаря
        /// вдобавок ничем не гарантирован: одно удаление ключа, и он перестроится молча.
        /// <para>Гейт <c>UiScreenCatalogGateTests</c> следит, чтобы список и словарь не разошлись.</para>
        /// </remarks>
        private static readonly string[] Order =
        {
            // Вход в игру: с этого начинается любая сессия.
            "mainmenu", "newgame", "profile", "slotcreate", "guilds",
            // Дом гильдии и подготовка отряда.
            "hub", "party", "items", "loadout", "loadout-inventory", "vessel-card",
            // Забег: узлы и их исход.
            "titlecard", "shop", "chest", "event", "camp", "reward", "outcome",
            // Служебное: видно игроку, но не по ходу игры.
            "settings", "pause",
            // Дев-полки: F1 команды, F2 лог движка, F3 витрина боёв.
            "devconsole", "dev-log", "dev-battles",
        };

        /// <summary>Все известные цели в порядке показа (для меню, прогона кадров и подсказок).</summary>
        public static IEnumerable<string> Ids => Order;

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

        /// <summary>
        /// Полка F2 — лог движка. Строки те же, что у консоли: полка отвечает за ХВОСТ сообщений, и
        /// правдоподобие кадра держится на смеси видов записи, а не на их количестве.
        /// </summary>
        private static void BuildDevLog(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/DevLogScreen.uxml");
            if (uxml == null) { AddError(root, "DevLogScreen.uxml не найден"); return; }

            var screen = new Guildmaster.UI.DevConsole.DevLogScreen(uxml, SampleLog());
            screen.Build(new Guildmaster.UI.UiScreenContext(root, RuValue));
            root.Add(screen.Root);
        }

        /// <summary>
        /// Полка F3 — витрина боёв: то, чем на самом деле запускают бой из игры. Прежде на её месте в
        /// каталоге стоял <c>DevBattlePicker</c> — экран, который из игры не открывается ниоткуда, и
        /// кадр выходил пустым (наход. Макса 23.08.2026).
        /// </summary>
        private static void BuildDevBattles(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/DevBattleBrowser.uxml");
            if (uxml == null) { AddError(root, "DevBattleBrowser.uxml не найден"); return; }

            var screen = new DevBattleBrowserScreen(uxml, SampleRegistry(), LoadContent());
            screen.Build(new Guildmaster.UI.UiScreenContext(root, RuValue));
            root.Add(screen.Root);
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
                gold: 250,
                onChosen: _ => { });
            root.Add(screen);
        }

        /// <summary>
        /// Страница «Отряд». Состав тут выдуманный: экран собирается БЕЗ живого забега — иначе
        /// посмотреть его можно было бы только в игре, а именно этого стенд и избегает.
        /// </summary>
        private static void BuildParty(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/PartyScreen.uxml");
            if (uxml == null) { AddError(root, "PartyScreen.uxml не найден"); return; }

            // Реликвии настоящие: лицо человека — портрет её архетипа, и на выдуманном имени его нет.
            IReadOnlyList<RelicData> allRelics = LoadContent()?.All<RelicData>() ?? Array.Empty<RelicData>();
            RelicData Relic(int i)
            {
                int seen = 0;
                for (int k = 0; k < allRelics.Count; k++)
                {
                    if (allRelics[k] == null || allRelics[k].Id == ContentIds.BaseRelic) continue;
                    if (seen++ == i) return allRelics[k];
                }
                return null;
            }
            Sprite FaceOf(RelicData r) =>
                r == null ? null : (r.Archetype != null && r.Archetype.Portrait != null ? r.Archetype.Portrait : r.Icon);

            var slots = new List<Guildmaster.UI.PartySlotView>
            {
                new(0, "Ирма", RuName(Relic(0)?.Id), inBattle: true,  open: true,  portrait: FaceOf(Relic(0))),
                new(1, "Кай",  RuName(Relic(1)?.Id), inBattle: true,  open: true,  portrait: FaceOf(Relic(1))),
                new(2, "Дан",  RuName(Relic(2)?.Id), inBattle: true,  open: true,  portrait: FaceOf(Relic(2))),
                new(3, "Сув",  RuName(Relic(3)?.Id), inBattle: true,  open: true,  portrait: FaceOf(Relic(3))),
                new(4, "Лех",  RuName(Relic(4)?.Id), inBattle: false, open: true,  portrait: FaceOf(Relic(4))),
                new(5, null,   null,                 inBattle: false, open: true),
                new(6, null,   null,                 inBattle: false, open: false),
                new(7, null,   null,                 inBattle: false, open: false),
            };

            VisualElement screen = Guildmaster.UI.PartyScreenView.Build(
                uxml, slots, localize: RuValue, battleSlots: 4, actions: null);
            MountSampleInspect(screen);
            root.Add(screen);
        }

        /// <summary>
        /// Панель осмотра на стенде: она общая для всех экранов, и смотреть её отдельно нет смысла —
        /// важно, сколько места она отнимает у состава.
        /// </summary>
        private static void MountSampleInspect(VisualElement screen)
        {
            VisualElement host = screen.Q<VisualElement>("inspect-host");
            if (host == null) return;

            var subject = new Guildmaster.UI.Components.InspectSubject(
                "Кай", "Щит · в бою",
                new[] { ("HP", "820"), ("броня", "24"), ("урон", "41"), ("скорость", "3.2") },
                new[] { ("trait.steady", "Стойкий", true), ("trait.slow", "Тугодум", false) },
                new[] { "item.boots", string.Empty, string.Empty, null },
                "держит строй", "relic.bulwark");

            host.Add(Guildmaster.UI.Components.InspectPanel.Build(subject, localize: RuValue));
        }

        /// <summary>Страница «Предметы» на выдуманном составе — по той же причине, что и «Отряд».</summary>
        private static void BuildItems(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/ItemsScreen.uxml");
            if (uxml == null) { AddError(root, "ItemsScreen.uxml не найден"); return; }

            // Вещи и лица берутся из НАСТОЯЩЕЙ базы: выдуманные id не имеют ни значка, ни имени, и
            // кадр показывал сырые ключи в слотах вместо предметов (наход. Макса 23.08.2026).
            IContentDatabase content = LoadContent();
            IReadOnlyList<ItemData> allItems = content?.All<ItemData>() ?? Array.Empty<ItemData>();
            IReadOnlyList<RelicData> allRelics = content?.All<RelicData>() ?? Array.Empty<RelicData>();

            string Item(int i) => i < allItems.Count && allItems[i] != null ? allItems[i].Id : string.Empty;
            RelicData Relic(int i)
            {
                int seen = 0;
                for (int k = 0; k < allRelics.Count; k++)
                {
                    if (allRelics[k] == null || allRelics[k].Id == ContentIds.BaseRelic) continue;
                    if (seen++ == i) return allRelics[k];
                }
                return null;
            }

            Sprite FaceOf(RelicData r) =>
                r == null ? null : (r.Archetype != null && r.Archetype.Portrait != null ? r.Archetype.Portrait : r.Icon);

            var rows = new List<Guildmaster.UI.ItemsRowView>
            {
                new(0, "Ирма", RuName(Relic(0)?.Id), new[] { Item(0), string.Empty, string.Empty, null }, FaceOf(Relic(0))),
                new(1, "Кай",  RuName(Relic(1)?.Id), new[] { Item(1), Item(2), string.Empty, null },      FaceOf(Relic(1))),
                new(2, "Дан",  RuName(Relic(2)?.Id), new[] { string.Empty, string.Empty, string.Empty, null }, FaceOf(Relic(2))),
                new(3, "Сув",  RuName(Relic(3)?.Id), new[] { Item(3), string.Empty, string.Empty, null }, FaceOf(Relic(3))),
            };
            var stash = new List<string> { Item(4), Item(5), Item(6) };

            Sprite IconOf(string id) =>
                !string.IsNullOrEmpty(id) && content != null && content.TryGet(id, out ItemData it) && it != null
                    ? it.Icon : null;

            VisualElement screen = Guildmaster.UI.ItemsScreenView.Build(
                uxml, rows, stash, localize: RuValue, iconOf: IconOf, nameOf: RuName, actions: null);
            MountSampleInspect(screen);
            root.Add(screen);
        }

        /// <summary>Расширенная карточка «Сосуда»: разворот с табами, оба таба переключаются на стенде.</summary>
        private static void BuildVesselCard(VisualElement root)
        {
            var subject = new Guildmaster.UI.VesselCardSubject(
                "Кай, сын каменотёса", "Щит · в бою",
                new[] { ("HP", "820"), ("броня", "24"), ("маг. броня", "12"), ("урон", "41") },
                new[] { ("trait.steady", "Стойкий", true), ("trait.slow", "Тугодум", false) },
                new[] { "item.boots", "item.amulet", string.Empty, null },
                (2, 1, 0),
                new[] { "Ушиб колена", "Рана плеча" },
                "Стойкость",
                new[] { "Пришёл из каменоломни у Серых Врат,", "когда гильдия взяла его отца." },
                new[] { ("боёв", "48"), ("побед", "41"), ("смертей в бою", "2"), ("походов", "6") },
                "relic.bulwark");

            int tab = 0;
            var host = new VisualElement();
            host.style.flexGrow = 1;
            void Draw()
            {
                host.Clear();
                host.Add(Guildmaster.UI.VesselCardView.Build(
                    subject, tab, onTab: i => { tab = i; Draw(); }, onClose: null, onRelic: null, localize: RuValue));
            }
            Draw();
            root.Add(host);
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
                titleOf: r => RuName(r?.Id),
                narrativeOf: r => Coalesce(RuValue((r?.Id) + ".desc"), "«Древний завет, что тлеет в глубине веков…»"),
                localize: RuValue,
                lockedSlots: 3,
                cardAnimations: true,
                cardAttackAnimation: true,
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

            // Тот же Build, что зовёт игра: стенд собирает экран НЕ своим кодом, иначе кадр показывал бы
            // стенд, а не игру (правило Макса 23.08.2026).
            // Возврат стенду нужен НЕ ради нажатия, а ради места: без действия вид его не поставит,
            // и кадр не покажет кнопку, которая в игре есть.
            Guildmaster.UI.SettingsScreenView view = Guildmaster.UI.SettingsScreenView.Build(
                uxml, RuValue, onLeave: () => { });

            // Значения статичны: стенд не поднимает ни VM, ни IDisplayService — он показывает вид, а не
            // поведение. Списки берутся у настоящего монитора, чтобы видеть честную длину.
            view.Master?.SetValueWithoutNotify(0.8f);
            view.Music?.SetValueWithoutNotify(0.65f);
            view.Sfx?.SetValueWithoutNotify(1.0f);

            var resolutions = new List<string>();
            var rates = new List<string>();
            foreach (UnityEngine.Resolution res in UnityEngine.Screen.resolutions)
            {
                string item = $"{res.width} x {res.height}";
                if (!resolutions.Contains(item)) resolutions.Add(item);

                string rate = $"{res.refreshRateRatio.value:0.##} Гц";
                if (!rates.Contains(rate)) rates.Add(rate);
            }

            view.WindowMode?.SetChoices(new List<string> { "Окно без рамок", "Полноэкранный", "Оконный" }, 0);
            view.Resolution?.SetChoices(resolutions, resolutions.Count - 1);
            view.RefreshRate?.SetChoices(rates, rates.Count - 1);

            // Частота гаснет вне эксклюзивного полноэкранного — показываем именно это состояние,
            // потому что оно и есть по умолчанию (окно без рамок).
            view.RefreshRate?.SetRowEnabled(false);
            view.ShowVideoHint("Частоту обновления можно менять только в полноэкранном режиме.");

            root.Add(view.Root);
        }

        private static void BuildLoadout(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/LoadoutScreen.uxml");
            if (uxml == null) { AddError(root, "LoadoutScreen.uxml не найден"); return; }

            IContentDatabase content = LoadContent();
            if (content == null) { AddError(root, "ContentDatabase не найдена"); return; }

            IReadOnlyList<RelicData> relics = content.All<RelicData>();

            // Тот же Build, что зовёт игра. Стенд не поднимает LoadoutViewModel — он подставляет
            // готовые строки в детали и отмечает первую карточку выбранной, чтобы на кадре было видно
            // и обычную карточку, и выделенную.
            Guildmaster.UI.LoadoutScreenView view = Guildmaster.UI.LoadoutScreenView.Build(
                uxml, relics, r => Coalesce(RuValue(r.Id + ".name"), r.Id), RuValue, onClose: () => { });

            RelicData first = view.FirstRelic;
            view.SyncCards(r => r == first, r => r == first);
            view.ShowDetail(
                Coalesce(RuValue((first?.Id) + ".name"), first?.Id ?? "Реликвия"),
                Coalesce(RuValue((first?.Id) + ".desc"), "«Древний завет, что тлеет в глубине веков…»"),
                "боевая · редкая",
                "урон +12 · броня +4");

            root.Add(view.Root);
        }

        private static void BuildCamp(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/CampScreen.uxml");
            if (uxml == null) { AddError(root, "CampScreen.uxml не найден"); return; }

            // Привал со свежим бюджетом: на кадре видно полный счётчик действий и все кнопки живыми.
            root.Add(Guildmaster.UI.CampScreenView.Build(
                uxml, new Guildmaster.Guild.CampSession(), RuValue, onLeave: () => { }));
        }

        private static void BuildProfile(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/ProfileScreen.uxml");
            if (uxml == null) { AddError(root, "ProfileScreen.uxml не найден"); return; }

            // Три слота из пяти заняты: видно и активный профиль, и соседний, и пустое место.
            var slots = new List<Guildmaster.UI.ProfileScreenView.SlotEntry>
            {
                new("p1", "Гильдмастер", true),
                new("p2", "Второй заход", false),
                new("p3", "Проба", false),
            };

            var palette = LoadFirst<Guildmaster.Data.Definitions.GuildmasterPalette>();
            var emblems = LoadFirst<Guildmaster.Data.Definitions.GuildEmblemCatalog>();

            root.Add(Guildmaster.UI.ProfileScreenView.Build(
                uxml, slots, slotLimit: 5, identity: default, steamName: "Игрок",
                skins: null, colorCount: Guildmaster.Core.Players.PlayerColors.Count, palette: palette,
                canLeave: true, customize: false, localize: RuValue,
                onSelect: null, onCreate: null, onDelete: null, onSave: null, onPreview: null, onBack: null,
                emblemOf: id => emblems != null ? emblems.Resolve(id) : null,
                shadeOf: index => palette != null &&
                                  palette.TryGet(Guildmaster.Core.Players.PlayerColors.TokenOf(index),
                                                 out UnityEngine.Color shade)
                                      ? shade
                                      : UnityEngine.Color.white));
        }

        private static void BuildSlotCreate(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/SlotCreateScreen.uxml");
            if (uxml == null) { AddError(root, "SlotCreateScreen.uxml не найден"); return; }

            root.Add(Guildmaster.UI.SlotCreateView.Build(
                uxml, Guildmaster.UI.SlotCreateView.SlotKind.Guild, "Новая гильдия",
                LoadFirst<Guildmaster.Data.Definitions.GuildEmblemCatalog>(),
                LoadFirst<Guildmaster.Data.Definitions.GuildmasterPalette>(),
                Guildmaster.Core.Players.PlayerColors.Count, RuValue,
                onCreate: null, onBack: null));
        }

        private static void BuildPause(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/PauseScreen.uxml");
            if (uxml == null) { AddError(root, "PauseScreen.uxml не найден"); return; }

            // Приглашение доступно: на кадре нужен обычный вид пункта, а выключенный он показан в
            // контактном листе элементов.
            root.Add(Guildmaster.UI.PauseScreenView.Build(uxml, RuValue, canInvite: true).Root);
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
            var summary = new List<Guildmaster.Guild.OutcomeSummaryRow>
            {
                new("ui.outcome.nodes", "Пройдено узлов", "7"),
                new("ui.outcome.gold",  "Золота собрано", "310"),
                new("ui.outcome.time",  "Время забега",   "24 мин"),
            };

            root.Add(Guildmaster.UI.OutcomeScreenView.Build(
                uxml, victory: true, RuValue, summary, glyph: null, onToMenu: () => { }));
        }

        private static void BuildMainMenu(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/MainMenuScreen.uxml");
            if (uxml == null) { AddError(root, "MainMenuScreen.uxml не найден"); return; }
            // Конфиг сообщества грузится так же, как контент: без него правая панель меню
            // (новости, отчёт об ошибке, ссылки, вишлист) остаётся пустой и на кадре её просто нет —
            // наход. Макса 23.08.2026 по первому же прогону кадров экранов.
            var community = AssetDatabase.LoadAssetAtPath<Guildmaster.Data.Definitions.CommunityConfig>(
                "Assets/_Project/ScriptableObjects/Configs/CommunityConfig.asset");

            root.Add(Guildmaster.UI.MainMenuScreenView.Build(
                uxml, RuValue, () => { }, () => { }, () => { }, () => { },
                canJoin: true, community: community));
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

            var palette = LoadFirst<Guildmaster.Data.Definitions.GuildmasterPalette>();
            var emblems = LoadFirst<Guildmaster.Data.Definitions.GuildEmblemCatalog>();

            root.Add(Guildmaster.UI.GuildSelectScreenView.Build(
                uxml, guilds, slotLimit: 4, RuValue,
                emblemOf: id => emblems != null ? emblems.Resolve(id) : null,
                shadeOf: index => palette != null &&
                                  palette.TryGet(Guildmaster.Core.Players.PlayerColors.TokenOf(index),
                                                 out UnityEngine.Color shade)
                                      ? shade
                                      : UnityEngine.Color.white,
                onPick: _ => { }, onBack: () => { }));
        }

        /// <summary>Двор гильдии — пока заглушка с меткой и единственной кнопкой.</summary>
        private static void BuildHub(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/HubScreen.uxml");
            if (uxml == null) { AddError(root, "HubScreen.uxml не найден"); return; }

            root.Add(Guildmaster.UI.HubScreenView.Build(
                uxml, "Гильдия 1", RuValue, () => { }, canStartRun: true,
                stage: (1, 8, "act.1.title"), onLeave: () => { }));
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

            var screen = new Guildmaster.UI.DevConsole.DevConsoleScreen(uxml, SampleRegistry(), SampleLog());
            screen.Build(new Guildmaster.UI.UiScreenContext(root, RuValue));
            root.Add(screen.Root);

            // Набранный префикс: палитра раскрывается, ghost дорисовывает общее продолжение.
            var field = screen.Root.Q<TextField>("console-field");
            if (field != null) field.value = "gm_sep";
        }

        /// <summary>Стендовый реестр команд: общий на все три дев-полки — они читают ОДИН реестр и в игре.</summary>
        private static Guildmaster.Core.DevConsole.DevCommandRegistry SampleRegistry()
        {
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
            return registry;
        }

        /// <summary>Стендовый лог: по строке на каждый вид записи — иначе кадр не показывает их цвета.</summary>
        private static Guildmaster.Core.DevConsole.DevConsoleLog SampleLog()
        {
            var log = new Guildmaster.Core.DevConsole.DevConsoleLog();
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Info, "[BattleStartup] - арена собрана: 4 против 3");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Echo, "> gm_arena_swap stone");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Reply, "облик «stone» надет, переход 0.8 с");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Echo, "> gm_sep_radius");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Error, "мало аргументов. Форма: gm_sep_radius <value>");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Warn, "[AudioService] - банк 'sfx_combat' уже загружен");
            log.Append(Guildmaster.Core.DevConsole.DevLogKind.Info, "[BattleTape] - лента: 1214 событий, показ на тике 342");
            return log;
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
