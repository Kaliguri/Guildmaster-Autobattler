using System;
using System.Collections.Generic;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Guild;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Проводка расширенной карточки «Сосуда»: собирает содержимое обоих табов и держит открытый таб.
    /// </summary>
    /// <remarks>
    /// <b>Травмы читаются каскадом, а не пересчитываются здесь</b> (<see cref="InjuryLedger"/>):
    /// сколько мест занято, знает он один, и второй счётчик рядом разошёлся бы с ним на первом же
    /// снятии раны.
    /// <para><b>Человек берётся из дома</b> (<see cref="IGuildRosterView"/>), а не из забега: забег
    /// держит только ссылку. Пока людей нет, карточка показывает то, чем место является — Реликвию.</para>
    /// </remarks>
    public sealed class VesselCardPresenter
    {
        private readonly IRunStateView _runStates;
        private readonly IGuildRosterView _guild;
        private readonly IContentDatabase _content;
        private readonly ILocalizationService _loc;
        private readonly IUnitStatPreview _stats;

        private VisualElement _host;
        private int _slotIndex = -1;
        private int _tab;

        public VesselCardPresenter(IRunStateView runStates, IGuildRosterView guild, IContentDatabase content,
                                   ILocalizationService loc, IUnitStatPreview stats = null)
        {
            _runStates = runStates;
            _guild     = guild;
            _content   = content;
            _loc       = loc;
            _stats     = stats;
        }

        /// <summary>Открыть карточку места отряда в переданном слое поверх экрана.</summary>
        public void Open(VisualElement host, int slotIndex)
        {
            _host      = host ?? throw new ArgumentNullException(nameof(host));
            _slotIndex = slotIndex;
            _tab       = 0;
            Render();
        }

        /// <summary>Закрыть карточку: слой пустеет, экран под ней остаётся тем же.</summary>
        public void Close()
        {
            _host?.Clear();
            _slotIndex = -1;
        }

        /// <summary>Содержимое карточки для места отряда. Пустой субъект — показывать некого.</summary>
        public VesselCardSubject BuildSubject(int slotIndex)
        {
            RosterSlot[] guild = _runStates?.Current?.Guild;
            if (guild == null || slotIndex < 0 || slotIndex >= guild.Length) return default;

            RosterSlot slot = guild[slotIndex];
            if (slot == null) return default;

            VesselState person = FindPerson(slot.VesselId);
            string relicName = NameOfId(slot.RelicId);
            string name = person != null && !string.IsNullOrEmpty(person.Name) ? person.Name : NameOfId(slot.VesselId) ?? relicName;
            if (string.IsNullOrEmpty(name)) return default;

            InjurySlots injuries = _content != null ? InjuryLedger.SlotsOf(slot, _content) : default;

            return new VesselCardSubject(
                name,
                BuildSubtitle(slot, relicName),
                BuildStats(slot),
                BuildTraits(person),
                slot.VesselItemIds,
                (injuries.Bruises, injuries.Wounds, injuries.Maimings),
                BuildInjuryNames(slot),
                mettle: null, // Закалка ещё не выдаётся никем: пустая строка честнее выдуманной ступени
                BuildLore(person),
                BuildStatistics(person),
                string.IsNullOrEmpty(slot.RelicId) || slot.RelicId == ContentIds.BaseRelic ? null : slot.RelicId);
        }

        private void Render()
        {
            if (_host == null) return;

            _host.Clear();
            _host.Add(VesselCardView.Build(
                BuildSubject(_slotIndex),
                _tab,
                onTab: index => { _tab = index; Render(); },
                onClose: Close,
                onRelic: null, // карточка Реликвии — следующая фаза
                localize: key => _loc?.GetString(key)));
        }

        private string BuildSubtitle(RosterSlot slot, string relicName)
        {
            string where = slot.InBattle
                ? Localized("ui.vcard.in_battle", "в бою")
                : Localized("ui.vcard.in_reserve", "в запасе");
            return string.IsNullOrEmpty(relicName) ? where : relicName + " · " + where;
        }

        private VesselState FindPerson(string vesselId)
        {
            if (string.IsNullOrEmpty(vesselId)) return null;
            IReadOnlyList<VesselState> people = _guild?.Roster;
            if (people == null) return null;
            for (int i = 0; i < people.Count; i++)
                if (people[i] != null && people[i].Id == vesselId) return people[i];
            return null;
        }

        private IReadOnlyList<(string, string)> BuildStats(RosterSlot slot)
        {
            if (_stats == null || _content == null) return Array.Empty<(string, string)>();
            if (string.IsNullOrEmpty(slot.RelicId) || !_content.TryGet(slot.RelicId, out RelicData relic))
                return Array.Empty<(string, string)>();

            IReadOnlyList<UnitStatLine> lines = _stats.Basic(relic);
            if (lines == null) return Array.Empty<(string, string)>();

            var rows = new List<(string, string)>(lines.Count);
            foreach (UnitStatLine line in lines)
            {
                string label = _loc?.GetString(line.LabelKey);
                rows.Add((string.IsNullOrEmpty(label) ? line.LabelFallback : label, line.Value));
            }
            return rows;
        }

        private IReadOnlyList<(string, string, bool)> BuildTraits(VesselState person)
        {
            if (person == null) return Array.Empty<(string, string, bool)>();

            var traits = new List<(string, string, bool)>(2);
            if (!string.IsNullOrEmpty(person.PositiveTraitId))
                traits.Add((person.PositiveTraitId, NameOfId(person.PositiveTraitId), true));
            if (!string.IsNullOrEmpty(person.NegativeTraitId))
                traits.Add((person.NegativeTraitId, NameOfId(person.NegativeTraitId), false));
            return traits;
        }

        private IReadOnlyList<string> BuildInjuryNames(RosterSlot slot)
        {
            Injury[] injuries = slot.Injuries;
            if (injuries == null || injuries.Length == 0) return Array.Empty<string>();

            var names = new List<string>(injuries.Length);
            foreach (Injury injury in injuries)
            {
                if (injury == null || string.IsNullOrEmpty(injury.Id)) continue;
                names.Add(NameOfId(injury.Id) ?? injury.Id);
            }
            return names;
        }

        /// <summary>
        /// Досье. Пока не написан генератор из сида рождения, показывать нечего — и это видно словами,
        /// а не выдуманной биографией.
        /// </summary>
        private IReadOnlyList<string> BuildLore(VesselState person) => Array.Empty<string>();

        /// <summary>
        /// Статистика. Счётчиков на человеке ещё нет (решение есть, реализации нет), поэтому пока
        /// показывается только то, что дом действительно знает.
        /// </summary>
        private IReadOnlyList<(string, string)> BuildStatistics(VesselState person)
        {
            if (person == null) return Array.Empty<(string, string)>();
            return new[]
            {
                (Localized("ui.vcard.runs", "походов пережито"), person.CompletedRuns.ToString()),
            };
        }

        private string NameOfId(string id)
        {
            if (string.IsNullOrEmpty(id) || id == ContentIds.BaseRelic) return null;
            string name = _loc?.GetString(id + ".name");
            return string.IsNullOrEmpty(name) ? id : name;
        }

        private string Localized(string key, string fallback)
        {
            string v = _loc?.GetString(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }
}
