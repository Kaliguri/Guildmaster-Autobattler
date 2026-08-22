using System;
using System.Collections.Generic;
using Guildmaster.Data.Stats;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Проводка панели осмотра: собирает субъект по месту в отряде и держит панель в контейнере,
    /// который дал экран.
    /// </summary>
    /// <remarks>
    /// <b>Панель одна на всю игру, а мест её появления много.</b> Поэтому презентер не знает, в каком
    /// экране он живёт: ему дают контейнер и говорят, кого показать. Экран боя и экран подготовки
    /// используют один и тот же код — иначе «инфа о юните» в двух местах разъехалась бы на первой же
    /// правке.
    /// <para><b>Статы берутся тем же швом, что в бою</b> (<see cref="IUnitStatPreview"/>), а не
    /// пересчитываются здесь: панель обязана показывать то же число, что увидит удар.</para>
    /// </remarks>
    public sealed class InspectPanelPresenter
    {
        private readonly IRunStateView _runStates;
        private readonly IContentDatabase _content;
        private readonly ILocalizationService _loc;
        private readonly IUnitStatPreview _stats;

        private VisualElement _host;
        private Action<int> _onAboutVessel;
        private Action<string> _onAboutRelic;
        private int _slotIndex = -1;

        public InspectPanelPresenter(IRunStateView runStates, IContentDatabase content,
                                     ILocalizationService loc, IUnitStatPreview stats = null)
        {
            _runStates = runStates;
            _content   = content;
            _loc       = loc;
            _stats     = stats;
        }

        /// <summary>Куда рисовать панель и что делать по её кнопкам.</summary>
        public void Mount(VisualElement host, Action<int> onAboutVessel = null, Action<string> onAboutRelic = null)
        {
            _host          = host ?? throw new ArgumentNullException(nameof(host));
            _onAboutVessel = onAboutVessel;
            _onAboutRelic  = onAboutRelic;
            Render();
        }

        /// <summary>Показать место отряда. Отрицательный индекс — «никто не выбран».</summary>
        public void Show(int slotIndex)
        {
            _slotIndex = slotIndex;
            Render();
        }

        /// <summary>Субъект по месту в отряде. Пустой — если места нет или оно свободно.</summary>
        public InspectSubject BuildSubject(int slotIndex)
        {
            RosterSlot[] guild = _runStates?.Current?.Guild;
            if (guild == null || slotIndex < 0 || slotIndex >= guild.Length) return default;

            RosterSlot slot = guild[slotIndex];
            if (slot == null) return default;

            string relicName = NameOfId(slot.RelicId);
            string name = !string.IsNullOrEmpty(slot.VesselId) ? NameOfId(slot.VesselId) : relicName;
            if (string.IsNullOrEmpty(name)) return default;

            string subtitle = slot.InBattle
                ? Localized("ui.inspect.in_battle", "в бою")
                : Localized("ui.inspect.in_reserve", "в запасе");
            if (!string.IsNullOrEmpty(relicName)) subtitle = relicName + " · " + subtitle;

            return new InspectSubject(
                name, subtitle,
                BuildStats(slot),
                BuildTraits(slot),
                slot.VesselItemIds,
                NameOfId(slot.AiPresetId),
                string.IsNullOrEmpty(slot.RelicId) || slot.RelicId == ContentIds.BaseRelic ? null : slot.RelicId);
        }

        private void Render()
        {
            if (_host == null) return;

            InspectSubject subject = BuildSubject(_slotIndex);
            string relicId = subject.RelicId;

            _host.Clear();
            _host.Add(InspectPanel.Build(
                subject,
                onAboutVessel: () => _onAboutVessel?.Invoke(_slotIndex),
                onAboutRelic: () => _onAboutRelic?.Invoke(relicId),
                localize: key => _loc?.GetString(key)));
        }

        /// <summary>
        /// Статы итогом. Без превью-сервиса список пуст — панель просто не покажет секцию, а не
        /// нарисует нули: ноль в панели читается как «боец слабый», хотя это «мы не посчитали».
        /// </summary>
        private IReadOnlyList<(string, string)> BuildStats(RosterSlot slot)
        {
            if (_stats == null || _content == null) return Array.Empty<(string, string)>();
            if (string.IsNullOrEmpty(slot.RelicId) || !_content.TryGet(slot.RelicId, out RelicData relic))
                return Array.Empty<(string, string)>();

            IReadOnlyList<UnitStatLine> lines = _stats.Basic(relic);
            if (lines == null || lines.Count == 0) return Array.Empty<(string, string)>();

            var rows = new List<(string, string)>(lines.Count);
            foreach (UnitStatLine line in lines)
            {
                // Подпись — по ключу, с готовым запасным словом от самого шва: своё придумывать
                // нельзя, иначе панель и остальной интерфейс назовут один стат по-разному.
                string label = _loc?.GetString(line.LabelKey);
                rows.Add((string.IsNullOrEmpty(label) ? line.LabelFallback : label, line.Value));
            }
            return rows;
        }

        private IReadOnlyList<(string, string, bool)> BuildTraits(RosterSlot slot)
        {
            // Перки живут на человеке дома, а не на слоте забега: пока людей нет, показывать нечего.
            // Секция просто не появится — это честнее, чем пустые чипы.
            return Array.Empty<(string, string, bool)>();
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
