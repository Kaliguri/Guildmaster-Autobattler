using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using Guildmaster.Data.Stats;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Browser: two-pane список контента + детальная (Odin через InspectorElement) + stat-context + CRUD.</summary>
    public sealed partial class ContentHubWindow
    {
        private ContentEntry _selected;
        private string _browserSearch = "";
        private VisualElement _detailPane;
        private ScrollView _railScroll;

        private void BuildBrowser(VisualElement container)
        {
            _selected = ResolveSelected();

            var col = new VisualElement();
            col.style.flexGrow = 1;
            container.Add(col);

            col.Add(BuildSubbar());

            var split = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            col.Add(split);

            split.Add(BuildRail());

            var detailScroll = new ScrollView();
            detailScroll.style.flexGrow = 1;
            _detailPane = new VisualElement();
            _detailPane.AddToClassList("gh-detail");
            detailScroll.Add(_detailPane);
            split.Add(detailScroll);

            RebuildDetail();
        }

        // ---------------------------------------------------------------- subbar (global CRUD)

        private VisualElement BuildSubbar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("gh-subbar");

            var create = new Button(ShowCreateMenu) { text = "+ Create" };
            create.AddToClassList("gh-btn");
            create.AddToClassList("gh-btn--pri");
            bar.Add(create);

            var sync = new Button(DoSync) { text = "Sync DB" };
            sync.AddToClassList("gh-btn");
            bar.Add(sync);

            return bar;
        }

        // ---------------------------------------------------------------- left rail

        private VisualElement BuildRail()
        {
            var rail = new VisualElement();
            rail.AddToClassList("gh-rail");
            rail.style.flexGrow = 1;

            var search = new ToolbarSearchField();
            search.AddToClassList("gh-rail-search");
            search.value = _browserSearch;
            search.RegisterValueChangedCallback(e =>
            {
                _browserSearch = e.newValue ?? "";
                PopulateList();
            });
            rail.Add(search);

            _railScroll = new ScrollView();
            _railScroll.style.flexGrow = 1;
            rail.Add(_railScroll);

            PopulateList();
            return rail;
        }

        private void PopulateList()
        {
            if (_railScroll == null) return;
            _railScroll.Clear();

            string q = _browserSearch.Trim().ToLowerInvariant();
            bool Match(ContentEntry e) =>
                q.Length == 0
                || (e.DisplayName != null && e.DisplayName.ToLowerInvariant().Contains(q))
                || (e.Id != null && e.Id.ToLowerInvariant().Contains(q));

            var groups = ContentIndex.Entries
                .GroupBy(e => e.Domain)
                .OrderBy(g => (g.Key == "Configs" || g.Key == "Visuals") ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var matching = group.Where(Match).OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                if (matching.Count == 0) continue;

                var header = new VisualElement();
                header.AddToClassList("gh-grp");
                header.Add(new Label(group.Key));
                var count = new Label(matching.Count.ToString());
                count.AddToClassList("gh-grp-count");
                header.Add(count);
                _railScroll.Add(header);

                foreach (var entry in matching)
                    _railScroll.Add(BuildRow(entry));
            }
        }

        private VisualElement BuildRow(ContentEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("gh-row");
            if (_selected == entry) row.AddToClassList("gh-row--sel");

            row.Add(new Label(entry.DisplayName));
            var id = new Label(entry.Id);
            id.AddToClassList("gh-row-id");
            row.Add(id);

            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                SelectEntry(entry);
            });
            return row;
        }

        // ---------------------------------------------------------------- selection

        private void SelectEntry(ContentEntry entry)
        {
            _selected = entry;
            _selectedGuid = entry?.Asset != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.Asset))
                : "";
            PopulateList();     // refresh highlight
            RebuildDetail();
        }

        private ContentEntry ResolveSelected()
        {
            if (string.IsNullOrEmpty(_selectedGuid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(_selectedGuid);
            if (string.IsNullOrEmpty(path)) return null;
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            return ContentIndex.Find(asset);
        }

        private void SelectAssetAndRebuild(UnityEngine.Object asset)
        {
            _selectedGuid = asset != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))
                : "";
            RebuildContent();
        }

        // ---------------------------------------------------------------- detail

        private void RebuildDetail()
        {
            if (_detailPane == null) return;
            _detailPane.Clear();

            if (_selected == null || _selected.Asset == null)
            {
                var hint = new Label("Выбери контент слева.");
                hint.AddToClassList("gh-stub");
                _detailPane.Add(hint);
                return;
            }

            BuildDetailHeader(_selected);

            _detailPane.Add(new Label("Поля") { }.WithClass("gh-sec-h"));
            _detailPane.Add(new InspectorElement(new SerializedObject(_selected.Asset)));

            if (_selected.IsUnit)
                BuildStatContext(_selected);
        }

        private void BuildDetailHeader(ContentEntry entry)
        {
            var meta = new VisualElement();
            meta.style.flexGrow = 1;

            meta.Add(new Label(entry.DisplayName).WithClass("gh-hname"));
            meta.Add(new Label(entry.Id).WithClass("gh-hid"));

            var badges = new VisualElement();
            badges.AddToClassList("gh-badges");
            badges.Add(Badge(TypeShort(entry.Type), accent: true));
            if (entry.Unit != null)
            {
                badges.Add(Badge(entry.Unit.DamageType.ToString()));
                badges.Add(Badge(entry.Unit.AttackType.ToString()));
                if (entry.Unit.ResourceType != ResourceType.None)
                    badges.Add(Badge(entry.Unit.ResourceType.ToString()));
            }
            meta.Add(badges);

            var actions = new VisualElement();
            actions.AddToClassList("gh-actions");
            actions.Add(MakeBtn("Reveal", () => { EditorGUIUtility.PingObject(entry.Asset); Selection.activeObject = entry.Asset; }));
            if (entry.Asset is ContentDefinition cd)
            {
                actions.Add(MakeBtn("Duplicate", () => DoDuplicate(cd)));
                actions.Add(MakeBtn("Validate", () => DoValidate(entry)));
                var del = MakeBtn("Delete", () => DoDelete(entry));
                del.AddToClassList("gh-btn--danger");
                actions.Add(del);
            }
            meta.Add(actions);

            var hdr = new VisualElement();
            hdr.AddToClassList("gh-hdr");
            hdr.Add(meta);
            _detailPane.Add(hdr);
        }

        private void BuildStatContext(ContentEntry entry)
        {
            _detailPane.Add(new Label("Статы — эффективные vs база и когорта").WithClass("gh-sec-h"));

            var block = new VisualElement();
            block.AddToClassList("gh-fieldblock");

            var cohort = ContentIndex.Cohort(entry.Type);
            var config = ContentIndex.StatsConfig;

            foreach (StatType st in Enum.GetValues(typeof(StatType)))
            {
                float eff = entry.Effective(st);
                float baseVal = config != null ? config.GetDefault(st) : StatsConfig.NaturalDefault(st);
                var agg = cohort != null ? cohort.For(st) : default;

                var row = new VisualElement();
                row.AddToClassList("gh-strow");
                row.Add(new Label(st.ToString()).WithClass("gh-st-name"));

                var effLbl = new Label(Num(eff)).WithClass("gh-st-num").WithClass("gh-st-eff");
                if (agg.Count > 1)
                {
                    if (eff > agg.Avg * 1.001f) effLbl.AddToClassList("gh-st-hi");
                    else if (eff < agg.Avg * 0.999f) effLbl.AddToClassList("gh-st-lo");
                }
                row.Add(effLbl);
                row.Add(new Label(Num(baseVal)).WithClass("gh-st-num").WithClass("gh-st-dim"));
                row.Add(new Label(agg.Count > 0 ? Num(agg.Avg) : "—").WithClass("gh-st-num").WithClass("gh-st-dim"));
                row.Add(new Label(agg.Count > 0 ? $"{Num(agg.Min)}–{Num(agg.Max)}" : "—")
                    .WithClass("gh-st-num").WithClass("gh-st-dim"));
                block.Add(row);
            }

            _detailPane.Add(block);
        }

        // ---------------------------------------------------------------- CRUD

        private void ShowCreateMenu()
        {
            var menu = new GenericMenu();
            foreach (Type type in ContentPaths.CreatableTypes)
            {
                Type captured = type;
                menu.AddItem(new GUIContent(TypeShort(type)), false, () =>
                {
                    var asset = ContentCrudService.Create(captured);
                    ContentIndex.Invalidate();
                    SelectAssetAndRebuild(asset);
                    _toasts?.Show($"Создан {asset.Id}", HubToasts.Kind.Success);
                });
            }
            menu.ShowAsContext();
        }

        private void DoDuplicate(ContentDefinition cd)
        {
            var copy = ContentCrudService.Duplicate(cd);
            if (copy == null) return;
            ContentIndex.Invalidate();
            SelectAssetAndRebuild(copy);
            _toasts?.Show($"Дубликат {copy.Id}", HubToasts.Kind.Success);
        }

        private void DoDelete(ContentEntry entry)
        {
            if (!(entry.Asset is ContentDefinition cd)) return;

            var usages = ContentCrudService.FindUsages(cd);
            if (usages.Count > 0)
            {
                EditorUtility.DisplayDialog("Нельзя удалить: есть использования",
                    $"'{cd.Id}' используется в {usages.Count} ассет(ах):\n\n" +
                    string.Join("\n", usages.Take(15)) + (usages.Count > 15 ? "\n…" : ""), "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Удалить контент?",
                    $"Удалить '{cd.Id}'\n({AssetDatabase.GetAssetPath(cd)})?", "Удалить", "Отмена"))
                return;

            ContentCrudService.TryDelete(cd, out _);
            ContentIndex.Invalidate();
            _selectedGuid = "";
            RebuildContent();
            _toasts?.Show("Удалено", HubToasts.Kind.Info);
        }

        private void DoValidate(ContentEntry entry)
        {
            var issues = ContentValidationService.Validate(entry);
            if (issues.Count == 0)
                _toasts?.Show($"'{entry.Id}' — без замечаний", HubToasts.Kind.Success);
            else
                _toasts?.Show($"{entry.Id}: {issues.Count} проблем — {issues[0].Message}", HubToasts.Kind.Error, 3600);
        }

        private void DoSync()
        {
            ContentDatabaseSync.Sync(verbose: true);
            ContentIndex.Invalidate();
            _toasts?.Show("Content Database синхронизирована", HubToasts.Kind.Success);
        }

        // ---------------------------------------------------------------- helpers

        private static string TypeShort(Type t) => t == null ? "?" : t.Name.Replace("Data", "");

        private static string Num(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? v.ToString("0") : v.ToString("0.##");

        private static VisualElement Badge(string text, bool accent = false)
        {
            var b = new Label(text);
            b.AddToClassList("gh-badge");
            if (accent) b.AddToClassList("gh-badge--acc");
            return b;
        }

        private Button MakeBtn(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("gh-btn");
            return b;
        }
    }

    internal static class VisualElementClassExtensions
    {
        public static T WithClass<T>(this T el, string cls) where T : VisualElement
        {
            el.AddToClassList(cls);
            return el;
        }
    }
}
