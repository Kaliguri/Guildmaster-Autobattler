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
    /// Dev-панель выбора энкаунтера (UI Toolkit; план шаг 1). Список всех <see cref="EncounterData"/> из
    /// контент-БД, кнопка запуска на каждый; <b>F2</b> — показать/скрыть. Запускает бой через
    /// <see cref="EncounterLoader"/>. Player-сторона — временная dev-реликвия (заглушка шага 1; в шаге 3
    /// её сменит ростер <c>BattlePresetData</c>). <b>R</b> (рестарт на месте) идёт через единый владелец
    /// <see cref="GuildmasterCommands"/> — панель лишь регистрирует свой рестарт (F5-безопасно).
    /// </summary>
    /// <remarks>
    /// Повесь на GameObject с <see cref="UIDocument"/> в BattleScene; на UIDocument назначь
    /// <c>GuildmasterPanelSettings</c> (тема .tss даёт gm-классы). VContainer инжектит зависимости; при
    /// отсутствии — самоинжект из <c>CombatLifetimeScope</c> в Start (как <c>CombatUnitDebugView</c>).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DevEncounterPanel : MonoBehaviour
    {
        [Header("Dev player side (заглушка шага 1)")]
        [Tooltip("Временная player-реликвия (team 0), пока нет BattlePreset-ростера (шаг 3). " +
                 "Пусто = бой без союзников (превью врагов).")]
        [SerializeField] private RelicData _devPlayerRelic;

        [Tooltip("Позиция dev-player-реликвии на арене.")]
        [SerializeField] private Vector2 _devPlayerPosition = new Vector2(-5f, 0f);

        [Tooltip("Показать панель при старте боя.")]
        [SerializeField] private bool _visibleOnStart = true;

        private EncounterLoader  _loader;
        private IContentDatabase _content;
        private UIDocument       _document;

        private VisualElement _overlay;      // обёртка в углу (тогглим display)
        private VisualElement _listContainer;
        private readonly List<(EncounterData enc, VisualElement row)> _rows = new();
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

            // Оверлей-обёртка в левом-верхнем углу — НЕ full-bleed (бой видно). Сама пропускает клики; ловит их панель.
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 8;
            _overlay.style.top  = 8;
            root.Add(_overlay);

            var panel = new VisualElement();
            panel.AddToClassList("gm-panel");
            panel.style.width = 260;
            panel.style.maxHeight = 340;
            _overlay.Add(panel);

            var title = new Label("Encounters (dev)");
            title.AddToClassList("gm-panel__title");
            panel.Add(title);

            var divider = new VisualElement();
            divider.AddToClassList("gm-divider");
            panel.Add(divider);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);
            _listContainer = scroll.contentContainer;

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
            _listContainer.Clear();

            IReadOnlyList<EncounterData> encounters = _content?.All<EncounterData>();
            if (encounters == null || encounters.Count == 0)
            {
                var empty = new Label("Нет энкаунтеров.\nСоздай ассеты + Sync Content Database.");
                empty.AddToClassList("gm-text-muted");
                empty.style.whiteSpace = WhiteSpace.Normal;
                _listContainer.Add(empty);
                return;
            }

            for (int i = 0; i < encounters.Count; i++)
            {
                EncounterData enc = encounters[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;

                var label = new Label($"{Short(enc.Id)}  ·  {enc.Tier}");
                label.style.flexGrow = 1;
                label.style.color = new Color(0.88f, 0.86f, 0.78f); // светлый на латунной панели (нет gm-класса под ряд)
                row.Add(label);

                var play = new Button(() => Play(enc)) { text = "Play" };
                play.AddToClassList("gm-button");
                play.style.marginTop = 0;
                play.style.marginBottom = 0;
                row.Add(play);

                _listContainer.Add(row);
                _rows.Add((enc, row));
            }

            RefreshActiveHighlight();
        }

        private void Play(EncounterData enc)
        {
            if (_loader == null) { Debug.LogWarning("[DevEncounterPanel] - EncounterLoader не внедрён (нет активного боя?)"); return; }

            List<PlayerSpawn> side = BuildPlayerSide();
            _loader.Load(enc, side);

            // R — через единый владелец GuildmasterCommands. Делегат резолвит ЖИВОЙ скоуп (переживает F5).
            GuildmasterCommands.SetLastBattle(_ => ReloadViaLiveScope(enc, side));

            RefreshActiveHighlight();
        }

        private List<PlayerSpawn> BuildPlayerSide()
        {
            if (_devPlayerRelic == null) return null;
            return new List<PlayerSpawn> { new PlayerSpawn(_devPlayerRelic, null, _devPlayerPosition) };
        }

        // Рестарт по R: находим ТЕКУЩИЙ боевой скоуп и его загрузчик (не захватываем старый — F5-безопасно).
        private static void ReloadViaLiveScope(EncounterData enc, List<PlayerSpawn> side)
        {
            var scope = LifetimeScope.Find<Guildmaster.Game.CombatLifetimeScope>();
            var loader = scope != null ? scope.Container.Resolve<EncounterLoader>() : null;
            if (loader != null) loader.Load(enc, side);
            else Debug.LogWarning("[DevEncounterPanel] - R: боевой скоуп/загрузчик не найден");
        }

        private void RefreshActiveHighlight()
        {
            string activeId = _loader?.LastEncounterId;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool active = activeId != null && _rows[i].enc.Id == activeId;
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

        private static string Short(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("encounter.")
                ? id.Substring("encounter.".Length)
                : id;
    }
}
