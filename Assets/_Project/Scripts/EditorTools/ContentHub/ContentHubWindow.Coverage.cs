using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Coverage: срезы «каких много/мало» по доменам и категориям юнитов.</summary>
    public sealed partial class ContentHubWindow
    {
        private void BuildCoverageSummary(VisualElement inner)
        {
            var byDomain = ContentIndex.Entries
                .GroupBy(e => e.Domain)
                .Select(g => (g.Key, g.Count()))
                .OrderByDescending(x => x.Item2).ToList();
            AddCountSection(inner, "Контент по доменам", byDomain);

            var units = ContentIndex.Entries.Where(e => e.IsUnit).ToList();
            if (units.Count > 0)
            {
                AddCountSection(inner, "Юниты по типу урона",
                    units.GroupBy(e => e.Unit.DamageType.ToString()).Select(g => (g.Key, g.Count()))
                         .OrderByDescending(x => x.Item2).ToList());
                AddCountSection(inner, "Юниты по типу атаки",
                    units.GroupBy(e => e.Unit.AttackType.ToString()).Select(g => (g.Key, g.Count()))
                         .OrderByDescending(x => x.Item2).ToList());
                AddCountSection(inner, "Юниты по ресурсу",
                    units.GroupBy(e => e.Unit.ResourceType.ToString()).Select(g => (g.Key, g.Count()))
                         .OrderByDescending(x => x.Item2).ToList());
            }
        }

        private void AddCountSection(VisualElement parent, string title, List<(string Label, int Count)> data)
        {
            parent.Add(new Label(title).WithClass("gh-sec-h"));
            int max = data.Count > 0 ? data.Max(d => d.Count) : 1;

            foreach (var (label, count) in data)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 22;

                var lbl = new Label(label);
                lbl.style.width = 170;
                lbl.style.fontSize = 12;
                row.Add(lbl);

                var barBg = new VisualElement();
                barBg.style.width = 220;
                barBg.style.height = 12;
                barBg.style.backgroundColor = new Color(0.11f, 0.10f, 0.09f, 1f);
                barBg.style.borderTopLeftRadius = 2; barBg.style.borderTopRightRadius = 2;
                barBg.style.borderBottomLeftRadius = 2; barBg.style.borderBottomRightRadius = 2;

                var fill = new VisualElement();
                fill.style.height = 12;
                fill.style.width = Mathf.Max(2f, 220f * count / max);
                fill.style.backgroundColor = new Color(0.96f, 0.65f, 0.14f, 0.85f);
                fill.style.borderTopLeftRadius = 2; fill.style.borderBottomLeftRadius = 2;
                barBg.Add(fill);
                row.Add(barBg);

                var cnt = new Label(count.ToString());
                cnt.style.marginLeft = 8;
                cnt.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(cnt);

                parent.Add(row);
            }
        }
    }
}
