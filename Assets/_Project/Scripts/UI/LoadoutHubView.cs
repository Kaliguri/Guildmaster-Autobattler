using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка хаба лоадаута (Фаза 2) из UXML-шаблона — обзор команды (4 сосуда) + общая секция
    /// (баннеры party-скоупа, запас реликвий, золото). Общий код для живого роутера и превью-стенда.
    /// Клик по сосуду уводит в per-unit loadout (существующий экран) через <paramref name="onVesselClick"/>.
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

        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<RosterEntry> roster,
            IReadOnlyList<ItemData> banners,
            IReadOnlyList<RelicData> stash,
            int gold,
            Func<string, string> nameOf,
            Func<string, string> localize,
            Action<int> onVesselClick,
            Action onClose,
            Action<int> onStashClick = null,
            int selectedStashIndex = -1)
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
            SetText(root, "hub-hint",            L("ui.hub.hint", "Реликвию из запаса → на сосуд. Клик по сосуду без выбора снимает релик."));
            SetText(root, "hub-gold",            L("ui.hub.gold", "Золото") + ": " + gold);

            var close = root.Q<Button>("btn-close");
            if (close != null) { close.text = L("ui.hub.close", "Закрыть"); close.clicked += () => onClose?.Invoke(); }

            // ── Команда: карточки сосудов (компонент VesselCard) ──
            var rosterBox = root.Q<VisualElement>("hub-roster");
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
                card.Clicked += () => onVesselClick?.Invoke(idx);
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

            // ── Запас реликвий — слоты (без flex-grow: не распираем панель); клик «взводит» реликвию ──
            var stashBox = root.Q<VisualElement>("hub-stash");
            for (int i = 0; stash != null && stashBox != null && i < stash.Count; i++)
            {
                int idx = i;
                var slot = new Slot { Size = Slot.SlotSize.Sm };
                slot.SetIcon(RelicSprite(stash[i]));
                slot.SetSelected(i == selectedStashIndex);
                if (onStashClick != null)
                {
                    slot.pickingMode = PickingMode.Position;
                    slot.RegisterCallback<ClickEvent>(_ => onStashClick(idx));
                }
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
