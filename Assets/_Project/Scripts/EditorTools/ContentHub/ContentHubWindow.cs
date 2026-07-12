using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Единое UI Toolkit окно для всего дата-слоя (ТЗ «08. Контент-хаб»). Шелл: pill-навигация по страницам,
    /// роутер контента, персист состояния. Паттерн перенесён из Bloodlines <c>ThemeEditorWindow</c> с упрощением.
    /// <para>Пакет 0 — только каркас: страницы-заглушки. Реальные страницы приходят пакетами 2+.</para>
    /// </summary>
    public sealed partial class ContentHubWindow : EditorWindow
    {
        // Порядок объявления = порядок отображения. Группировка pill-бара — в PageGroups.
        public enum Page { Browser, Balance, Coverage, Visual, Audio, Doctor, Configs }

        private static readonly Page[][] PageGroups =
        {
            new[] { Page.Browser },                                  // Content
            new[] { Page.Balance, Page.Coverage, Page.Visual, Page.Audio }, // Design
            new[] { Page.Doctor, Page.Configs },                    // System
        };

        private const string StylesDir = "Assets/_Project/Scripts/EditorTools/ContentHub/Styles/";
        private const string WindowTitle = "Content Hub";

        // Персист текущей страницы через сериализованное состояние окна — переживает domain reload.
        [SerializeField] private Page _page = Page.Browser;
        [SerializeField] private string _selectedGuid;

        private VisualElement _content;
        private HubToasts _toasts;
        private readonly Dictionary<Page, Button> _pills = new Dictionary<Page, Button>();

        [MenuItem("Tools/Guildmaster/Content Hub")]
        public static void Open()
        {
            var w = GetWindow<ContentHubWindow>();
            w.titleContent = new GUIContent(WindowTitle);
            w.minSize = new Vector2(960, 600);
            w.Focus();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("gh-vars");

            LoadStyle(root, "ContentHubTokens.uss");
            LoadStyle(root, "ContentHub.uss");

            BuildShell(root);
            _toasts = new HubToasts(root);
            root.UnregisterCallback<KeyDownEvent>(OnGlobalKey);
            root.RegisterCallback<KeyDownEvent>(OnGlobalKey);
            ContentIndex.Changed -= OnIndexChanged;
            ContentIndex.Changed += OnIndexChanged;
            EditorApplication.update -= ClipTick;
            EditorApplication.update += ClipTick;
            RebuildContent();
            RefreshDoctorBadge();
        }

        private void OnDisable()
        {
            ContentIndex.Changed -= OnIndexChanged;
            EditorApplication.update -= ClipTick;
            CloseCommandPalette();
        }

        private void OnFocus()
        {
            // Свежий rebuild при возврате фокуса — правки USS/C# отражаются без переоткрытия окна
            // (тот же приём, что у ThemeEditorWindow).
            if (_content != null) RebuildContent();
        }

        private static void LoadStyle(VisualElement root, string file)
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesDir + file);
            if (uss != null) root.styleSheets.Add(uss);
        }

        // ---------------------------------------------------------------- shell

        private void BuildShell(VisualElement root)
        {
            var shell = new VisualElement { name = "root" };
            shell.AddToClassList("gh-root");

            shell.Add(BuildToolbar());

            var accent = new VisualElement { name = "accentLine" };
            accent.AddToClassList("gh-accent-line");
            shell.Add(accent);

            shell.Add(BuildTabbar());

            _content = new VisualElement { name = "content" };
            _content.AddToClassList("gh-content");
            shell.Add(_content);

            root.Add(shell);
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("gh-toolbar");

            var brand = new VisualElement();
            brand.AddToClassList("gh-brand");
            var dim = new Label("Guildmaster"); dim.AddToClassList("gh-brand-dim");
            var bright = new Label("Content Hub"); bright.AddToClassList("gh-brand-bright");
            brand.Add(dim); brand.Add(bright);
            bar.Add(brand);

            var spacer = new VisualElement(); spacer.AddToClassList("gh-header-spacer");
            bar.Add(spacer);

            bar.Add(BuildNavButtons());

            return bar;
        }

        private VisualElement BuildTabbar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("gh-tabbar");
            _pills.Clear();

            for (int g = 0; g < PageGroups.Length; g++)
            {
                if (g > 0)
                {
                    var sep = new VisualElement();
                    sep.AddToClassList("gh-pill-group-sep");
                    sep.pickingMode = PickingMode.Ignore;
                    bar.Add(sep);
                }

                var group = new VisualElement();
                group.AddToClassList("gh-pill-group");
                foreach (var page in PageGroups[g]) group.Add(MakePill(page));
                bar.Add(group);
            }

            return bar;
        }

        private Button MakePill(Page page)
        {
            var b = new Button(() => SelectPage(page));
            b.AddToClassList("gh-pill");
            if (page == _page) b.AddToClassList("gh-pill--active");

            var label = new Label(PageLabel(page));
            label.AddToClassList("gh-pill-label");
            b.Add(label);

            if (page == Page.Doctor)
            {
                _doctorBadge = new Label();
                _doctorBadge.AddToClassList("gh-pill-badge");
                _doctorBadge.style.display = DisplayStyle.None;
                b.Add(_doctorBadge);
            }

            _pills[page] = b;
            return b;
        }

        private void SelectPage(Page page)
        {
            if (_page == page) return;
            if (_pills.TryGetValue(_page, out var prev)) prev.RemoveFromClassList("gh-pill--active");
            _page = page;
            if (_pills.TryGetValue(_page, out var next)) next.AddToClassList("gh-pill--active");
            RebuildContent();
        }

        // ---------------------------------------------------------------- content router

        private void RebuildContent()
        {
            _content.Clear();

            switch (_page)
            {
                case Page.Browser:
                    BuildBrowser(_content);
                    break;
                case Page.Doctor:
                    BuildDoctor(_content);
                    break;
                case Page.Balance:
                    BuildBalance(_content);
                    break;
                case Page.Coverage:
                    BuildCoverage(_content);
                    break;
                case Page.Visual:
                    BuildVisual(_content);
                    break;
                case Page.Audio:
                    BuildAudio(_content);
                    break;
                case Page.Configs:
                    BuildConfigs(_content);
                    break;
                default:
                    var page = new VisualElement();
                    page.AddToClassList("gh-page");
                    _content.Add(page);
                    var stub = new Label($"«{PageLabel(_page)}» — скоро (см. ТЗ 08).");
                    stub.AddToClassList("gh-stub");
                    page.Add(stub);
                    break;
            }
        }

        // ---------------------------------------------------------------- page metadata

        private static string PageLabel(Page page) => page switch
        {
            Page.Browser => "Browser",
            Page.Balance => "Balance",
            Page.Coverage => "Coverage",
            Page.Visual => "Visual",
            Page.Audio => "Audio",
            Page.Doctor => "Doctor",
            Page.Configs => "Configs",
            _ => page.ToString(),
        };
    }
}
