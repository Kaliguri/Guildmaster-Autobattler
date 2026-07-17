#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
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
            ["loadout-hub"]  = BuildLoadoutHub,
            ["run-topbar"]   = BuildRunTopBar,
            ["settings"]     = BuildSettings,
            ["map"]          = BuildMap,
            ["shop"]         = BuildShop,
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
                    if (all[i] != null && all[i].Id != "relic.base") choices.Add(all[i]);
            }

            // Без loc-сервиса в стенде: имя = короткий id, статичные подписи берут RU-фолбэк из View.
            VisualElement screen = Guildmaster.UI.RewardScreenView.Build(
                uxml, choices, inventoryFull: false, currentInventory: null,
                nameOf: r => RuName(r?.Id),
                localize: RuValue,
                onTake: (_, __) => { },
                onSkip: () => { });
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
                onChosen: _ => { },
                onContinue: () => { });
            root.Add(screen);
        }

        private static void BuildLoadoutHub(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/LoadoutHubScreen.uxml");
            if (uxml == null) { AddError(root, "LoadoutHubScreen.uxml не найден"); return; }

            IContentDatabase content = LoadContent();
            var roster  = new List<Guildmaster.UI.LoadoutHubView.RosterEntry>();
            var stash   = new List<RelicData>();
            var banners = new List<ItemData>();
            if (content != null)
            {
                IReadOnlyList<RelicData>  relics  = content.All<RelicData>();
                IReadOnlyList<VesselData> vessels = content.All<VesselData>();
                IReadOnlyList<ItemData>   items   = content.All<ItemData>();

                // 4 сосуда команды + надетый релик (для превью — по индексу из пула).
                for (int i = 0; vessels != null && i < vessels.Count && roster.Count < 4; i++)
                {
                    RelicData r = (relics != null && relics.Count > i) ? relics[i] : null;
                    roster.Add(new Guildmaster.UI.LoadoutHubView.RosterEntry(vessels[i], r));
                }
                // Фолбэк: VesselData ассетов ещё нет (скелет Фазы 2/4) — набиваем 4 слота реликами,
                // чтобы в превью была видна раскладка команды (Vessel=null → карточка берёт имя релика).
                for (int i = 0; relics != null && roster.Count < 4 && i < relics.Count; i++)
                    if (relics[i] != null && relics[i].Id != "relic.base")
                        roster.Add(new Guildmaster.UI.LoadoutHubView.RosterEntry(null, relics[i]));
                for (int i = 0; relics != null && i < relics.Count && stash.Count < 6; i++)
                    if (relics[i] != null && relics[i].Id != "relic.base") stash.Add(relics[i]);
                for (int i = 0; items != null && i < items.Count && banners.Count < 2; i++)
                    if (items[i] != null && items[i].Scope == ItemScope.Party) banners.Add(items[i]);
            }

            VisualElement screen = Guildmaster.UI.LoadoutHubView.Build(
                uxml, roster, banners, stash, gold: 120,
                nameOf: id => RuName(id), localize: RuValue,
                onVesselClick: _ => { }, onClose: () => { });
            root.Add(screen);
        }

        private static void BuildRunTopBar(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/RunTopBar.uxml");
            if (uxml == null) { AddError(root, "RunTopBar.uxml не найден"); return; }

            VisualElement screen = Guildmaster.UI.RunTopBarView.Build(
                uxml, gold: 120, actNumber: 1, timerText: "12:34",
                localize: null, onHub: () => { }, onSettings: () => { }, onStart: () => { });
            root.Add(screen);
        }

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
            root.Add(r);
        }

        private static void SetSliderRow(VisualElement root, string name, string label, float value)
        {
            var row = root.Q<Guildmaster.UI.Components.SliderRow>(name);
            if (row == null) return;
            row.LabelText = label;
            row.SetValueWithoutNotify(value);
        }

        private static void BuildMap(VisualElement root)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Screens/MapScreen.uxml");
            if (uxml == null) { AddError(root, "MapScreen.uxml не найден"); return; }

            // Стендовая карта из фикс-сида (детерминирована), доступные узлы — соседи старта.
            var map = Guildmaster.Guild.MapGenerator.Generate(
                new Guildmaster.Core.Random.XorShiftRng(7), new Guildmaster.Guild.MapGenConfig());
            var available = new HashSet<string>();
            foreach (var n in Guildmaster.Guild.MapTraversal.AvailableNext(map)) available.Add(n.Id);

            VisualElement screen = Guildmaster.UI.MapScreenView.Build(uxml, map, available, RuValue, _ => { });
            root.Add(screen);
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
                    if (all[i] != null && all[i].Id != "relic.base") relics.Add(all[i]);
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

        private static void BuildGallery(VisualElement root)
        {
            IContentDatabase content = LoadContent();
            var relics = new List<RelicData>();
            if (content != null)
            {
                IReadOnlyList<RelicData> all = content.All<RelicData>();
                for (int i = 0; all != null && i < all.Count && relics.Count < 6; i++)
                    if (all[i] != null && all[i].Id != "relic.base") relics.Add(all[i]);
            }
            UnityEngine.Sprite Ico(int i) => i < relics.Count ? relics[i].Icon : null;
            string Nm(int i) => i < relics.Count ? Short(relics[i].Id) : "—";

            // Пересоздаём риг галереи (прошлые камеры/RT освобождаем).
            _galleryRig?.Dispose();
            _galleryRig = new Guildmaster.UI.Components.RelicCardVisualRig();

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
        }

        // ── Стендовые данные ─────────────────────────────────────────────────

        private static IContentDatabase LoadContent()
        {
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>(ContentDbPath);
            return db != null ? new ContentRegistry(db.Entries) : null;
        }

        private static void AddError(VisualElement root, string msg)
        {
            var label = new Label("UI Preview: " + msg);
            label.AddToClassList("gm-dev-empty");
            root.Add(label);
        }

        /// <summary>Короткий id без домена (<c>relic.assassin</c> → <c>assassin</c>) — фолбэк без loc.</summary>
        private static string Short(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int dot = id.IndexOf('.');
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }

        // Живой риг для галереи: держим статикой (камеры URP рендерят каждый кадр), пересоздаём при ребилде.
        private static Guildmaster.UI.Components.RelicCardVisualRig _galleryRig;

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
        private static string RuName(string id) => Coalesce(RuValue(id + ".name"), Short(id));

        private static string Coalesce(string a, string b) => string.IsNullOrEmpty(a) ? b : a;
    }
}
#endif
