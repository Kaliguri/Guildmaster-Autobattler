using System;
using System.Linq;
using Guildmaster.Presentation.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Audio: матрица озвучки юнитов (юнит × действие) из AudioCatalog — самые дырявые сверху.</summary>
    public sealed partial class ContentHubWindow
    {
        private void BuildAudio(VisualElement container)
        {
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            var inner = new VisualElement();
            inner.AddToClassList("gh-detail");
            scroll.Add(inner);
            container.Add(scroll);

            var catalog = ContentIndex.Entries.Select(e => e.Asset).OfType<AudioCatalog>().FirstOrDefault();
            if (catalog == null)
            {
                inner.Add(new Label("В проекте нет AudioCatalog.").WithClass("gh-stub"));
                return;
            }

            var actions = ((AudioAction[])Enum.GetValues(typeof(AudioAction)))
                .Where(a => a != AudioAction.Ui).ToArray();

            int Coverage(ContentEntry u) => actions.Count(a => catalog.Contains(u.Id + "." + AudioResolver.ActionKey(a)));

            var units = ContentIndex.Entries.Where(e => e.IsUnit)
                .OrderBy(Coverage).ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

            inner.Add(new Label($"Озвучка юнитов ({catalog.name}) — ✔ точная запись · ○ дефолт действия · · нет")
                .WithClass("gh-sec-h"));

            var head = new VisualElement();
            head.AddToClassList("gh-tbl-head");
            var hName = new Label("Юнит");
            hName.AddToClassList("gh-tbl-hcell"); hName.AddToClassList("gh-tbl-hcell--name");
            hName.style.width = 180; head.Add(hName);
            foreach (var a in actions)
            {
                var hc = new Label(AudioResolver.ActionKey(a));
                hc.AddToClassList("gh-tbl-hcell"); hc.style.width = 64;
                head.Add(hc);
            }
            inner.Add(head);

            foreach (var u in units)
            {
                var row = new VisualElement();
                row.AddToClassList("gh-tbl-row");

                var name = new Label(u.DisplayName);
                name.AddToClassList("gh-tbl-cell"); name.AddToClassList("gh-tbl-cell--name");
                name.style.width = 180;
                name.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) NavigateToEntry(u); });
                row.Add(name);

                foreach (var a in actions)
                {
                    bool exact = catalog.Contains(u.Id + "." + AudioResolver.ActionKey(a));
                    bool def = catalog.HasDefault(a);
                    var cell = new Label(exact ? "✔" : def ? "○" : "·");
                    cell.AddToClassList("gh-tbl-cell");
                    cell.style.width = 64;
                    cell.style.unityTextAlign = TextAnchor.MiddleCenter;
                    if (exact) cell.style.color = new Color(0.5f, 0.7f, 0.41f);
                    else if (!def) cell.style.color = new Color(0.47f, 0.44f, 0.41f);
                    row.Add(cell);
                }
                inner.Add(row);
            }
        }
    }
}
