using Cysharp.Threading.Tasks;
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

        /// <summary>Что показывает dev-панель сейчас (переключается F2/F1; None — скрыта).</summary>
        private enum DevView { None, Battles, Screens }

        private EncounterLoader  _loader;
        private IContentDatabase _content;
        private UIDocument       _document;

        private VisualElement _overlay;       // battles-режим (F2): обёртка в углу (тогглим display)
        private VisualElement _list;
        private VisualElement _screensRoot;   // screens-режим (F1): полноэкранный просмотр экранов (editor-only)
        private VisualElement _screenContent; // куда UiPreviewCatalog строит выбранный экран
        private readonly List<(string key, VisualElement row)> _rows = new();
        private string _activeKey;            // "e:<id>" | "p:<id>" — что запущено последним (подсветка)
        private DevView _view = DevView.None;

        [Inject]
        public void Construct(EncounterLoader loader, IContentDatabase content)
        {
            _loader  = loader;
            _content = content;
        }

        private void Start()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null) { Debug.LogWarning("[DevEncounterPanel] - нет UIDocument"); enabled = false; return; }

            if (_loader == null || _content == null)
            {
                // Самоинжект (DevTools знает Game, обратного нет) — как CombatUnitDebugView. Ждём, пока
                // скоуп ПОСТРОИТСЯ: его объект появляется в сцене раньше контейнера, и прямой
                // scope.Container.Inject падал NRE в зависимости от порядка объектов в сцене.
                // Список боёв строим уже после инъекции — до неё его не из чего собирать.
                DevSelfInject.WhenScopeReady<Guildmaster.Game.CombatLifetimeScope>(this, () =>
                {
                    BuildUi();
                    Apply();
                }).Forget();
                return;
            }

            BuildUi();
            Apply(); // старт скрыт (DevView.None) — панель не мозолит глаз, пока не нажали F2/F1
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // F2 — бои, F1 — экраны UI; повторное нажатие того же режима прячет панель.
            if (kb.f2Key.wasPressedThisFrame) Toggle(DevView.Battles);
            if (kb.f1Key.wasPressedThisFrame) Toggle(DevView.Screens);
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
            BuildScreensOverlay(root);
        }

        // F1-режим: полноэкранный просмотрщик UI-экранов (награда/ивент/хаб/галерея и т.д.) со стендовыми
        // данными через UiPreviewCatalog — чтобы глазами проверить экраны, не гоняя весь флоу. Editor-only
        // (каталог грузит UXML через AssetDatabase); в билде F1 просто не строится.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void BuildScreensOverlay(VisualElement root)
        {
#if UNITY_EDITOR
            _screensRoot = new VisualElement { name = "gm-dev-screens" };
            _screensRoot.style.position = Position.Absolute;
            _screensRoot.style.left = 0; _screensRoot.style.top = 0;
            _screensRoot.style.right = 0; _screensRoot.style.bottom = 0;
            _screensRoot.style.display = DisplayStyle.None;

            // Слой контента — сюда каталог строит выбранный экран (у экранов свой полноэкранный scrim).
            _screenContent = new VisualElement { name = "gm-dev-screen-content" };
            _screenContent.style.position = Position.Absolute;
            _screenContent.style.left = 0; _screenContent.style.top = 0;
            _screenContent.style.right = 0; _screenContent.style.bottom = 0;
            _screensRoot.Add(_screenContent);

            // Верхняя лента-переключатель экранов (поверх контента).
            var bar = new VisualElement();
            bar.AddToClassList("gm-dev-screens-bar");
            foreach (string id in UiPreviewCatalog.Ids)
            {
                if (id == "dev-picker") continue; // сам пикер здесь не нужен
                string screenId = id;
                var b = new Button(() => UiPreviewCatalog.Build(screenId, _screenContent)) { text = ScreenLabel(screenId) };
                b.AddToClassList("gm-dev-play");
                bar.Add(b);
            }
            _screensRoot.Add(bar);
            root.Add(_screensRoot);
#endif
        }

#if UNITY_EDITOR
        private static string ScreenLabel(string id) => id switch
        {
            "reward"      => "Награда",
            "event"       => "Ивент",
            "settings"    => "Настройки",
            "gallery"     => "Галерея",
            _              => id,
        };
#endif

        private void Toggle(DevView view)
        {
            _view = _view == view ? DevView.None : view;
            Apply();
        }

        private void Apply()
        {
            if (_overlay != null)
                _overlay.style.display = _view == DevView.Battles ? DisplayStyle.Flex : DisplayStyle.None;
            if (_screensRoot != null)
                _screensRoot.style.display = _view == DevView.Screens ? DisplayStyle.Flex : DisplayStyle.None;
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

    }
}
