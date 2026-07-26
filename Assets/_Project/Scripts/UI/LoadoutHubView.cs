using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.UI.Components;
using Guildmaster.UI.Tooltips;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка хаба лоадаута (Фаза 2) из UXML-шаблона — обзор команды (4 сосуда) + общая секция
    /// (баннеры party-скоупа, запас реликвий, золото). Общий код для живого роутера и превью-стенда.
    /// <para>
    /// Реликвии переносятся ДРАГОМ через переиспользуемый примитив <see cref="UiDragDrop"/> (не самопис): слоты
    /// запаса — источники (payload <see cref="StashRelic"/>), карточки сосудов — цели надевания (принимают
    /// StashRelic) и источники снятия (payload <see cref="VesselRelic"/>, если надет не базовый кит), бокс
    /// запаса — цель снятия. Тот же примитив переиспользуют будущие вкладки предметов/баннеров.
    /// </para>
    /// Разметка/стиль — только из <c>LoadoutHubScreen.uxml</c> + классы дизайн-системы.
    /// </summary>
    public static class LoadoutHubView
    {
        /// <summary>Слот команды для хаба: сосуд + надетый на него релик (весь кит).</summary>
        public readonly struct RosterEntry
        {
            public readonly VesselData Vessel;
            public readonly RelicData  Relic;
            public RosterEntry(VesselData vessel, RelicData relic) { Vessel = vessel; Relic = relic; }
        }

        // Payload'ы драга (доменные, для UiDragDrop): откуда тянут реликвию.
        private sealed class StashRelic  { public readonly int Index; public StashRelic(int i)  => Index = i; }
        private sealed class VesselRelic { public readonly int Index; public VesselRelic(int i) => Index = i; }


        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<RosterEntry> roster,
            IReadOnlyList<ItemData> banners,
            IReadOnlyList<RelicData> stash,
            int gold,
            Func<string, string> nameOf,
            Func<string, string> localize,
            Action onClose,
            Action<int, int> onEquip = null,   // (vesselIndex, stashIndex): надеть релик из запаса на сосуд
            Action<int> onUnequip = null)       // (vesselIndex): снять релик с сосуда обратно в запас
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }
            string Name(string id) => nameOf != null ? nameOf(id) : id;

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            SetText(root, "hub-title",          L("ui.hub.title", "Гильдия"));
            SetText(root, "hub-team-header",     L("ui.hub.team", "Команда"));
            SetText(root, "hub-banners-header",  L("ui.hub.banners", "Баннеры"));
            SetText(root, "hub-stash-header",    L("ui.hub.stash", "Запас реликвий"));
            SetText(root, "hub-hint",            L("ui.hub.hint", "Перетащи реликвию из запаса на сосуд — наденешь; с сосуда в запас — снимешь."));
            SetText(root, "hub-gold",            L("ui.hub.gold", "Золото") + ": " + gold);

            var close = root.Q<Button>("btn-close");
            if (close != null) { close.text = L("ui.hub.close", "Закрыть"); close.clicked += () => onClose?.Invoke(); }

            var rosterBox = root.Q<VisualElement>("hub-roster");
            var stashBox  = root.Q<VisualElement>("hub-stash");

            var drag = new UiDragDrop(root);

            // ── Команда: карточки сосудов — цель надевания (принимает StashRelic) + источник снятия (VesselRelic) ──
            for (int i = 0; roster != null && rosterBox != null && i < roster.Count; i++)
            {
                RosterEntry e = roster[i];
                int idx = i;

                // Имя носителя; при отсутствии VesselData (скелет Фазы 2/4) — имя надетого релика как плейсхолдер.
                string vesselName = e.Vessel != null ? Name(e.Vessel.Id)
                                  : (e.Relic != null ? Name(e.Relic.Id) : "—");
                var card = new VesselCard
                {
                    VesselName = vesselName,
                    RelicName  = e.Relic != null ? Name(e.Relic.Id) : "—",
                };
                card.SetRelicIcon(RelicSprite(e.Relic));
                // Карточка носителя показывает пару «сосуд + реликвия»; подсказка рассказывает про того,
                // кто в этой паре несёт данные — сосуд, если он есть, иначе саму реликвию.
                card.WithTooltip(e.Vessel != null
                    ? TooltipRequest.Vessel(e.Vessel.Id)
                    : TooltipRequest.Relic(e.Relic?.Id));

                drag.AddTarget(card, p => p is StashRelic, p => onEquip?.Invoke(idx, ((StashRelic)p).Index));

                bool hasRealRelic = e.Relic != null && e.Relic.Id != Data.Definitions.ContentIds.BaseRelic;
                if (hasRealRelic)
                    drag.AddSource(card, new VesselRelic(idx), RelicSprite(e.Relic));

                rosterBox.Add(card);
            }

            // ── Баннеры (party-скоуп) — слоты ──
            var bannersBox = root.Q<VisualElement>("hub-banners");
            for (int i = 0; banners != null && bannersBox != null && i < banners.Count; i++)
            {
                var slot = new Slot { Size = Slot.SlotSize.Sm };
                slot.SetIcon(banners[i] != null ? banners[i].Icon : null);
                bannersBox.Add(slot);
            }

            // ── Запас реликвий: бокс — цель снятия (принимает VesselRelic); слоты — источники надевания ──
            if (stashBox != null)
                drag.AddTarget(stashBox, p => p is VesselRelic, p => onUnequip?.Invoke(((VesselRelic)p).Index));

            for (int i = 0; stash != null && stashBox != null && i < stash.Count; i++)
            {
                var slot = new Slot { Size = Slot.SlotSize.Sm };
                Sprite icon = RelicSprite(stash[i]);
                slot.SetIcon(icon);
                slot.WithTooltip(TooltipRequest.Relic(stash[i]?.Id)); // в запасе видна только иконка — что это, скажет тултип
                drag.AddSource(slot, new StashRelic(i), icon);
                stashBox.Add(slot);
            }

            return root;
        }

        // Спрайт релика для слота/карточки: портрет из UnitVisual, иначе иконка-фолбэк.
        private static Sprite RelicSprite(RelicData relic)
        {
            if (relic == null) return null;
            return relic.Visual != null && relic.Visual.Portrait != null ? relic.Visual.Portrait : relic.Icon;
        }

        private static void SetText(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }
    }
}
