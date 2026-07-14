using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Dev-панель запуска боёв (UI Toolkit; план шаги 1/3). Два списка: <b>Encounters</b> (только враги —
    /// player-сторона = временная dev-реликвия) и <b>Battle Presets</b> (враги + свой player-ростер).
    /// <b>F2</b> — показать/скрыть. Запускает через <see cref="EncounterLoader"/>; <b>R</b> (рестарт на
    /// месте) идёт через единого владельца <see cref="GuildmasterCommands"/> (F5-безопасно).
    /// </summary>
    /// <remarks>
    /// Повесь на GameObject с <see cref="UIDocument"/> в BattleScene; на UIDocument назначь
    /// <c>GuildmasterPanelSettings</c> (тема .tss даёт gm-классы). VContainer инжектит зависимости; при
    /// отсутствии — самоинжект из <c>CombatLifetimeScope</c> в Start (как <c>CombatUnitDebugView</c>).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DevEncounterPanel : MonoBehaviour
    {
        [Header("Dev player side для списка Encounters (заглушка шага 1)")]
        [Tooltip("Временная player-реликвия (team 0) для запуска ГОЛЫХ энкаунтеров. Пресеты используют свой ростер. " +
                 "Пусто = энкаунтер без союзников (превью врагов).")]
        [SerializeField] private RelicData _devPlayerRelic;

        [Tooltip("Позиция dev-player-реликвии на арене (для списка Encounters).")]
        [SerializeField] private Vector2 _devPlayerPosition = new Vector2(-5f, 0f);

        [Tooltip("Показать панель при старте боя.")]
        [SerializeField] private bool _visibleOnStart = true;

        private EncounterLoader  _loader;
        private IContentDatabase _content;
        private UIDocument       _document;

        private VisualElement _overlay;   // обёртка в углу (тогглим display)
        private VisualElement _list;
        private readonly List<(string key, VisualElement row)> _rows = new();
        private string _activeKey;        // "e:<id>" | "p:<id>" — что запущено последним (подсветка)
        private bool _visible;

        [Inject]
        public void Construct(EncounterLoader loader, IContentDatabase content)
        {
            _loader  = loader;
            _content = content;
        }

        private void Start()
        {
            if (_loader == null || _content == null)
            {
                // Самоинжект (DevTools знает Game, обратного нет) — как CombatUnitDebugView.
                var scope = LifetimeScope.Find<Guildmaster.Game.CombatLifetimeScope>();
                scope?.Container.Inject(this);
            }

            _document = GetComponent<UIDocument>();
            if (_document == null) { Debug.LogWarning("[DevEncounterPanel] - нет UIDocument"); enabled = false; return; }

            BuildUi();
            SetVisible(_visibleOnStart);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) SetVisible(!_visible);
        }

        private void BuildUi()
        {
            VisualElement root = _document.rootVisualElement;
            root.Clear();

            // Оверлей в левом-верхнем углу — НЕ full-bleed (бой видно). Сам пропускает клики; ловит их панель.
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 8;
            _overlay.style.top  = 8;
            root.Add(_overlay);

            var panel = new VisualElement();
            panel.AddToClassList("gm-panel");
            panel.style.width = 280;
            panel.style.maxHeight = 460;
            _overlay.Add(panel);

            var title = new Label("Battles (dev)");
            title.AddToClassList("gm-panel__title");
            panel.Add(title);

            var divider = new VisualElement();
            divider.AddToClassList("gm-divider");
            panel.Add(divider);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);
            _list = scroll.contentContainer;

            PopulateRows();

            var footer = new VisualElement();
            footer.AddToClassList("gm-divider");
            panel.Add(footer);

            var hint = new Label("F2 — hide · R — restart");
            hint.AddToClassList("gm-text-muted");
            panel.Add(hint);
        }

        private void PopulateRows()
        {
            _rows.Clear();
            _list.Clear();

            IReadOnlyList<EncounterData> encounters = _content?.All<EncounterData>();
            IReadOnlyList<BattlePresetData> presets = _content?.All<BattlePresetData>();

            bool any = (encounters != null && encounters.Count > 0) || (presets != null && presets.Count > 0);
            if (!any)
            {
                var empty = new Label("Нет боёв.\nСоздай ассеты + Sync Content Database.");
                empty.AddToClassList("gm-text-muted");
                empty.style.whiteSpace = WhiteSpace.Normal;
                _list.Add(empty);
                return;
            }

            // ── Пресеты (готовые бои со своим ростером) ──
            if (presets != null && presets.Count > 0)
            {
                AddSectionHeader("Presets");
                for (int i = 0; i < presets.Count; i++)
                {
                    BattlePresetData p = presets[i];
                    AddRow("p:" + p.Id, $"{Short(p.Id)}  ·  {RosterSize(p)}p", "Play", () => PlayPreset(p));
                }
            }

            // ── Энкаунтеры (только враги; player = dev-реликвия) ──
            if (encounters != null && encounters.Count > 0)
            {
                AddSectionHeader("Encounters");
                for (int i = 0; i < encounters.Count; i++)
                {
                    EncounterData e = encounters[i];
                    AddRow("e:" + e.Id, $"{Short(e.Id)}  ·  {e.Tier}", "Play", () => PlayEncounter(e));
                }
            }

            RefreshActiveHighlight();
        }

        private void AddSectionHeader(string text)
        {
            var h = new Label(text);
            h.AddToClassList("gm-text-muted");
            h.style.marginTop = 4;
            h.style.marginBottom = 2;
            h.style.unityFontStyleAndWeight = FontStyle.Bold;
            _list.Add(h);
        }

        private void AddRow(string key, string label, string buttonText, System.Action onPlay)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            var lbl = new Label(label);
            lbl.style.flexGrow = 1;
            lbl.style.color = new Color(0.88f, 0.86f, 0.78f); // светлый на латунной панели (нет gm-класса под ряд)
            row.Add(lbl);

            var play = new Button(onPlay) { text = buttonText };
            play.AddToClassList("gm-button");
            play.style.marginTop = 0;
            play.style.marginBottom = 0;
            row.Add(play);

            _list.Add(row);
            _rows.Add((key, row));
        }

        private void PlayEncounter(EncounterData enc)
        {
            if (_loader == null) { Debug.LogWarning("[DevEncounterPanel] - EncounterLoader не внедрён"); return; }
            List<PlayerSpawn> side = BuildDevPlayerSide();
            _loader.Load(enc, side);
            GuildmasterCommands.SetLastBattle(_ => ReloadEncounterViaLiveScope(enc, side));
            _activeKey = "e:" + enc.Id;
            RefreshActiveHighlight();
        }

        private void PlayPreset(BattlePresetData preset)
        {
            if (_loader == null) { Debug.LogWarning("[DevEncounterPanel] - EncounterLoader не внедрён"); return; }
            _loader.LoadPreset(preset);
            GuildmasterCommands.SetLastBattle(_ => ReloadPresetViaLiveScope(preset));
            _activeKey = "p:" + preset.Id;
            RefreshActiveHighlight();
        }

        private List<PlayerSpawn> BuildDevPlayerSide()
        {
            if (_devPlayerRelic == null) return null;
            return new List<PlayerSpawn> { new PlayerSpawn(_devPlayerRelic, null, _devPlayerPosition) };
        }

        // Рестарт по R: находим ТЕКУЩИЙ боевой скоуп и его загрузчик (не захватываем старый — F5-безопасно).
        private static void ReloadEncounterViaLiveScope(EncounterData enc, List<PlayerSpawn> side)
        {
            var loader = LiveLoader();
            if (loader != null) loader.Load(enc, side);
        }

        private static void ReloadPresetViaLiveScope(BattlePresetData preset)
        {
            var loader = LiveLoader();
            if (loader != null) loader.LoadPreset(preset);
        }

        private static EncounterLoader LiveLoader()
        {
            var scope = LifetimeScope.Find<Guildmaster.Game.CombatLifetimeScope>();
            if (scope == null) { Debug.LogWarning("[DevEncounterPanel] - R: боевой скоуп не найден"); return null; }
            return scope.Container.Resolve<EncounterLoader>();
        }

        private void RefreshActiveHighlight()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool active = _activeKey != null && _rows[i].key == _activeKey;
                _rows[i].row.style.backgroundColor = active
                    ? new Color(1f, 1f, 1f, 0.10f)
                    : new Color(0f, 0f, 0f, 0f);
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_overlay != null)
                _overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
