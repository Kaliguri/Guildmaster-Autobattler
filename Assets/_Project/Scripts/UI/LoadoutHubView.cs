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
    /// <para>
    /// Реликвии переносятся ДРАГОМ (UITK pointer-capture, не Input System — чтобы не драться с драгом юнитов
    /// в расстановке): тащишь релик из запаса на карточку сосуда → надевается (свап прежнего в запас); тащишь
    /// надетый релик с сосуда в бокс запаса → снимается. Drop решается по <c>worldBound</c> карточек/бокса.
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

        private const string BaseRelicId = "relic.base";

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

            var drag = new RelicDrag(root, stashBox, onEquip, onUnequip);

            // ── Команда: карточки сосудов (drop-таргет надевания; drag-источник снятия, если надет не базовый) ──
            for (int i = 0; roster != null && rosterBox != null && i < roster.Count; i++)
            {
                RosterEntry e = roster[i];

                // Имя носителя; при отсутствии VesselData (скелет Фазы 2/4) — имя надетого релика как плейсхолдер.
                string vesselName = e.Vessel != null ? Name(e.Vessel.Id)
                                  : (e.Relic != null ? Name(e.Relic.Id) : "—");
                var card = new VesselCard
                {
                    VesselName = vesselName,
                    RelicName  = e.Relic != null ? Name(e.Relic.Id) : "—",
                };
                card.SetRelicIcon(RelicSprite(e.Relic));

                bool hasRealRelic = e.Relic != null && e.Relic.Id != BaseRelicId;
                drag.RegisterVessel(card, i, hasRealRelic ? RelicSprite(e.Relic) : null);
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

            // ── Запас реликвий — слоты (drag-источник надевания) ──
            for (int i = 0; stash != null && stashBox != null && i < stash.Count; i++)
            {
                var slot = new Slot { Size = Slot.SlotSize.Sm };
                Sprite icon = RelicSprite(stash[i]);
                slot.SetIcon(icon);
                drag.RegisterStash(slot, i, icon);
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

        /// <summary>
        /// Драг реликов в хабе на UITK-событиях с pointer-capture (НЕ через Input System — чтобы не конфликтовать
        /// с драгом юнитов в расстановке). Источник — слот запаса (надевание) или карточка сосуда (снятие);
        /// ghost едет за курсором, дроп-таргет определяется по <c>worldBound</c>. Один инстанс на сборку хаба.
        /// </summary>
        private sealed class RelicDrag
        {
            private const float GhostSize = 48f;

            private readonly VisualElement _root;
            private readonly VisualElement _stashBox;
            private readonly Action<int, int> _onEquip;
            private readonly Action<int> _onUnequip;
            private readonly List<(VisualElement card, int index)> _vessels = new();

            private VisualElement _ghost;
            private bool _dragging;
            private int _fromStash  = -1; // источник — слот запаса (надевание на сосуд)
            private int _fromVessel = -1; // источник — сосуд (снятие в запас)

            public RelicDrag(VisualElement root, VisualElement stashBox, Action<int, int> onEquip, Action<int> onUnequip)
            {
                _root = root; _stashBox = stashBox; _onEquip = onEquip; _onUnequip = onUnequip;
            }

            /// <summary>Карточка сосуда: всегда drop-таргет; drag-источник снятия — только если надет не базовый кит.</summary>
            public void RegisterVessel(VisualElement card, int index, Sprite dragIcon)
            {
                _vessels.Add((card, index));
                if (dragIcon == null) return;
                card.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || _dragging) return;
                    _fromVessel = index; _fromStash = -1;
                    Begin(card, evt.pointerId, evt.position, dragIcon);
                    evt.StopPropagation();
                });
            }

            /// <summary>Слот запаса: drag-источник надевания на сосуд.</summary>
            public void RegisterStash(VisualElement slot, int index, Sprite dragIcon)
            {
                if (dragIcon == null) return;
                slot.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || _dragging) return;
                    _fromStash = index; _fromVessel = -1;
                    Begin(slot, evt.pointerId, evt.position, dragIcon);
                    evt.StopPropagation();
                });
            }

            private void Begin(VisualElement source, int pointerId, Vector2 pos, Sprite icon)
            {
                _dragging = true;

                _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
                _ghost.AddToClassList("gm-drag-ghost");
                if (icon != null) _ghost.style.backgroundImage = new StyleBackground(icon);
                _root.Add(_ghost);
                MoveGhost(pos);

                source.CapturePointer(pointerId);

                EventCallback<PointerMoveEvent> onMove = null;
                EventCallback<PointerUpEvent>   onUp   = null;
                onMove = me => { if (_dragging) MoveGhost(me.position); };
                onUp = ue =>
                {
                    if (_dragging) Drop(ue.position);
                    if (source.HasPointerCapture(ue.pointerId)) source.ReleasePointer(ue.pointerId);
                    source.UnregisterCallback(onMove);
                    source.UnregisterCallback(onUp);
                };
                source.RegisterCallback(onMove);
                source.RegisterCallback(onUp);
            }

            private void Drop(Vector2 pos)
            {
                _dragging = false;
                if (_ghost != null) { _ghost.RemoveFromHierarchy(); _ghost = null; }

                if (_fromStash >= 0)
                {
                    int vessel = VesselAt(pos);
                    if (vessel >= 0) _onEquip?.Invoke(vessel, _fromStash);
                }
                else if (_fromVessel >= 0)
                {
                    if (_stashBox != null && _stashBox.worldBound.Contains(pos)) _onUnequip?.Invoke(_fromVessel);
                }
                _fromStash = -1; _fromVessel = -1;
            }

            private int VesselAt(Vector2 pos)
            {
                foreach (var (card, index) in _vessels)
                    if (card.worldBound.Contains(pos)) return index;
                return -1;
            }

            private void MoveGhost(Vector2 panelPos)
            {
                if (_ghost == null) return;
                Vector2 local = _root.WorldToLocal(panelPos);
                _ghost.style.left = local.x - GhostSize * 0.5f;
                _ghost.style.top  = local.y - GhostSize * 0.5f;
            }
        }
    }
}
