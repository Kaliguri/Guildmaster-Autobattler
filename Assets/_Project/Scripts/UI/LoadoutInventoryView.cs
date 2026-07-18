using System;
using System.Collections.Generic;
using Guildmaster.Core.Presentation;
using Guildmaster.Data.Definitions;
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
            IRosterStage battleStage = null)
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement screen = screenUxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            // ── Хром тела (гильдия/режимы/золото/меню теперь в глобальном топбаре RunModeBar) ──
            SetText(root, "battle-hint", L("ui.loadout.deployment", "Живая расстановка"));

            // ── Живая арена-«окно в мир» (Ф3b.1): RT roster-stage как фон левой зоны; подсказка гаснет. ──
            // Ф3b.2: пан/зум камеры — нативными UITK-событиями прямо на элементе (окно = UITK, не «дырка»).
            var battleZone = root.Q<VisualElement>("battle-zone");
            if (battleZone != null && battleStage != null && battleStage.Texture != null)
            {
                battleZone.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(battleStage.Texture));
                var hint = root.Q<Label>("battle-hint");
                if (hint != null) hint.style.display = DisplayStyle.None;
                AttachCameraInput(battleZone, battleStage);
            }
            SetBtn (root, "filter-relics",  L("ui.loadout.filter.relics", "Реликвии"));
            SetBtn (root, "filter-items",   L("ui.loadout.filter.items", "Предметы"));
            SetBtn (root, "filter-banners", L("ui.loadout.filter.banners", "Знамёна"));
            SetBtn (root, "sort", L("ui.loadout.sort.name", "Имя") + " ↓");
            SetText(root, "video-hint", L("ui.loadout.video", "видео-вставка 16:9"));
            SetText(root, "skills-label", L("ui.loadout.skills", "способности"));

            var search = root.Q<TextField>("search");
            if (search != null) SetPlaceholder(search, L("ui.loadout.search", "Поиск…"));

            // ── Способности (плейсхолдер-ряд) + улучшения (2 ряда × 3, плейсхолдеры) ──
            var abilities = root.Q<VisualElement>("detail-abilities");
            for (int i = 0; abilities != null && i < AbilitySlots; i++)
                abilities.Add(new Slot { Size = Slot.SlotSize.Md });

            FillUpgradeRow(root.Q<VisualElement>("upgrade-row-1"));
            FillUpgradeRow(root.Q<VisualElement>("upgrade-row-2"));

            // ── Грид таро-карточек ──
            var grid = root.Q<ScrollView>("relic-grid");
            if (grid == null) return root;
            // Вертикальный скролл; сам грид — ОТДЕЛЬНЫЙ контейнер внутри ScrollView, а не его
            // contentContainer: в режиме Vertical ScrollView инлайном форсит column/no-wrap на
            // contentContainer, и USS-перенос не применится. Свой контейнер (width:100% из USS)
            // корректно заворачивает ряд карточек.
            grid.mode = ScrollViewMode.Vertical;
            grid.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
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
                // Цикл: 2с idle → атака → 2с idle → атака. Attack проигрывается и возвращается в idle,
                // затем снова пауза перед следующим ударом.
                animLoop = root.schedule.Execute(() => rig.PlayAttack(activeRt)).Every(2000);
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

                // Клик → детали + запуск анимации ЭТОГО юнита (остальные замирают).
                cardRoot.RegisterCallback<ClickEvent>(_ => { ShowDetail(relic); Animate(relic); });
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

            return root;
        }

        // Управление камерой окна (Ф3b.2): ЛКМ-драг = пан «схватом мира», колесо = зум. События приходят
        // на сам элемент (RT-окно = обычный VisualElement) — panel.Pick-зонирование тут не нужно (оно для
        // драга релик→юнит в Ф3b.3). Дельту курсора отдаём долей вьюпорта — контроллер переводит в мир.
        private static void AttachCameraInput(VisualElement zone, IRosterStage stage)
        {
            bool dragging = false;
            Vector2 last = default;

            zone.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                last = (Vector2)e.position;
                zone.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            zone.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 size = zone.contentRect.size;
                if (size.x < 1f || size.y < 1f) return;
                Vector2 delta = (Vector2)e.position - last;
                last = (Vector2)e.position;
                stage.PanByFraction(new Vector2(delta.x / size.x, delta.y / size.y));
            });
            zone.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                if (zone.HasPointerCapture(e.pointerId)) zone.ReleasePointer(e.pointerId);
            });
            zone.RegisterCallback<WheelEvent>(e =>
            {
                stage.ZoomBy(-Mathf.Sign(e.delta.y)); // колесо вверх (delta.y<0) → приближение
                e.StopPropagation();
            });
        }

        private static void FillUpgradeRow(VisualElement row)
        {
            for (int i = 0; row != null && i < UpgradesPerRow; i++)
                row.Add(new Slot { Size = Slot.SlotSize.Lg });
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
