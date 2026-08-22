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
    /// Проводка страницы «Отряд»: читает состав из забега, отдаёт его вью плоскими записями и
    /// переводит жесты игрока в команды.
    /// <para>Между вью и состоянием стоит намеренно: <see cref="PartyScreenView"/> не должен уметь
    /// читать <c>RunState</c> — иначе посмотреть экран можно было бы только в живом забеге, а тесты
    /// потребовали бы половины игры.</para>
    /// </summary>
    /// <remarks>
    /// <b>Пересборка целиком, а не точечное обновление.</b> Состав меняется редко и всегда по жесту
    /// игрока, зато способов измениться у него много (в бой, в запас, местами, награда открыла место).
    /// Точечное обновление пришлось бы писать на каждый и рассинхронилось бы на первом же пропущенном.
    /// <para><b>Читает после команды, а не предсказывает.</b> Команда может не пройти — пятый боец в
    /// четырёхместный бой не влезет, — и экран, нарисовавший результат заранее, соврал бы.</para>
    /// </remarks>
    public sealed class PartyScreenPresenter
    {
        private readonly IRunStateView _runStates;
        private readonly IRunCommands _commands;
        private readonly IContentDatabase _content;
        private readonly ILocalizationService _loc;
        private readonly GameConfig _config;

        private VisualTreeAsset _uxml;
        private VisualElement _host;
        private Action<int> _onInspect;
        private Action<int> _onOpenCard;
        private Action _onBattle;

        public PartyScreenPresenter(IRunStateView runStates, IRunCommands commands,
                                    IContentDatabase content, ILocalizationService loc, GameConfig config)
        {
            _runStates = runStates;
            _commands  = commands;
            _content   = content;
            _loc       = loc;
            _config    = config;
        }

        /// <summary>
        /// Собрать экран в переданный контейнер и держать его в актуальном виде.
        /// </summary>
        /// <param name="onInspect">ЛКМ по месту — открыть панель осмотра. Пока не подключено, см. фазу 4.</param>
        /// <param name="onOpenCard">ПКМ по месту — расширенная карточка. Фаза 5.</param>
        /// <param name="onBattle">Выход в бой.</param>
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

        /// <summary>Перечитать состав и пересобрать экран.</summary>
        public void Refresh()
        {
            if (_host == null || _uxml == null) return;

            _host.Clear();
            _host.Add(PartyScreenView.Build(
                _uxml,
                BuildSlots(),
                key => _loc?.GetString(key),
                _config != null && _config.BattleSlots > 0 ? _config.BattleSlots : 4,
                actions: new PartyScreenView.Actions
                {
                    SetInBattle = (index, on) => Run(() => _commands?.SetSlotInBattle(index, on)),
                    Swap        = (from, to)  => Run(() => _commands?.SwapSlots(from, to)),
                    Inspect     = index => _onInspect?.Invoke(index),
                    OpenCard    = index => _onOpenCard?.Invoke(index),
                    Battle      = () => _onBattle?.Invoke(),
                }));
        }

        /// <summary>Состав забега глазами экрана. Пустой забег даёт пустой список — это не отказ, а факт.</summary>
        public IReadOnlyList<PartySlotView> BuildSlots()
        {
            RunState run = _runStates?.Current;
            RosterSlot[] guild = run?.Guild;
            if (guild == null || guild.Length == 0) return Array.Empty<PartySlotView>();

            int open = run.OpenSlots > 0 && run.OpenSlots <= guild.Length ? run.OpenSlots : guild.Length;

            var slots = new List<PartySlotView>(guild.Length);
            for (int i = 0; i < guild.Length; i++)
            {
                RosterSlot slot = guild[i];
                slots.Add(new PartySlotView(
                    index: i,
                    name: NameOf(slot),
                    relic: RelicNameOf(slot),
                    inBattle: slot != null && slot.InBattle,
                    open: i < open));
            }
            return slots;
        }

        /// <summary>
        /// Кто занимает место. Людей в забеге может не быть вовсе — <c>VesselId</c> пуст, пока не
        /// заведён наём, — и тогда место представляет сама Реликвия: экран честно показывает то, чем
        /// отряд СЕЙЧАС является, а не заглушку с выдуманным именем.
        /// </summary>
        private string NameOf(RosterSlot slot)
        {
            if (slot == null) return null;
            if (!string.IsNullOrEmpty(slot.VesselId))
            {
                string name = _loc?.GetString(slot.VesselId + ".name");
                return string.IsNullOrEmpty(name) ? slot.VesselId : name;
            }
            // Место без человека, но с китом — занято: в бой выходит именно этот слот.
            return string.IsNullOrEmpty(slot.RelicId) || slot.RelicId == ContentIds.BaseRelic
                ? null
                : RelicNameOf(slot);
        }

        private string RelicNameOf(RosterSlot slot)
        {
            string id = slot?.RelicId;
            if (string.IsNullOrEmpty(id) || id == ContentIds.BaseRelic) return null;

            string name = _loc?.GetString(id + ".name");
            if (!string.IsNullOrEmpty(name)) return name;
            return _content != null && _content.TryGet(id, out RelicData relic) && relic != null ? relic.Id : id;
        }

        /// <summary>Выполнить команду и перечитать состав: экран показывает исход, а не намерение.</summary>
        private void Run(Action command)
        {
            command?.Invoke();
            Refresh();
        }
    }
}
