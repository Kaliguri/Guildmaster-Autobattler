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
    /// <c>GuildmasterPanelSettings</c> (тема даёт gm-токены) и Source Asset = <c>DevBattlePicker.uxml</c>
    /// (шелл панели + компактные dev-классы). Код только НАПОЛНЯЕТ список — вёрстка в UXML/USS.
    /// VContainer инжектит зависимости; при отсутствии — самоинжект из <c>CombatLifetimeScope</c> в Start.
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

            _overlay = root.Q<VisualElement>("gm-dev-overlay");
            var scroll = root.Q<ScrollView>("gm-dev-scroll");

            if (_overlay == null || scroll == null)
            {
                Debug.LogWarning("[DevEncounterPanel] - на UIDocument не назначен DevBattlePicker.uxml " +
                                 "(нет #gm-dev-overlay/#gm-dev-scroll). Source Asset панели пуст?");
                enabled = false;
                return;
            }

            _list = scroll.contentContainer;
            PopulateRows();
        }

        private void PopulateRows()
        {
            // Разметку строк держит общий билдер (им же пользуется превью-стенд); панель отвечает за
            // реальные действия Play и подсветку активного.
            DevBattlePickerView.Populate(_list, _content, PlayPreset, PlayEncounter, _rows);
            RefreshActiveHighlight();
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
                _rows[i].row.EnableInClassList("gm-dev-row--active", active);
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_overlay != null)
                _overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
