using System;
using System.Collections.Generic;
using System.Linq;
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
        // Инструмент-страницы. Домены типов SO — динамические табы (строятся из индекса), не enum.
        public enum Page { Browser, Balance, Audio, Doctor, Configs }

        // Конфиг-подобные домены редактируются на странице Configs, а не отдельным табом-доменом.
        private static readonly HashSet<string> ConfigLikeDomains =
            new HashSet<string> { "Configs", "Design", "Audio" };

        public static bool IsConfigLike(string domain) => ConfigLikeDomains.Contains(domain);

        private const string StylesDir = "Assets/_Project/Scripts/EditorTools/ContentHub/Styles/";
        private const string WindowTitle = "Content Hub";

        // Персист текущей страницы через сериализованное состояние окна — переживает domain reload.
        [SerializeField] private Page _page = Page.Browser;
        [SerializeField] private string _selectedGuid;

        private VisualElement _content;
        private VisualElement _tabbar;
        private HubToasts _toasts;
        private readonly Dictionary<Page, Button> _pills = new Dictionary<Page, Button>();
        private readonly Dictionary<string, Button> _domainPills = new Dictionary<string, Button>();

        [MenuItem("Alebardium/Content Hub", priority = 0)]
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
            RebuildContent();
            RefreshDoctorBadge();
        }

        private void OnDisable()
        {
            ContentIndex.Changed -= OnIndexChanged;
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

            _tabbar = new VisualElement();
            _tabbar.AddToClassList("gh-tabbar");
            shell.Add(_tabbar);
            PopulateTabs();

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

        /// <summary>Строит панель табов: домены типов SO (динамически из индекса) + инструменты.</summary>
        private void PopulateTabs()
        {
            if (_tabbar == null) return;
            _tabbar.Clear();
            _pills.Clear();
            _domainPills.Clear();

            // Группа 1 — типы SO: «Все» + по табу на домен контента.
            var content = NewPillGroup();
            content.Add(MakeDomainPill("Все", ""));
            foreach (var domain in ContentDomainTabs())
                content.Add(MakeDomainPill(domain, domain));
            _tabbar.Add(content);

            _tabbar.Add(PillSep());

            // Группа 2 — аналитика.
            var analytics = NewPillGroup();
            analytics.Add(MakePagePill(Page.Balance));
            analytics.Add(MakePagePill(Page.Audio));
            _tabbar.Add(analytics);

            _tabbar.Add(PillSep());

            // Группа 3 — инструменты.
            var tools = NewPillGroup();
            tools.Add(MakePagePill(Page.Doctor));
            tools.Add(MakePagePill(Page.Configs));
            _tabbar.Add(tools);
        }

        private IEnumerable<string> ContentDomainTabs() =>
            ContentIndex.Entries.Select(e => e.Domain).Distinct()
                .Where(d => !IsConfigLike(d))
                .OrderBy(DomainOrder).ThenBy(d => d, StringComparer.OrdinalIgnoreCase);

        private static VisualElement NewPillGroup()
        {
            var g = new VisualElement();
            g.AddToClassList("gh-pill-group");
            return g;
        }

        private static VisualElement PillSep()
        {
            var s = new VisualElement();
            s.AddToClassList("gh-pill-group-sep");
            s.pickingMode = PickingMode.Ignore;
            return s;
        }

        private Button MakeDomainPill(string label, string domain)
        {
            var b = new Button(() => GoToDomain(domain));
            b.AddToClassList("gh-pill");
            if (_page == Page.Browser && _browserDomain == domain) b.AddToClassList("gh-pill--active");
            var l = new Label(label); l.AddToClassList("gh-pill-label"); b.Add(l);
            _domainPills[domain] = b;
            return b;
        }

        private Button MakePagePill(Page page)
        {
            var b = new Button(() => SelectPage(page));
            b.AddToClassList("gh-pill");
            if (_page == page) b.AddToClassList("gh-pill--active");
            var label = new Label(PageLabel(page)); label.AddToClassList("gh-pill-label"); b.Add(label);

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

        private void GoToDomain(string domain)
        {
            _page = Page.Browser;
            _browserDomain = domain;
            ApplyView();
        }

        private void SelectPage(Page page)
        {
            _page = page;
            ApplyView();
        }

        /// <summary>Применить смену вида: перестроить табы (активные состояния) + контент + бейдж + кнопки Nav.</summary>
        private void ApplyView()
        {
            PopulateTabs();
            RebuildContent();
            RefreshDoctorBadge();
            RefreshNavButtons();
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
            Page.Audio => "Audio",
            Page.Doctor => "Doctor",
            Page.Configs => "Configs",
            _ => page.ToString(),
        };
    }
}
