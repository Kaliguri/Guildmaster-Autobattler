using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Configs: конфиг-синглтоны и дизайн-SO через InspectorElement + подсветка отличий от код-дефолтов.</summary>
    public sealed partial class ContentHubWindow
    {
        private IEnumerable<ContentEntry> ConfigEntries() =>
            ContentIndex.Entries
                .Where(e => IsConfigLike(e.Domain))
                .OrderBy(e => e.Domain).ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase);

        private ContentEntry ResolveConfigEntry()
        {
            var entry = ResolveSelected();
            if (entry != null && IsConfigLike(entry.Domain)) return entry;
            return ConfigEntries().FirstOrDefault();
        }

        private void BuildConfigs(VisualElement container)
        {
            var split = new TwoPaneSplitView(0, 240f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            container.Add(split);

            var rail = new VisualElement();
            rail.AddToClassList("gh-rail");
            var railScroll = new ScrollView();
            railScroll.style.flexGrow = 1;
            foreach (var e in ConfigEntries())
                railScroll.Add(BuildRow(e));
            rail.Add(railScroll);
            split.Add(rail);

            var right = new ScrollView();
            right.style.flexGrow = 1;
            var inner = new VisualElement();
            inner.AddToClassList("gh-detail");
            right.Add(inner);
            split.Add(right);

            var entry = ResolveConfigEntry();
            if (entry == null || entry.Asset == null)
            {
                inner.Add(new Label("Нет конфигов/дизайн-SO.").WithClass("gh-stub"));
                return;
            }

            inner.Add(new Label(entry.DisplayName).WithClass("gh-hname"));

            var diff = ConfigDiff.Diff(entry.Asset);
            inner.Add(new Label(diff.Count == 0
                ? "Отличий от код-дефолтов нет"
                : $"Отличия от код-дефолтов: {diff.Count}").WithClass("gh-sec-h"));

            if (diff.Count > 0)
            {
                var block = new VisualElement();
                block.AddToClassList("gh-fieldblock");
                foreach (var r in diff)
                {
                    var row = new VisualElement();
                    row.AddToClassList("gh-strow");
                    var path = new Label(r.Path).WithClass("gh-st-name");
                    path.style.width = 260;
                    row.Add(path);
                    row.Add(new Label(r.Value).WithClass("gh-st-eff"));
                    var def = new Label($"деф: {r.Default}").WithClass("gh-st-dim");
                    def.style.marginLeft = 12;
                    row.Add(def);
                    block.Add(row);
                }
                inner.Add(block);
            }

            inner.Add(new Label("Поля").WithClass("gh-sec-h"));
            inner.Add(new InspectorElement(new SerializedObject(entry.Asset)));
        }
    }
}
