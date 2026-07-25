using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка нового лоадаут/инвентарь-экрана (редизайн, Ф3a каркас) из UXML-шаблона.
    /// Полноэкранный трёхколоночник: боевая зона-«дырка» слева | грид таро-карточек реликвий |
    /// панель деталей (видео 16:9, способности, улучшения, нарратив). Общий код для роутера и
    /// превью-стенда, по образцу <see cref="LoadoutHubView"/>.
    /// <para>
    /// Ф3a — КАРКАС: живая зона/видео/способности/улучшения — плейсхолдеры; наполнение боевой
    /// зоны (camera.rect), hover-анимации и ПКМ-модалки — следующие фазы. Разметка/стиль — только
    /// из <c>LoadoutInventoryScreen.uxml</c> + <c>RelicArcanaCard.uxml</c> + классы дизайн-системы.
    /// </para>
    /// </summary>
    public static class LoadoutInventoryView
    {
        private const int AbilitySlots = 3;   // плейсхолдер-ряд способностей
        private const int UpgradesPerRow = 3; // 6 улучшений = 2 ряда × 3 (T1/T2)

        public static VisualElement Build(
            VisualTreeAsset screenUxml,
            VisualTreeAsset cardUxml,
            IReadOnlyList<RelicData> relics,
            int gold,
            Func<RelicData, string> titleOf,
            Func<RelicData, string> narrativeOf,
            Func<string, string> localize,
            int lockedSlots = 0,
            bool cardAnimations = true,
            bool cardAttackAnimation = true,
            Action<RelicData, RelicDragPhase> onRelicDrag = null,
            Func<RelicData, IReadOnlyList<TagData>> tagsOf = null,
            Func<RelicData, IReadOnlyList<UnitStatLine>> statsOf = null)
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement screen = screenUxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            // Корень/тело/боевая «дырка» — picking-mode Ignore (задано в UXML): panel.Pick над ними
            // возвращает null, и ввод под инвентарём идёт в мир (drag юнитов и камера в расстановке).
            // НЕ ставить Position: он перехватывает pick на ВЕСЬ экран (root покрывает всё) и глушит
            // дырку — под инвентарём переставал стартовать деплой-драг (press гейтится !PointerOverUI).
            // Непрозрачные панели .gm-loadout__main ловят pick сами (дефолтный Position).
            root.pickingMode = PickingMode.Ignore;

            // ── Хром тела (гильдия/режимы/золото/меню теперь в глобальном топбаре RunModeBar) ──
            // Левая зона (battle-zone) — «дырка» СКВОЗЬ панель к реальной боевой камере (Ф3b P1): прозрачна,
            // мир виден через неё (пока загружена боевая сцена). Подсказку прячем — она была для мок-заглушки.
            var battleHint = root.Q<Label>("battle-hint");
            if (battleHint != null) battleHint.style.display = DisplayStyle.None;
            // Фильтры-чипы (иконка + подпись, п.1): Реликвии активны по умолчанию, клик переключает
            // подсветку. Фильтрация по категории — отдельная фаза; здесь пока только визуальный выбор.
            var filterChips = new[]
            {
                (chip: root.Q<Chip>("filter-relics"),  label: L("ui.loadout.filter.relics", "Реликвии")),
                (chip: root.Q<Chip>("filter-items"),   label: L("ui.loadout.filter.items", "Предметы")),
                (chip: root.Q<Chip>("filter-banners"), label: L("ui.loadout.filter.banners", "Знамёна")),
            };
            for (int i = 0; i < filterChips.Length; i++)
            {
                Chip chip = filterChips[i].chip;
                if (chip == null) continue;
                chip.Text = filterChips[i].label;
                chip.SetActive(i == 0);
                chip.RegisterCallback<ClickEvent>(_ =>
                {
                    foreach (var fc in filterChips) fc.chip?.SetActive(fc.chip == chip);
                });
            }
            SetBtn (root, "sort", L("ui.loadout.sort.name", "Имя") + " ↓");
            SetText(root, "video-hint", L("ui.loadout.video", "видео-вставка 16:9"));
            SetText(root, "skills-label", L("ui.loadout.skills", "Способности"));
            SetText(root, "upgrades-label", L("ui.loadout.upgrades", "Улучшения"));
            SetText(root, "stats-label", L("ui.loadout.stats", "Характеристики"));

            var search = root.Q<TextField>("search");
            if (search != null) SetPlaceholder(search, L("ui.loadout.search", "Поиск…"));

            // ── Способности (плейсхолдер-ряд) + улучшения (2 ряда × 3, плейсхолдеры) ──
            var abilities = root.Q<VisualElement>("detail-abilities");
            for (int i = 0; abilities != null && i < AbilitySlots; i++)
                abilities.Add(new Slot { Size = Slot.SlotSize.Sm });

            FillUpgradeRow(root.Q<VisualElement>("upgrade-row-1"));
            FillUpgradeRow(root.Q<VisualElement>("upgrade-row-2"));

            // ── Теги «быстрого чтения» (ряд под именем): реальные теги юнита из UnitTagResolver,
            //    иконка + подпись, порядок осей Role→DamageType→Playstyle→Mechanic с «|» между группами.
            //    Заполняется per-relic в ShowDetail (набор зависит от выбранного релика).
            //    Высота ряда фиксирована (3 строки, USS) — лишние теги сворачиваются в чип «+N»
            //    с подсказкой по наведению; подсказка живёт оверлеем в корне экрана. ──
            var tags = root.Q<VisualElement>("detail-tags");
            var tagTooltip = new Tooltip();
            root.Add(tagTooltip);

            // ── Статблок (внизу): 8 квадратов «значение над подписью», 4 в ряд. Числа — РЕАЛЬНЫЕ,
            //    из IUnitStatPreview (тот же каскад, что собирает бой); заполняется per-relic
            //    в ShowDetail. Нет шва (dev-стенд без DI) — блок прячется, а не врёт заглушками. ──
            var stats = root.Q<VisualElement>("detail-stats");
            var statsSection = stats?.parent;

            // ── Грид таро-карточек ──
            var grid = root.Q<ScrollView>("relic-grid");
            if (grid == null) return root;
            // Вертикальный скролл; сам грид — ОТДЕЛЬНЫЙ контейнер внутри ScrollView, а не его
            // contentContainer: в режиме Vertical ScrollView инлайном форсит column/no-wrap на
            // contentContainer, и USS-перенос не применится. Свой контейнер (width:100% из USS)
            // корректно заворачивает ряд карточек.
            grid.mode = ScrollViewMode.Vertical;
            grid.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // Вертикальный скроллбар ВСЕГДА виден (реш. Макса): его ширина зарезервирована постоянно,
            // грид не дёргается при появлении/исчезании прокрутки, и место под него всегда учтено.
            grid.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            var gridEl = new VisualElement();
            gridEl.AddToClassList("gm-loadout__grid");
            grid.Add(gridEl);

            var cards = new List<(RelicData relic, VisualElement card)>();
            var rts = new Dictionary<RelicData, RenderTexture>();
            IVisualElementScheduledItem animLoop = null;
            RenderTexture activeRt = null;

            // Риг анимированных спрайтов карточек: боевой ViewPrefab → RT. ВСЕ карты стоят статично (idle-кадр);
            // двигается ТОЛЬКО выбранный юнит — цикл idle→attack (план 10 §5). Живёт, пока открыт экран.
            var rig = new RelicCardVisualRig();
            root.RegisterCallback<DetachFromPanelEvent>(_ => { animLoop?.Pause(); rig.Dispose(); });

            // ТОЛЬКО детали + подсветка выбора. Анимацию НЕ трогает (на старте всё статично, пока не кликнули).
            void ShowDetail(RelicData r)
            {
                SetText(root, "detail-title", (Title(r, titleOf) ?? "—").ToUpperInvariant());
                SetText(root, "detail-narrative", narrativeOf?.Invoke(r) ?? string.Empty);
                if (tags != null) FillTags(tags, tagsOf?.Invoke(r), L, tagTooltip);
                if (stats != null) FillStats(stats, statsSection, statsOf?.Invoke(r), L);
                foreach (var (relic, card) in cards)
                    card.EnableInClassList("gm-arcana-card--selected", relic == r);
            }

            // Выбранный юнит гоняет idle→attack по кругу; предыдущий замораживается обратно в статику.
            void Animate(RelicData r)
            {
                animLoop?.Pause();
                if (activeRt != null) rig.SetFrozen(activeRt, true);
                activeRt = (r != null && rts.TryGetValue(r, out RenderTexture rt)) ? rt : null;
                if (activeRt == null) return;
                rig.SetFrozen(activeRt, false);
                rig.PlayIdle(activeRt);

                // Настройка «анимация атаки» выключена → выбранная карта живёт только в idle, без attack-цикла.
                if (!cardAttackAnimation) return;

                // Цикл: атака → ВОЗВРАТ в idle (ровно после клипа) → пауза 3с → снова атака. Раньше карта
                // застревала на последнем кадре атаки (у клипа нет exit-перехода в Idle) — возвращаем вручную
                // по длине клипа. Длина 0 (нет клипа) → фолбэк 800 мс.
                long attackMs = (long)(rig.AttackLengthSeconds(activeRt) * 1000f);
                if (attackMs <= 0) attackMs = 800;
                const long idleHoldMs = 3000;

                void Cycle()
                {
                    RenderTexture rt2 = activeRt;
                    if (rt2 == null) return;
                    rig.PlayAttack(rt2);
                    root.schedule.Execute(() => rig.PlayIdle(rt2)).StartingIn(attackMs);
                }

                animLoop = root.schedule.Execute(Cycle).Every(attackMs + idleHoldMs);
            }

            for (int i = 0; relics != null && i < relics.Count; i++)
            {
                RelicData relic = relics[i];
                VisualElement card = cardUxml.CloneTree();
                VisualElement cardRoot = card.childCount > 0 ? card[0] : card;

                // Визуал карточки: боевой спрайт через риг, СРАЗУ заморожен (статичный idle); нет ViewPrefab → портрет.
                var art = cardRoot.Q<VisualElement>("art");
                if (art != null)
                {
                    if (relic != null && relic.ViewPrefab != null)
                    {
                        RenderTexture rt = rig.Acquire(relic);
                        rig.SetFrozen(rt, true);
                        art.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                        rts[relic] = rt;
                    }
                    else
                    {
                        Sprite sprite = RelicSprite(relic);
                        if (sprite != null) art.style.backgroundImage = new StyleBackground(sprite);
                    }
                }

                SetText(cardRoot, "num", Roman(i + 1));
                SetText(cardRoot, "title", (Title(relic, titleOf) ?? relic.Id).ToUpperInvariant());

                // Клик → детали + запуск анимации ЭТОГО юнита (остальные замирают). Настройка «анимация
                // карточек» выключена → карты статичны (idle-кадр), Animate не зовём.
                cardRoot.RegisterCallback<ClickEvent>(_ =>
                {
                    ShowDetail(relic);
                    if (cardAnimations) Animate(relic);
                });
                WireRelicDrag(cardRoot, relic, onRelicDrag); // QA #5: тащить реликвию на юнита в мире
                gridEl.Add(cardRoot);
                cards.Add((relic, cardRoot));
            }

            // Заблокированные (не открытые) карты — тусклые с замком, ПОСЛЕ владеемых (лево-паковка).
            // Реальный owned/locked-сплит приходит с фильтром по владению (Фаза 5); пока — визуальный задел.
            for (int i = 0; i < lockedSlots; i++)
            {
                VisualElement card = cardUxml.CloneTree();
                VisualElement cardRoot = card.childCount > 0 ? card[0] : card;
                cardRoot.AddToClassList("gm-arcana-card--locked");
                SetText(cardRoot, "num", string.Empty);
                SetText(cardRoot, "title", string.Empty);
                gridEl.Add(cardRoot);
            }

            // Предвыбор первого релика — сразу заполнить панель деталей.
            if (cards.Count > 0) ShowDetail(cards[0].relic);

            // Рабочая сортировка по имени: клик по «Имя ↓» переключает направление и переставляет карты
            // (поиск-фильтрация — отдельная фаза; здесь только порядок). Locked-заглушки остаются в конце.
            var sortBtn = root.Q<Button>("sort");
            if (sortBtn != null)
            {
                string sortName = L("ui.loadout.sort.name", "Имя");
                bool sortDesc = false;

                void ApplySort()
                {
                    cards.Sort((a, b) => string.Compare(
                        Title(a.relic, titleOf) ?? a.relic?.Id ?? string.Empty,
                        Title(b.relic, titleOf) ?? b.relic?.Id ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase));
                    if (sortDesc) cards.Reverse();
                    for (int i = 0; i < cards.Count; i++)
                        gridEl.Insert(i, cards[i].card); // Insert перемещает уже добавленную карту в новый порядок
                    sortBtn.text = sortName + (sortDesc ? " ↑" : " ↓");
                }

                sortBtn.clicked += () => { sortDesc = !sortDesc; ApplySort(); };
            }

            return root;
        }

        private const float RelicDragThresholdSq = 36f; // (6 панельных ед)² — меньше сдвиг = клик, больше = drag

        // Drag карточки реликвии на юнита в мире (QA #5): pointer-capture на карте, порог клик/drag; за порогом
        // публикуем Start/Move/Drop — позицию курсора фаза расстановки берёт из своего ввода (событие лишь
        // держит жест активным). Клик (без drag) не трогаем — ClickEvent карты (выбор) срабатывает как раньше.
        private static void WireRelicDrag(VisualElement card, RelicData relic,
            Action<RelicData, RelicDragPhase> onDrag)
        {
            if (onDrag == null || relic == null) return;
            bool armed = false, dragging = false;
            Vector2 start = default;
            int ptr = -1;

            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                armed = true; dragging = false; start = e.position; ptr = e.pointerId;
                card.CapturePointer(e.pointerId);
            });
            card.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!armed) return;
                if (!dragging)
                {
                    if (((Vector2)e.position - start).sqrMagnitude < RelicDragThresholdSq) return; // ещё клик
                    dragging = true;
                    onDrag(relic, RelicDragPhase.Start);
                }
                onDrag(relic, RelicDragPhase.Move);
            });
            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (card.HasPointerCapture(ptr)) card.ReleasePointer(ptr);
                if (dragging) onDrag(relic, RelicDragPhase.Drop);
                armed = false; dragging = false;
            });
        }

        private static void FillUpgradeRow(VisualElement row)
        {
            // Sm — тот же размер, что у способностей. Md читался крупно: девять слотов на панели съедали
            // высоту, которой не хватало статблоку (реш. Макса 2026-07-25, второй заход).
            for (int i = 0; row != null && i < UpgradesPerRow; i++)
                row.Add(new Slot { Size = Slot.SlotSize.Sm });
        }

        // Ряд тегов «быстрого чтения»: чипы иконка+подпись в порядке осей, с «|» между группами (осями).
        // Ряд ограничен тремя строками (высота задана в USS) — что не влезло, уходит в чип «+N».
        private static void FillTags(VisualElement container, IReadOnlyList<TagData> tags,
            Func<string, string, string> L, Tooltip tooltip)
        {
            container.Clear();
            tooltip?.Hide();
            if (tags == null || tags.Count == 0) { container.style.display = DisplayStyle.None; return; }
            container.style.display = DisplayStyle.Flex;

            var names = new List<string>(tags.Count);
            TagCategory? prev = null;
            for (int i = 0; i < tags.Count; i++)
            {
                TagData t = tags[i];
                if (t == null) continue;
                if (prev.HasValue && t.Category != prev.Value) container.Add(TagSeparator());
                string name = L(t.Id + ".name", TagFallback(t.Id));
                container.Add(TagChip(t, name));
                names.Add(name);
                prev = t.Category;
            }

            // Свёртка считается по РЕАЛЬНОЙ раскладке, поэтому ждём первый прошедший layout
            // (до него ширины нулевые). Ширина панели фиксирована, так что замер нужен ровно один раз.
            void OnLaidOut(GeometryChangedEvent _)
            {
                container.UnregisterCallback<GeometryChangedEvent>(OnLaidOut);
                CollapseOverflowingTags(container, names, tooltip, L);
            }

            container.RegisterCallback<GeometryChangedEvent>(OnLaidOut);
        }

        private const int TagRows = 3;    // столько строк тегов помещается в ряд (высота — из USS)
        private const float TagRowHeight = 24f; // иконка 16 + вертикальные margin'ы чипа

        /// <summary>
        /// Симулирует перенос чипов по ширине контейнера и прячет всё, что не поместилось в
        /// <see cref="TagRows"/> строк, заменяя хвост чипом «+N» с подсказкой. Считаем сами, а не
        /// «скрыл — перезамерил»: каждое скрытие роняло бы новый layout-проход и мигание ряда.
        /// </summary>
        private static void CollapseOverflowingTags(VisualElement container, List<string> names,
            Tooltip tooltip, Func<string, string, string> L)
        {
            float width = container.resolvedStyle.width;
            if (width <= 0f || container.childCount == 0) return;

            var children = new List<VisualElement>(container.childCount);
            var widths = new List<float>(container.childCount);
            for (int i = 0; i < container.childCount; i++)
            {
                VisualElement el = container[i];
                children.Add(el);
                widths.Add(el.resolvedStyle.width + el.resolvedStyle.marginLeft + el.resolvedStyle.marginRight);
            }

            // Симуляция переноса на закэшированных ширинах. Гашение висячего разделителя сдвигает
            // остальные, поэтому крутим до стабилизации (гасить больше нечего) — но не более трёх
            // проходов: их и не нужно, каждый проход убирает как минимум один «|».
            int firstHidden = -1;
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                float x = 0f;
                int row = 0;
                firstHidden = -1;

                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i].style.display == DisplayStyle.None) continue;
                    float w = widths[i];
                    if (x > 0f && x + w > width) { row++; x = 0f; }
                    if (row >= TagRows) { firstHidden = i; break; }

                    // Разделитель осей, перенесённый в начало строки, висит палкой перед первым тегом
                    // и читается как обрыв. Такой «|» гасим: границу осей показывает сам перенос.
                    if (x == 0f && children[i].ClassListContains("gm-tag-sep"))
                    {
                        children[i].style.display = DisplayStyle.None;
                        changed = true;
                        continue;
                    }

                    x += w;
                }

                if (!changed) break;
            }

            if (firstHidden < 0) return; // всё поместилось — «+N» не нужен

            // Освобождаем место под сам чип «+N»: он встаёт в конец последней видимой строки,
            // при необходимости вытесняя ещё пару тегов (и висящие разделители — «|» в конце строки
            // выглядит обрывом фразы).
            var more = new Chip { Text = string.Empty, pickingMode = PickingMode.Position };
            more.AddToClassList("gm-chip--sm");
            more.AddToClassList("gm-tag");
            more.AddToClassList("gm-tag--more");

            int visible = firstHidden;
            while (visible > 0 && children[visible - 1].ClassListContains("gm-tag-sep")) visible--;

            int hiddenCount = 0;
            for (int i = visible; i < children.Count; i++)
            {
                bool alreadyHidden = children[i].style.display == DisplayStyle.None;
                children[i].style.display = DisplayStyle.None;
                if (!alreadyHidden && !children[i].ClassListContains("gm-tag-sep")) hiddenCount++;
            }
            if (hiddenCount == 0) return;

            more.Text = "+" + hiddenCount;
            container.Add(more);

            // Подсказка со скрытыми именами: список идёт в том же порядке осей, что и сам ряд.
            string hidden = string.Join(", ", names.GetRange(names.Count - hiddenCount, hiddenCount));
            more.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (tooltip == null) return;
                tooltip.Set(L("ui.loadout.tags.more", "Ещё теги"), null, hidden, null);
                VisualElement root = tooltip.parent;
                Vector2 pos = root.WorldToLocal(more.worldBound.position);
                tooltip.ShowAt(new Vector2(pos.x, pos.y + more.worldBound.height + 4f));
            });
            more.RegisterCallback<PointerLeaveEvent>(_ => tooltip?.Hide());
        }

        // Тег — ТОТ ЖЕ компонент Chip, что фильтры инвентаря и лента режимов, в малом размере
        // (--sm). Единый стиль держит компонент; вид/размер — целиком в USS, инлайн-стилей нет.
        private static VisualElement TagChip(TagData tag, string name)
        {
            var chip = new Chip { Text = name, pickingMode = PickingMode.Ignore };
            chip.AddToClassList("gm-chip--sm");
            chip.AddToClassList("gm-tag");
            chip.SetIcon(tag.Icon);
            return chip;
        }

        // Разделитель между осями тегов (Role | DamageType | Playstyle | Mechanic).
        private static VisualElement TagSeparator()
        {
            var sep = new Label("|") { pickingMode = PickingMode.Ignore };
            sep.AddToClassList("gm-tag-sep");
            return sep;
        }

        private static string TagFallback(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("tag.") ? id.Substring(4) : id;

        // Статблок выбранного кита: реальные числа из шва. Нет данных (dev-стенд без DI, пустой
        // релик) — прячем всю секцию вместе с подписью: пустая рамка «характеристики» врёт сильнее,
        // чем её отсутствие.
        private static void FillStats(VisualElement container, VisualElement section,
            IReadOnlyList<UnitStatLine> lines, Func<string, string, string> L)
        {
            container.Clear();
            bool has = lines != null && lines.Count > 0;
            if (section != null) section.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;

            for (int i = 0; i < lines.Count; i++)
                container.Add(MakeStat(L(lines[i].LabelKey, lines[i].LabelFallback), lines[i].Value));
        }

        // Квадрат статблока: значение над подписью.
        private static VisualElement MakeStat(string label, string value)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gm-stat");
            var v = new Label(value);
            v.AddToClassList("gm-stat__value");
            var l = new Label(label);
            l.AddToClassList("gm-stat__label");
            cell.Add(v);
            cell.Add(l);
            return cell;
        }

        private static string Title(RelicData r, Func<RelicData, string> titleOf)
            => r == null ? null : (titleOf != null ? titleOf(r) : r.Id);

        // Спрайт релика: портрет из UnitVisual, иначе иконка-фолбэк.
        private static Sprite RelicSprite(RelicData relic)
        {
            if (relic == null) return null;
            return relic.Visual != null && relic.Visual.Portrait != null ? relic.Visual.Portrait : relic.Icon;
        }

        // Номер аркана (плейсхолдер по индексу; финально — стабильный id из компендиума).
        private static string Roman(int n)
        {
            if (n <= 0) return "0";
            int[] vals = { 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] syms = { "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < vals.Length; i++)
                while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
            return sb.ToString();
        }

        private static void SetText(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }

        private static void SetBtn(VisualElement root, string name, string text)
        {
            var btn = root.Q<Button>(name);
            if (btn != null) btn.text = text;
        }

        private static void SetPlaceholder(TextField field, string text)
        {
#if UNITY_2023_1_OR_NEWER
            field.textEdition.placeholder = text;
#endif
        }
    }
}
