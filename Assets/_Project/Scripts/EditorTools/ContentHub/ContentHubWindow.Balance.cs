using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Stats;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Balance: таблица юниты×статы (эффективные + производные), сортировка по клику, хитмап, pin-compare.</summary>
    public sealed partial class ContentHubWindow
    {
        private sealed class BalCol
        {
            public string Title;
            public float Width;
            public bool IsName;
            public Func<ContentEntry, float> Num;
        }

        private List<BalCol> _balCols;
        private VisualElement _balTable;
        private readonly HashSet<ContentEntry> _pinned = new HashSet<ContentEntry>();
        private string _balSearch = "";
        private int _balTypeFilter;         // 0 = все, 1 = Relics, 2 = Enemies
        private bool _balOnlyPinned;
        private int _balSortCol;            // индекс в _balCols
        private bool _balSortAsc = true;
        private List<ContentEntry> _balCurrentRows = new List<ContentEntry>();

        private void EnsureBalCols()
        {
            if (_balCols != null) return;
            _balCols = new List<BalCol>
            {
                new BalCol { Title = "Юнит", Width = 180, IsName = true },
                new BalCol { Title = "DPS",  Width = 64, Num = e => StatMath.AutoAttackDps(e.EffectiveStats) },
                new BalCol { Title = "Атк/с", Width = 56, Num = e => StatMath.AttacksPerSecond(e.Effective(StatType.AttackSpeed)) },
            };
            foreach (StatType st in Enum.GetValues(typeof(StatType)))
            {
                var captured = st;
                _balCols.Add(new BalCol { Title = ShortStat(st), Width = 62, Num = e => e.Effective(captured) });
            }
        }

        private void BuildBalance(VisualElement container)
        {
            EnsureBalCols();

            var col = new VisualElement();
            col.style.flexGrow = 1;
            container.Add(col);

            // ---- toolbar
            var bar = new VisualElement();
            bar.AddToClassList("gh-subbar");

            var search = new ToolbarSearchField();
            search.value = _balSearch;
            search.RegisterValueChangedCallback(e => { _balSearch = e.newValue ?? ""; PopulateBalance(); });
            bar.Add(search);

            var typePopup = new PopupField<string>(new List<string> { "Все", "Relics", "Enemies" }, _balTypeFilter);
            typePopup.style.width = 110;
            typePopup.RegisterValueChangedCallback(e => { _balTypeFilter = typePopup.index; PopulateBalance(); });
            bar.Add(typePopup);

            var onlyPinned = new Toggle("только закреплённые") { value = _balOnlyPinned };
            onlyPinned.RegisterValueChangedCallback(e => { _balOnlyPinned = e.newValue; PopulateBalance(); });
            bar.Add(onlyPinned);

            var export = new Button(ExportBalanceMarkdown) { text = "Export MD" };
            export.AddToClassList("gh-btn");
            bar.Add(export);

            col.Add(bar);

            // ---- table
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            _balTable = new VisualElement();
            scroll.Add(_balTable);
            col.Add(scroll);

            PopulateBalance();
        }

        private void PopulateBalance()
        {
            if (_balTable == null) return;
            _balTable.Clear();

            string q = _balSearch.Trim().ToLowerInvariant();
            var rows = ContentIndex.Entries.Where(e => e.IsUnit).ToList();

            if (_balTypeFilter == 1) rows = rows.Where(e => e.Type != null && e.Type.Name == "RelicData").ToList();
            else if (_balTypeFilter == 2) rows = rows.Where(e => e.Type != null && e.Type.Name == "EnemyData").ToList();
            if (_balOnlyPinned) rows = rows.Where(e => _pinned.Contains(e)).ToList();
            if (q.Length > 0)
                rows = rows.Where(e => (e.DisplayName?.ToLowerInvariant().Contains(q) ?? false)
                                       || (e.Id?.ToLowerInvariant().Contains(q) ?? false)).ToList();

            _balSortCol = Mathf.Clamp(_balSortCol, 0, _balCols.Count - 1);
            var sortCol = _balCols[_balSortCol];
            rows.Sort((a, b) =>
            {
                bool pa = _pinned.Contains(a), pb = _pinned.Contains(b);
                if (pa != pb) return pa ? -1 : 1;
                int c = sortCol.IsName
                    ? string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase)
                    : sortCol.Num(a).CompareTo(sortCol.Num(b));
                return _balSortAsc ? c : -c;
            });

            // диапазоны для хитмапа (по текущему набору строк)
            var ranges = new Dictionary<BalCol, (float min, float max)>();
            foreach (var c in _balCols)
            {
                if (c.IsName) continue;
                float min = float.MaxValue, max = float.MinValue;
                foreach (var r in rows) { float v = c.Num(r); if (v < min) min = v; if (v > max) max = v; }
                ranges[c] = (min, max);
            }

            _balCurrentRows = rows;

            _balTable.Add(BuildBalHeader());
            foreach (var entry in rows)
                _balTable.Add(BuildBalRow(entry, ranges));
        }

        private void ExportBalanceMarkdown()
        {
            var numeric = _balCols.Where(c => !c.IsName).ToList();
            var headers = new List<string> { "Юнит" };
            headers.AddRange(numeric.Select(c => c.Title));

            var rows = new List<IReadOnlyList<string>>();
            foreach (var e in _balCurrentRows)
            {
                var cells = new List<string> { e.DisplayName };
                cells.AddRange(numeric.Select(c => Num(c.Num(e))));
                rows.Add(cells);
            }

            EditorGUIUtility.systemCopyBuffer = MarkdownTable.Format(headers, rows);
            _toasts?.Show($"Markdown ({_balCurrentRows.Count} строк) в буфере", HubToasts.Kind.Success);
        }

        private VisualElement BuildBalHeader()
        {
            var head = new VisualElement();
            head.AddToClassList("gh-tbl-head");

            var pin = new Label("");
            pin.AddToClassList("gh-tbl-pin");
            head.Add(pin);

            for (int i = 0; i < _balCols.Count; i++)
            {
                var c = _balCols[i];
                int idx = i;
                var cell = new Label(c.Title + (i == _balSortCol ? (_balSortAsc ? " ▲" : " ▼") : ""));
                cell.AddToClassList("gh-tbl-hcell");
                if (c.IsName) cell.AddToClassList("gh-tbl-hcell--name");
                if (i == _balSortCol) cell.AddToClassList("gh-tbl-hcell--sorted");
                cell.style.width = c.Width;
                cell.tooltip = c.Title;
                cell.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    if (_balSortCol == idx) _balSortAsc = !_balSortAsc;
                    else { _balSortCol = idx; _balSortAsc = true; }
                    PopulateBalance();
                });
                head.Add(cell);
            }
            return head;
        }

        private VisualElement BuildBalRow(ContentEntry entry, Dictionary<BalCol, (float min, float max)> ranges)
        {
            var row = new VisualElement();
            row.AddToClassList("gh-tbl-row");

            bool pinned = _pinned.Contains(entry);
            var pin = new Label(pinned ? "★" : "☆");
            pin.AddToClassList("gh-tbl-pin");
            if (pinned) pin.AddToClassList("gh-tbl-pin--on");
            pin.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (!_pinned.Remove(entry)) _pinned.Add(entry);
                PopulateBalance();
            });
            row.Add(pin);

            foreach (var c in _balCols)
            {
                if (c.IsName)
                {
                    var name = new Label(entry.DisplayName);
                    name.AddToClassList("gh-tbl-cell");
                    name.AddToClassList("gh-tbl-cell--name");
                    name.style.width = c.Width;
                    name.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) NavigateToEntry(entry); });
                    row.Add(name);
                    continue;
                }

                float v = c.Num(entry);
                var cell = new Label(Num(v));
                cell.AddToClassList("gh-tbl-cell");
                cell.style.width = c.Width;

                var (min, max) = ranges[c];
                if (max > min)
                {
                    float t = (v - min) / (max - min);
                    cell.style.backgroundColor = new Color(0.96f, 0.65f, 0.14f, t * 0.22f);
                }
                row.Add(cell);
            }
            return row;
        }

        // Короткие подписи статов для узких колонок.
        private static string ShortStat(StatType st)
        {
            switch (st)
            {
                case StatType.MaxHP: return "HP";
                case StatType.AutoAttackDamage: return "AA";
                case StatType.AttackSpeed: return "AS";
                case StatType.AttackRange: return "Rng";
                case StatType.AbilityPower: return "AP";
                case StatType.PhysArmor: return "PArm";
                case StatType.MagicArmor: return "MArm";
                case StatType.MoveSpeed: return "MS";
                case StatType.MaxResource: return "Res";
                default: return st.ToString().Length > 6 ? st.ToString().Substring(0, 6) : st.ToString();
            }
        }
    }
}
