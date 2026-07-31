using System.Collections.Generic;
using Guildmaster.Core.DevConsole;
using Guildmaster.Data.Definitions;
using Guildmaster.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Витрина боёв (<c>F3</c>): поиск по всему, что можно запустить, — энкаунтеры и пресеты из
    /// контент-БД плюс срезы китов. Набрал часть имени, стрелками выбрал, Enter — поехали.
    /// </summary>
    /// <remarks>
    /// <b>Витрина не запускает бой сама — она исполняет КОМАНДУ из реестра</b> (<c>battle</c>,
    /// <c>preset</c>, <c>kit</c>, …). Так у запуска остаётся один путь: то, что делает клик мышью, можно
    /// повторить руками в консоли на F1, а новая команда или новый бой в базе появляются здесь сами,
    /// без правки этого файла. Прежняя dev-панель делала обратное — держала собственный вызов
    /// <c>EncounterLoader</c>, и «запустить бой» существовало в двух несогласованных видах.
    /// <para>Экран живёт в DevTools, а не в слое UI, потому что знает про контент и команды; UI-слою о
    /// боях знать незачем.</para>
    /// </remarks>
    public sealed class DevBattleBrowserScreen : UiScreen, Guildmaster.UI.DevConsole.IDevOverlayScreen
    {
        /// <summary>
        /// Строка витрины. Первая колонка — ГОТОВАЯ КОМАНДА, а не голый id: по ней и ищут, и её же можно
        /// перенести в консоль на F1 как есть.
        /// </summary>
        private readonly struct Entry
        {
            public readonly string Command;  // «battle encounter.goblin_raid» — она же ключ поиска
            public readonly string Kind;     // «энкаунтер» / «пресет» / «срез»
            public readonly string Tier;     // Common / Elite / Special / — (у пресетов и срезов нет)
            public readonly string Hint;     // краткое описание боя

            public Entry(string command, string kind, string tier, string hint)
            {
                Command = command;
                Kind = kind;
                Tier = tier;
                Hint = hint;
            }
        }

        /// <summary>Колонка, по которой отсортирован список.</summary>
        private enum SortColumn { Command, Kind, Tier, Hint }

        private const int MaxRows = 40;

        private readonly VisualTreeAsset _tree;
        private readonly DevCommandRegistry _registry;
        private readonly IContentDatabase _content;

        private readonly List<Entry> _all = new List<Entry>();
        private readonly List<Entry> _shown = new List<Entry>();
        private readonly List<VisualElement> _rowViews = new List<VisualElement>();

        /// <summary>Ответ последней запущенной команды — показывается в подвале, а не только в логе.</summary>
        private string _lastResult = string.Empty;

        private ScrollView _list;
        private TextField _field;
        private Label _ghost;
        private Label _status;
        private int _selected;

        private readonly Dictionary<SortColumn, Button> _headCells = new Dictionary<SortColumn, Button>();
        private SortColumn _sortColumn = SortColumn.Command;
        private bool _sortDescending;

        public DevBattleBrowserScreen(VisualTreeAsset tree, DevCommandRegistry registry, IContentDatabase content)
        {
            _tree = tree;
            _registry = registry;
            _content = content;
        }

        /// <inheritdoc />
        public override ScreenKind Kind => ScreenKind.Modal;

        /// <inheritdoc />
        public override bool SuppressScrim => true;

        /// <inheritdoc />
        public override void Build(UiScreenContext ctx)
        {
            Root = _tree != null ? _tree.Instantiate() : new VisualElement();
            Root.style.flexGrow = 1f;

            _list   = Root.Q<ScrollView>("picker-list");
            _field  = Root.Q<TextField>("picker-field");
            _ghost  = Root.Q<Label>("picker-ghost");
            _status = Root.Q<Label>("picker-status");

            if (_field != null) _field.RegisterValueChangedCallback(_ => Refilter());

            // Шапка таблицы: подписи колонок и есть кнопки сортировки. Повторный клик по той же колонке
            // переворачивает порядок — привычный жест из любой таблицы, отдельных стрелок-кнопок не нужно.
            BindHead("head-command", SortColumn.Command);
            BindHead("head-kind",    SortColumn.Kind);
            BindHead("head-tier",    SortColumn.Tier);
            BindHead("head-hint",    SortColumn.Hint);

            var run = Root.Q<Button>("picker-run");
            if (run != null) run.clicked += Run;

            var refresh = Root.Q<Button>("picker-refresh");
            if (refresh != null) refresh.clicked += Refresh;

            // Как и в консоли, клавиши ловим на корне: иначе Tab и стрелки уходят в навигацию по фокусу.
            Root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            Root.RegisterCallback<NavigationMoveEvent>(evt => { evt.StopPropagation(); evt.PreventDefault(); },
                TrickleDown.TrickleDown);

            // Фокус ставим сами и через schedule — та же причина, что в консоли: к моменту
            // GetInitialFocus панель ещё не приняла элемент, и поле остаётся без фокуса.
            Root.RegisterCallback<AttachToPanelEvent>(_ => _field?.schedule.Execute(() => _field.Focus()));

            Collect();
            Refilter();
        }

        /// <inheritdoc />
        public override VisualElement GetInitialFocus() => _field;

        /// <summary>Перечитать список — состав боёв мог смениться (пересобрали контент-БД).</summary>
        public void Refresh()
        {
            Collect();
            Refilter();
        }

        // ── Сбор ──────────────────────────────────────────────────────────────────────────

        private void Collect()
        {
            _all.Clear();

            IReadOnlyList<EncounterData> encounters = _content?.All<EncounterData>();
            if (encounters != null)
                for (int i = 0; i < encounters.Count; i++)
                {
                    EncounterData e = encounters[i];
                    if (e == null) continue;
                    _all.Add(new Entry($"battle {e.Id}", "энкаунтер", e.Tier.ToString(), EnemySummary(e)));
                }

            IReadOnlyList<BattlePresetData> presets = _content?.All<BattlePresetData>();
            if (presets != null)
                for (int i = 0; i < presets.Count; i++)
                {
                    BattlePresetData p = presets[i];
                    if (p == null) continue;
                    _all.Add(new Entry($"preset {p.Id}", "пресет", "—", "враги плюс свой ростер"));
                }

            // Срезы китов — те же, что у команды kit: витрина показывает их рядом с боями, потому что
            // ищут их тем же движением («хочу посмотреть монаха»), а не по принадлежности к подсистеме.
            AddSlice("spearman",   "копейщик против кластера");
            AddSlice("shepherd",   "пастырь и раненые союзники");
            AddSlice("cryomancer", "криомант против кластера");
            AddSlice("defender",   "защитник под ударами");
            AddSlice("ranger",     "следопыт против кластера");
            AddSlice("assassin",   "убийца против болванчиков");
            AddSlice("monk",       "монах вихря против четверых");

            _all.Add(new Entry("mirror", "прочее", "—", "зеркальный отряд 4v4"));
            _all.Add(new Entry("crowd",  "прочее", "—", "плотный клубок — тест расталкивания"));
            _all.Add(new Entry("bones",  "прочее", "—", "дуэль скелетных дев-бойцов"));
        }

        private void AddSlice(string kit, string hint) => _all.Add(new Entry($"kit {kit}", "срез", "—", hint));

        // Считаем ФАКТИЧЕСКИХ врагов, а не строк расстановки: одна строка может нести Count копий, и
        // «врагов: 3» вместо «врагов: 9» врало бы ровно там, где витрину и открывают — при выборе боя.
        private static string EnemySummary(EncounterData e)
        {
            var units = e.Units;
            if (units == null || units.Count == 0) return "пусто";

            int total = 0;
            for (int i = 0; i < units.Count; i++) total += units[i].Count;

            return $"врагов: {total}";
        }

        // ── Поиск и показ ─────────────────────────────────────────────────────────────────

        private void Refilter()
        {
            string query = _field?.value;
            _shown.Clear();

            for (int i = 0; i < _all.Count; i++)
            {
                if (string.IsNullOrEmpty(query) ||
                    _all[i].Command.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    _all[i].Kind.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    _all[i].Hint.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _shown.Add(_all[i]);
            }

            _shown.Sort(CompareBySortColumn);

            if (_selected >= _shown.Count) _selected = _shown.Count - 1;
            if (_selected < 0) _selected = 0;

            RebuildRows();
            UpdateStatus();
        }

        private int CompareBySortColumn(Entry a, Entry b)
        {
            int result;
            switch (_sortColumn)
            {
                case SortColumn.Kind: result = string.CompareOrdinal(a.Kind, b.Kind); break;
                case SortColumn.Tier: result = string.CompareOrdinal(a.Tier, b.Tier); break;
                case SortColumn.Hint: result = string.CompareOrdinal(a.Hint, b.Hint); break;
                default:              result = 0; break;
            }

            // Внутри равных значений — всегда по команде: без вторичного ключа строки прыгали бы между
            // перерисовками, и выбранная под курсором уезжала бы сама.
            if (result == 0) result = string.CompareOrdinal(a.Command, b.Command);

            return _sortDescending ? -result : result;
        }

        private void BindHead(string elementName, SortColumn column)
        {
            var button = Root.Q<Button>(elementName);
            if (button == null) return;

            _headCells[column] = button;
            button.clicked += () => SortBy(column);
        }

        private void SortBy(SortColumn column)
        {
            if (_sortColumn == column) _sortDescending = !_sortDescending;
            else { _sortColumn = column; _sortDescending = false; }

            foreach (var pair in _headCells)
            {
                bool active = pair.Key == _sortColumn;
                pair.Value.EnableInClassList("gm-picker__head-cell--sorted", active);
                pair.Value.text = HeadLabel(pair.Key) + (active ? (_sortDescending ? "  ↓" : "  ↑") : string.Empty);
            }

            Refilter();
        }

        private static string HeadLabel(SortColumn column)
        {
            switch (column)
            {
                case SortColumn.Kind: return "тип";
                case SortColumn.Tier: return "сложность";
                case SortColumn.Hint: return "описание";
                default:              return "команда";
            }
        }

        /// <summary>Перекрасить выделение, не пересобирая строки (см. про двойной клик выше).</summary>
        private void UpdateSelectionClasses()
        {
            for (int i = 0; i < _rowViews.Count; i++)
                _rowViews[i].EnableInClassList("gm-picker__row--selected", i == _selected);
        }

        private void RebuildRows()
        {
            if (_list == null) return;
            _list.Clear();
            _rowViews.Clear();

            int shown = _shown.Count < MaxRows ? _shown.Count : MaxRows;
            for (int i = 0; i < shown; i++)
            {
                Entry entry = _shown[i];
                int index = i;

                var row = new VisualElement();
                row.AddToClassList("gm-picker__row");
                if (i == _selected) row.AddToClassList("gm-picker__row--selected");

                row.Add(Cell(entry.Command, "gm-picker__cell--command"));
                row.Add(Cell(entry.Kind,    "gm-picker__cell--kind"));
                row.Add(Cell(entry.Tier,    "gm-picker__cell--tier"));
                row.Add(Cell(entry.Hint,    "gm-picker__cell--hint"));

                // Один клик выбирает, двойной запускает. ВАЖНО: выбор НЕ пересобирает список — иначе
                // второй клик приходит уже в новый элемент, у которого clickCount снова единица, и
                // двойной клик не случается никогда (ровно это и было).
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    _selected = index;
                    UpdateSelectionClasses();
                    if (evt.clickCount >= 2) Run();
                });

                _rowViews.Add(row);
                _list.Add(row);
            }

            if (_shown.Count > shown)
            {
                var more = new Label($"…и ещё {_shown.Count - shown} — уточни запрос");
                more.AddToClassList("gm-picker__cell--hint");
                _list.Add(more);
            }

            // Подсказка-призрак дописывает команду выбранной строки к набранному, как в консоли.
            if (_ghost != null)
            {
                string typed = _field?.value ?? string.Empty;
                bool canGhost = _shown.Count > 0 && typed.Length > 0 &&
                                _shown[_selected].Command.StartsWith(typed, System.StringComparison.OrdinalIgnoreCase);
                _ghost.text = canGhost
                    ? new string(' ', typed.Length) + _shown[_selected].Command.Substring(typed.Length)
                    : string.Empty;
            }
        }

        private void UpdateStatus()
        {
            if (_status == null) return;

            // Три строки подвала: что нашли, чем управлять, чем кончился последний запуск.
            string tail = string.IsNullOrEmpty(_lastResult) ? "запуска ещё не было" : _lastResult;
            _status.text = $"найдено {_shown.Count} из {_all.Count}\n" +
                           $"↑↓ выбор · Enter или двойной клик — запуск · Tab дописать · F3 закрыть\n" +
                           tail;
        }

        // ── Клавиши ───────────────────────────────────────────────────────────────────────

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Run();
                    Consume(evt);
                    break;

                case KeyCode.UpArrow:
                    Move(-1);
                    Consume(evt);
                    break;

                case KeyCode.DownArrow:
                    Move(+1);
                    Consume(evt);
                    break;

                case KeyCode.Tab:
                    // Tab дописывает id выбранного — дальше можно уточнить аргументы прямо в консоли.
                    if (_shown.Count > 0 && _field != null)
                    {
                        _field.SetValueWithoutNotify(_shown[_selected].Command);
                        _field.cursorIndex = _field.value.Length;
                        _field.selectIndex = _field.value.Length;
                        Refilter();
                    }
                    Consume(evt);
                    break;
            }
        }

        private void Move(int delta)
        {
            if (_shown.Count == 0) return;

            _selected += delta;
            if (_selected < 0) _selected = _shown.Count - 1;
            if (_selected >= _shown.Count) _selected = 0;

            RebuildRows();
        }

        private void Run()
        {
            if (_registry == null || _shown.Count == 0) return;

            string command = _shown[_selected].Command;
            DevCommandResult result = _registry.Execute(command);

            // Ответ показываем ЗДЕСЬ, в подвале. Раньше он уходил только в Debug.Log — то есть в лог на
            // F2, которого в этот момент на экране нет: со стороны выглядело как «Enter не работает»,
            // хотя команда честно отвечала «боевой скоуп не найден».
            _lastResult = string.IsNullOrEmpty(result.Message)
                ? $"{command} — готово"
                : $"{command} — {result.Message}";

            if (result.IsError) Debug.LogWarning($"[DevBattleBrowser] - {_lastResult}");
            else Debug.Log($"[DevBattleBrowser] - {_lastResult}");

            UpdateStatus();
        }

        /// <summary>Ячейка строки: класс колонки задаёт ширину, ту же, что у подписи в шапке.</summary>
        private static Label Cell(string text, string columnClass)
        {
            var label = new Label(text);
            label.AddToClassList(columnClass);
            return label;
        }

        private static void Consume(EventBase evt)
        {
            evt.StopPropagation();
            evt.PreventDefault();
        }
    }
}
