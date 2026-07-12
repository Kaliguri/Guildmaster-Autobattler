using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Навигация: Back/Forward история, кликабельные чипы связей, командная палитра (Ctrl+K).</summary>
    public sealed partial class ContentHubWindow
    {
        private readonly struct NavAddress
        {
            public readonly Page Page;
            public readonly string Guid;
            public readonly string Domain;   // домен-таб для Browser
            public NavAddress(Page page, string guid, string domain) { Page = page; Guid = guid; Domain = domain; }
        }

        private readonly NavHistory<NavAddress> _nav = new NavHistory<NavAddress>();
        private Button _backBtn, _fwdBtn;

        // command palette
        private VisualElement _cmdkScrim;
        private ScrollView _cmdkList;
        private List<ContentEntry> _cmdkFiltered = new List<ContentEntry>();
        private int _cmdkActive;

        // ---------------------------------------------------------------- toolbar buttons

        private VisualElement BuildNavButtons()
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.alignItems = Align.Center;

            _backBtn = new Button(Back) { text = "←" };
            _backBtn.AddToClassList("gh-navbtn");
            _backBtn.tooltip = "Назад";
            _fwdBtn = new Button(Forward) { text = "→" };
            _fwdBtn.AddToClassList("gh-navbtn");
            _fwdBtn.tooltip = "Вперёд";
            group.Add(_backBtn);
            group.Add(_fwdBtn);

            var kbd = new Button(OpenCommandPalette) { text = "Найти  Ctrl+K" };
            kbd.AddToClassList("gh-kbd");
            group.Add(kbd);

            RefreshNavButtons();
            return group;
        }

        private void RefreshNavButtons()
        {
            _backBtn?.SetEnabled(_nav.CanBack);
            _fwdBtn?.SetEnabled(_nav.CanForward);
        }

        // ---------------------------------------------------------------- history

        private static string GuidOf(ContentEntry e) =>
            e?.Asset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.Asset)) : "";

        /// <summary>Записать текущую позицию в историю (зовётся при выборе из списка Browser).</summary>
        internal void RecordNav(string guid)
        {
            _nav.Push(new NavAddress(Page.Browser, guid, _browserDomain));
            RefreshNavButtons();
        }

        /// <summary>Перейти к сущности (из чипа/палитры): нужный таб-домен (или Configs для конфиг-подобных) + выбор + история.</summary>
        private void NavigateToEntry(ContentEntry entry)
        {
            if (entry == null) return;
            var addr = IsConfigLike(entry.Domain)
                ? new NavAddress(Page.Configs, GuidOf(entry), "")
                : new NavAddress(Page.Browser, GuidOf(entry), entry.Domain);
            NavGoto(addr, record: true);
        }

        private void NavGoto(NavAddress addr, bool record)
        {
            if (record) _nav.Push(addr);
            _selectedGuid = addr.Guid;
            _page = addr.Page;
            if (addr.Page == Page.Browser) _browserDomain = addr.Domain;
            ApplyView();
        }

        private void Back()
        {
            if (!_nav.CanBack) return;
            NavGoto(_nav.Back(), record: false);
        }

        private void Forward()
        {
            if (!_nav.CanForward) return;
            NavGoto(_nav.Forward(), record: false);
        }

        // ---------------------------------------------------------------- relation chips

        private VisualElement EntityChip(ContentEntry target, string role)
        {
            var chip = new VisualElement();
            chip.AddToClassList("gh-chip");
            if (!string.IsNullOrEmpty(role))
                chip.Add(new Label(role).WithClass("gh-chip-role"));
            chip.Add(new Label(target.DisplayName));
            chip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                NavigateToEntry(target);
            });
            return chip;
        }

        private void BuildRelations(ContentEntry entry)
        {
            if (entry.Uses.Count > 0)
            {
                _detailPane.Add(new Label("Связи").WithClass("gh-sec-h"));
                var chips = new VisualElement();
                chips.AddToClassList("gh-chips");
                foreach (var u in entry.Uses.OrderBy(x => x.Domain))
                    chips.Add(EntityChip(u, u.Domain));
                _detailPane.Add(chips);
            }

            if (entry.UsedBy.Count > 0)
            {
                _detailPane.Add(new Label("Используется в").WithClass("gh-sec-h"));
                var chips = new VisualElement();
                chips.AddToClassList("gh-chips");
                foreach (var u in entry.UsedBy.OrderBy(x => x.Domain))
                    chips.Add(EntityChip(u, u.Domain));
                _detailPane.Add(chips);
            }
        }

        // ---------------------------------------------------------------- command palette (Ctrl+K)

        private void OnGlobalKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.K && (evt.ctrlKey || evt.commandKey))
            {
                OpenCommandPalette();
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        private void OpenCommandPalette()
        {
            CloseCommandPalette();
            var root = rootVisualElement;

            _cmdkScrim = new VisualElement();
            _cmdkScrim.AddToClassList("gh-cmdk-scrim");
            _cmdkScrim.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _cmdkScrim) CloseCommandPalette();
            });

            var panel = new VisualElement();
            panel.AddToClassList("gh-cmdk");

            var input = new ToolbarSearchField();
            input.AddToClassList("gh-cmdk-input");
            input.RegisterValueChangedCallback(e => FilterCommandPalette(e.newValue));
            panel.Add(input);

            _cmdkList = new ScrollView();
            _cmdkList.AddToClassList("gh-cmdk-list");
            panel.Add(_cmdkList);

            _cmdkScrim.Add(panel);
            root.Add(_cmdkScrim);

            FilterCommandPalette("");
            input.RegisterCallback<KeyDownEvent>(OnCommandPaletteKey);
            input.Q("unity-text-input")?.Focus();
        }

        private void FilterCommandPalette(string query)
        {
            query = (query ?? "").Trim().ToLowerInvariant();
            _cmdkFiltered = ContentIndex.Entries
                .Where(e => query.Length == 0
                            || (e.DisplayName?.ToLowerInvariant().Contains(query) ?? false)
                            || (e.Id?.ToLowerInvariant().Contains(query) ?? false))
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
            _cmdkActive = 0;
            RenderCommandPalette();
        }

        private void RenderCommandPalette()
        {
            if (_cmdkList == null) return;
            _cmdkList.Clear();
            for (int i = 0; i < _cmdkFiltered.Count; i++)
            {
                var entry = _cmdkFiltered[i];
                var item = new VisualElement();
                item.AddToClassList("gh-cmdk-item");
                if (i == _cmdkActive) item.AddToClassList("gh-cmdk-item--active");
                item.Add(new Label(entry.DisplayName));
                item.Add(new Label(entry.Id).WithClass("gh-cmdk-item-id"));
                var captured = entry;
                item.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    CloseCommandPalette();
                    NavigateToEntry(captured);
                });
                _cmdkList.Add(item);
            }
        }

        private void OnCommandPaletteKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape) { CloseCommandPalette(); evt.StopPropagation(); return; }
            if (_cmdkFiltered.Count == 0) return;

            if (evt.keyCode == KeyCode.DownArrow)
            {
                _cmdkActive = System.Math.Min(_cmdkActive + 1, _cmdkFiltered.Count - 1);
                RenderCommandPalette(); evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.UpArrow)
            {
                _cmdkActive = System.Math.Max(_cmdkActive - 1, 0);
                RenderCommandPalette(); evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                var target = _cmdkFiltered[_cmdkActive];
                CloseCommandPalette();
                NavigateToEntry(target);
                evt.StopPropagation();
            }
        }

        private void CloseCommandPalette()
        {
            _cmdkScrim?.RemoveFromHierarchy();
            _cmdkScrim = null;
            _cmdkList = null;
        }
    }
}
