using System;
using System.Collections.Generic;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Проводка страницы «Предметы»: показывает, что на ком надето, и переводит перетаскивание в
    /// команды. Тот же приём, что у страницы «Отряд» — вью не умеет читать <c>RunState</c>.
    /// </summary>
    /// <remarks>
    /// <b>Закрытый слот и пустой слот — разные вещи, и экран обязан их различать.</b> Пустой ждёт
    /// вещь, закрытый ждёт награду забега. В записи строки первое это пустая строка, второе —
    /// <c>null</c>: одно значение на оба состояния заставило бы экран гадать.
    /// </remarks>
    public sealed class ItemsScreenPresenter
    {
        private readonly IRunStateView _runStates;
        private readonly IRunCommands _commands;
        private readonly ILocalizationService _loc;
        private readonly GameConfig _config;

        private VisualTreeAsset _uxml;
        private VisualElement _host;
        private Action<int> _onInspect;
        private Action<int> _onOpenCard;
        private Action _onBattle;

        public ItemsScreenPresenter(IRunStateView runStates, IRunCommands commands,
                                    ILocalizationService loc, GameConfig config)
        {
            _runStates = runStates;
            _commands  = commands;
            _loc       = loc;
            _config    = config;
        }

        public void Mount(VisualElement host, VisualTreeAsset uxml,
                          Action<int> onInspect = null, Action<int> onOpenCard = null, Action onBattle = null)
        {
            _host       = host ?? throw new ArgumentNullException(nameof(host));
            _uxml       = uxml ?? throw new ArgumentNullException(nameof(uxml));
            _onInspect  = onInspect;
            _onOpenCard = onOpenCard;
            _onBattle   = onBattle;
            Refresh();
        }

        /// <summary>Перечитать снаряжение и пересобрать экран.</summary>
        public void Refresh()
        {
            if (_host == null || _uxml == null) return;

            _host.Clear();
            _host.Add(ItemsScreenView.Build(
                _uxml,
                BuildRows(),
                BuildStash(),
                new ItemsScreenView.Actions
                {
                    Equip    = (slot, itemSlot, id) => Run(() => _commands?.SetSlotItem(slot, itemSlot, id)),
                    Unequip  = (slot, itemSlot)     => Run(() => _commands?.SetSlotItem(slot, itemSlot, string.Empty)),
                    Inspect  = index => _onInspect?.Invoke(index),
                    OpenCard = index => _onOpenCard?.Invoke(index),
                    Battle   = () => _onBattle?.Invoke(),
                },
                key => _loc?.GetString(key)));
        }

        /// <summary>
        /// Строки страницы: только занятые места отряда. Свободные и закрытые сюда не попадают —
        /// вешать вещь не на кого, и пустая строка была бы приглашением к жесту, который не сработает.
        /// </summary>
        public IReadOnlyList<ItemsRowView> BuildRows()
        {
            RunState run = _runStates?.Current;
            RosterSlot[] guild = run?.Guild;
            if (guild == null || guild.Length == 0) return Array.Empty<ItemsRowView>();

            int openPlaces = run.OpenSlots > 0 && run.OpenSlots <= guild.Length ? run.OpenSlots : guild.Length;
            int openItemSlots = OpenItemSlots(run);
            int maxItemSlots = _config != null && _config.VesselItemSlotsMax > 0
                ? _config.VesselItemSlotsMax
                : openItemSlots;

            var rows = new List<ItemsRowView>(guild.Length);
            for (int i = 0; i < guild.Length && i < openPlaces; i++)
            {
                RosterSlot slot = guild[i];
                if (slot == null) continue;

                string name = NameOf(slot);
                if (string.IsNullOrEmpty(name)) continue; // место свободно — надевать не на кого

                var items = new string[maxItemSlots];
                for (int k = 0; k < maxItemSlots; k++)
                {
                    if (k >= openItemSlots) { items[k] = null; continue; } // закрыт до награды
                    string worn = slot.VesselItemIds != null && k < slot.VesselItemIds.Length
                        ? slot.VesselItemIds[k]
                        : string.Empty;
                    items[k] = worn ?? string.Empty;
                }

                rows.Add(new ItemsRowView(i, name, RelicNameOf(slot), items));
            }
            return rows;
        }

        /// <summary>Вещи в запасе забега.</summary>
        public IReadOnlyList<string> BuildStash()
        {
            string[] stash = _runStates?.Current?.ItemInventory;
            return stash ?? Array.Empty<string>();
        }

        private int OpenItemSlots(RunState run)
        {
            int open = run.OpenItemSlots;
            if (open <= 0) open = _config != null && _config.VesselItemSlots > 0 ? _config.VesselItemSlots : 3;
            int max = _config != null && _config.VesselItemSlotsMax > 0 ? _config.VesselItemSlotsMax : open;
            return open > max ? max : open;
        }

        private string NameOf(RosterSlot slot)
        {
            if (!string.IsNullOrEmpty(slot.VesselId))
            {
                string name = _loc?.GetString(slot.VesselId + ".name");
                return string.IsNullOrEmpty(name) ? slot.VesselId : name;
            }
            return RelicNameOf(slot);
        }

        private string RelicNameOf(RosterSlot slot)
        {
            string id = slot?.RelicId;
            if (string.IsNullOrEmpty(id) || id == ContentIds.BaseRelic) return null;
            string name = _loc?.GetString(id + ".name");
            return string.IsNullOrEmpty(name) ? id : name;
        }

        private void Run(Action command)
        {
            command?.Invoke();
            Refresh();
        }
    }
}
