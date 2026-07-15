using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Построение списка боёв для дев-пикера (F2) — общий код для живой панели
    /// (<see cref="DevEncounterPanel"/>) и превью-стенда (<c>UiPreviewCatalog</c>).
    /// Разметка — только через USS-классы <c>DevBattlePicker.uss</c>, никакого инлайн-стиля.
    /// </summary>
    public static class DevBattlePickerView
    {
        /// <summary>
        /// Наполнить контейнер списка секциями «Presets»/«Encounters». <paramref name="rows"/> (если задан)
        /// заполняется парами (ключ, ряд) для подсветки активного: ключи <c>"p:&lt;id&gt;"</c> / <c>"e:&lt;id&gt;"</c>.
        /// </summary>
        public static void Populate(
            VisualElement list,
            IContentDatabase content,
            Action<BattlePresetData> onPreset,
            Action<EncounterData> onEncounter,
            List<(string key, VisualElement row)> rows = null)
        {
            list.Clear();
            rows?.Clear();

            IReadOnlyList<EncounterData> encounters = content?.All<EncounterData>();
            IReadOnlyList<BattlePresetData> presets = content?.All<BattlePresetData>();

            bool any = (encounters != null && encounters.Count > 0) || (presets != null && presets.Count > 0);
            if (!any)
            {
                var empty = new Label("Нет боёв.\nСоздай ассеты + Sync Content Database.");
                empty.AddToClassList("gm-dev-empty");
                list.Add(empty);
                return;
            }

            if (presets != null && presets.Count > 0)
            {
                AddSectionHeader(list, "Presets");
                for (int i = 0; i < presets.Count; i++)
                {
                    BattlePresetData p = presets[i];
                    AddRow(list, rows, "p:" + p.Id, $"{Short(p.Id)}  ·  {RosterSize(p)}p", () => onPreset?.Invoke(p));
                }
            }

            if (encounters != null && encounters.Count > 0)
            {
                AddSectionHeader(list, "Encounters");
                for (int i = 0; i < encounters.Count; i++)
                {
                    EncounterData e = encounters[i];
                    AddRow(list, rows, "e:" + e.Id, $"{Short(e.Id)}  ·  {e.Tier}", () => onEncounter?.Invoke(e));
                }
            }
        }

        private static void AddSectionHeader(VisualElement list, string text)
        {
            var h = new Label(text);
            h.AddToClassList("gm-dev-section");
            list.Add(h);
        }

        private static void AddRow(VisualElement list, List<(string, VisualElement)> rows,
                                   string key, string label, Action onPlay)
        {
            var row = new VisualElement();
            row.AddToClassList("gm-dev-row");

            var lbl = new Label(label);
            lbl.AddToClassList("gm-dev-row__label");
            row.Add(lbl);

            var play = new Button(onPlay) { text = "Play" };
            play.AddToClassList("gm-dev-play");
            row.Add(play);

            list.Add(row);
            rows?.Add((key, row));
        }

        private static int RosterSize(BattlePresetData p) => p.Roster != null ? p.Roster.Count : 0;

        private static string Short(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int dot = id.IndexOf('.');
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }
    }
}
