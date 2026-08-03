using System.Collections.Generic;
using Guildmaster.Core.DevConsole;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.DevConsole
{
    /// <summary>
    /// Экран dev-консоли: полка сверху с выводом, палитрой совпадений и строкой ввода. Первый экран,
    /// построенный на навигаторе с нуля (Трек К UI-реворка), — поэтому здесь нет ни одного обращения к
    /// видимости или вводу: тип экрана всё решает за него.
    /// </summary>
    /// <remarks>
    /// <b>Modal без затемнения.</b> Тип <see cref="ScreenKind.Modal"/> нужен ради глушения геймплейного
    /// ввода (клавиатура принадлежит консоли), а <see cref="SuppressScrim"/> снимает scrim: консоль
    /// открывают, чтобы РАЗГЛЯДЫВАТЬ кадр под ней, и затемнять его — работать против собственной задачи.
    /// <para><b>Ghost-подсказка вместо цикла по Tab:</b> остаток дополненного имени печатается под полем
    /// приглушённым, и Tab его принимает. Точное совпадение символов держится на моноширинном шрифте —
    /// на пропорциональном ghost «уехал» бы вправо на первой же букве.</para>
    /// </remarks>
    public sealed class DevConsoleScreen : UiScreen, IDevOverlayScreen
    {
        private const string LineClass = "gm-console__line";
        private const string HiddenPaletteClass = "gm-console__palette--hidden";

        /// <summary>Сколько совпадений показывает палитра. Больше — это уже не выбор, а простыня.</summary>
        private const int MaxPaletteHits = 8;

        private readonly VisualTreeAsset _tree;
        private readonly DevCommandRegistry _registry;
        private readonly DevConsoleLog _log;

        private readonly List<DevCommand> _hits = new List<DevCommand>();
        private readonly List<string> _history = new List<string>();

        private ScrollView _logView;
        private ScrollView _palette;
        private TextField _field;
        private Label _ghost;
        private Label _status;

        /// <summary>-1 = ввод свежий, не листаем историю.</summary>
        private int _historyCursor = -1;

        private int _selectedHit;

        /// <summary>Вывод наполнили до привязки к панели — прокрутить, как только панель появится.</summary>
        private bool _scrollPending;

        public DevConsoleScreen(VisualTreeAsset tree, DevCommandRegistry registry, DevConsoleLog log)
        {
            _tree     = tree;
            _registry = registry;
            _log      = log;
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

            _logView = Root.Q<ScrollView>("console-log");
            _palette = Root.Q<ScrollView>("console-palette");
            _field   = Root.Q<TextField>("console-field");
            _ghost   = Root.Q<Label>("console-ghost");
            _status  = Root.Q<Label>("console-status");

            if (_field != null) _field.RegisterValueChangedCallback(OnTyped);

            var help = Root.Q<Button>("console-help");
            if (help != null) help.clicked += PrintHelp;

            var all = Root.Q<Button>("console-all");
            if (all != null) all.clicked += PrintAllByGroup;

            var clear = Root.Q<Button>("console-clear");
            if (clear != null) clear.clicked += () => { _log?.Clear(); PrintWelcome(); };

            // Клавиши ловим на КОРНЕ экрана в trickle-фазе, а не на поле. Причина: Tab и стрелки в UITK
            // сначала превращаются в навигацию по фокусу (NavigationMoveEvent) и до обработчика на поле
            // либо не доходят, либо доходят уже после того, как фокус уехал на соседнюю кнопку — а
            // следующий Enter «нажимает» её. Именно так консоль запускала то, чего у неё не просили.
            Root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            Root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            Root.RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                Submit();
                Consume(evt);
            }, TrickleDown.TrickleDown);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                // Фокус ставим САМИ и через schedule. GetInitialFocus навигатора срабатывает раньше, чем
                // панель успевает принять элемент, и поле остаётся без фокуса — тогда клавиши уходят мимо
                // экрана, и Enter «не работает», хотя обработчик на месте.
                _field?.schedule.Execute(() => _field.Focus());

                if (!_scrollPending) return;
                _scrollPending = false;
                _logView?.schedule.Execute(ScrollToEnd);
            });

            UpdateStatus();

            if (_log == null || _log.Count == 0) PrintWelcome();
        }

        /// <inheritdoc />
        /// <remarks>
        /// Подписка живёт ровно столько, сколько экран показан, и потому стоит здесь, симметрично
        /// <see cref="OnExit"/>. В <c>Build</c> её держать нельзя: навигатор строит экран, только пока
        /// <c>Root == null</c>, то есть один раз за сессию, а закрывают и открывают консоль много раз —
        /// после первого закрытия она осталась бы без подписок и молчала бы на любую команду.
        /// </remarks>
        public override void OnEnter()
        {
            if (_log != null)
            {
                _log.Appended += OnLineAppended;
                _log.Cleared  += RebuildLog;
            }

            RebuildLog(); // заодно догоняем строки, набежавшие, пока экран был закрыт
        }

        /// <summary>
        /// Первый экран консоли: что вообще можно набрать. Пустая полка с мигающим курсором честно
        /// говорит «я готова» и совершенно не говорит, что делать дальше, — а по памяти команды помнит
        /// только тот, кто их писал.
        /// </summary>
        private void PrintWelcome()
        {
            if (_log == null) return;

            // Подсказки по клавишам ЗДЕСЬ не печатаем: они живут в подвале, на виду постоянно, и в теле
            // лога уезжали бы вверх с первой же командой (замечание Макса 01.08).
            _log.Append(DevLogKind.Reply, $"Консоль команд. Их сейчас {_registry?.Count ?? 0}.");
            _log.Append(DevLogKind.Info,  "  battles          что можно запустить (или F3 — витрина с поиском)");
            _log.Append(DevLogKind.Info,  "  kit monk         срез одного кита против болванчиков");
            _log.Append(DevLogKind.Info,  "  spawn 4          тест-бой четверо на четверо");
            _log.Append(DevLogKind.Info,  "  restart · win    перезапустить бой · закончить победой");
            _log.Append(DevLogKind.Info,  "  sep · fx · arena группы: расталкивание, эффекты, облик арены");
        }

        /// <summary>
        /// Все команды, СГРУППИРОВАННЫЕ по подсистеме, с заголовком группы: что и для чего. Плоский
        /// алфавитный список из тридцати пяти строк читается как телефонная книга — по нему нельзя
        /// понять, что вообще умеет консоль.
        /// </summary>
        private void PrintAllByGroup()
        {
            if (_log == null || _registry == null) return;

            IReadOnlyList<DevCommand> all = _registry.All;
            var groups = new List<string>();
            var byGroup = new Dictionary<string, List<DevCommand>>();

            for (int i = 0; i < all.Count; i++)
            {
                string group = GroupOf(all[i].Name);
                if (!byGroup.TryGetValue(group, out List<DevCommand> list))
                {
                    list = new List<DevCommand>();
                    byGroup.Add(group, list);
                    groups.Add(group);
                }
                list.Add(all[i]);
            }

            _log.Append(DevLogKind.Reply, $"Все команды ({all.Count}) по группам:");
            for (int g = 0; g < groups.Count; g++)
            {
                _log.Append(DevLogKind.Echo, GroupTitle(groups[g]));
                List<DevCommand> list = byGroup[groups[g]];
                for (int i = 0; i < list.Count; i++)
                    _log.Append(DevLogKind.Info, $"    {list[i].Usage,-34} {list[i].Summary}");
            }
        }

        // Группа = первое слово имени до подчёркивания: sep_radius и sep_ally про одно, и в списке им
        // место рядом. Команды без подчёркивания собираются в «прочее» — заводить им по группе на штуку
        // значило бы получить двадцать заголовков по одной строке.
        private static string GroupOf(string name)
        {
            int at = name.IndexOf('_');
            string head = at > 0 ? name.Substring(0, at) : name;
            switch (head)
            {
                case "sep": case "fx": case "arena": case "map": return head;
                case "battle": case "battles": case "preset": case "kit": return "бои";
                default: return "прочее";
            }
        }

        private static string GroupTitle(string group)
        {
            switch (group)
            {
                case "бои":   return "  бои — что запустить";
                case "sep":   return "  sep — расталкивание тел";
                case "fx":    return "  fx — визуальные эффекты";
                case "arena": return "  arena — облик арены";
                case "map":   return "  map — карта акта";
                default:      return "  прочее — состояние боя и показа";
            }
        }

        /// <summary>Весь список команд с формой вызова — по кнопке «справка».</summary>
        private void PrintHelp()
        {
            if (_log == null || _registry == null) return;

            _log.Append(DevLogKind.Reply, $"Все команды ({_registry.Count}):");
            IReadOnlyList<DevCommand> all = _registry.All;
            for (int i = 0; i < all.Count; i++)
                _log.Append(DevLogKind.Info, $"  {all[i].Usage}   — {all[i].Summary}");
        }

        /// <inheritdoc />
        public override VisualElement GetInitialFocus() => _field;

        /// <inheritdoc />
        public override void OnExit()
        {
            if (_log != null)
            {
                _log.Appended -= OnLineAppended;
                _log.Cleared  -= RebuildLog;
            }
        }

        // ── Ввод ──────────────────────────────────────────────────────────────────────────

        private void OnTyped(ChangeEvent<string> evt)
        {
            _historyCursor = -1;

            // Раскладка ОС нас не касается: набранное в русской раскладке переводится в латиницу по
            // позиции клавиши, потому что все имена команд латинские (решение Макса 31.07).
            // Заодно выбрасываем сам символ клавиши-тогла: клавиша открывает консоль, а её литера
            // попадала в поле первой же буквой ввода.
            string latin = StripToggleChars(KeyboardLayoutMap.ToLatin(evt.newValue));
            if (!string.Equals(latin, evt.newValue) && _field != null)
            {
                int caret = _field.cursorIndex;
                _field.SetValueWithoutNotify(latin);
                _field.cursorIndex = caret;
                _field.selectIndex = caret;
            }

            RefreshCompletion(latin);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit();
                    Consume(evt);
                    break;

                case KeyCode.Tab:
                    AcceptCompletion();
                    Consume(evt);
                    break;

                case KeyCode.UpArrow:
                    StepHistory(+1);
                    Consume(evt);
                    break;

                case KeyCode.DownArrow:
                    StepHistory(-1);
                    Consume(evt);
                    break;
            }
        }

        // Tab и стрелки внутри консоли принадлежат ЕЙ, а не навигации по фокусу: уехавший фокус тут
        // означает, что следующий Enter нажмёт постороннюю кнопку.
        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.direction == NavigationMoveEvent.Direction.Next ||
                evt.direction == NavigationMoveEvent.Direction.Previous ||
                evt.direction == NavigationMoveEvent.Direction.Up ||
                evt.direction == NavigationMoveEvent.Direction.Down)
            {
                Consume(evt);
            }
        }

        /// <summary>
        /// Гасит событие целиком: и дальнейшее распространение, и навигацию по умолчанию.
        /// </summary>
        /// <remarks>
        /// Второе делал <c>PreventDefault</c>, объявленный устаревшим. Замена ему не одна:
        /// распространение останавливает <c>StopPropagation</c>, а навигацию по фокусу отменяет
        /// только <c>focusController.IgnoreEvent</c> — без него фокус всё равно уедет на соседний
        /// элемент, и следующий Enter нажмёт постороннюю кнопку.
        /// </remarks>
        private void Consume(EventBase evt)
        {
            evt.StopPropagation();
            Root?.focusController?.IgnoreEvent(evt);
        }

        private void Submit()
        {
            string line = _field?.value;
            if (string.IsNullOrWhiteSpace(line)) return;

            _log?.Append(DevLogKind.Echo, "> " + line);

            // История помнит только НЕПОВТОРЯЮЩИЕСЯ подряд строки: «↑» после трёх одинаковых вызовов
            // должно поднимать предыдущую КОМАНДУ, а не три её копии.
            if (_history.Count == 0 || _history[_history.Count - 1] != line) _history.Add(line);
            _historyCursor = -1;

            DevCommandResult result = _registry != null
                ? _registry.Execute(line)
                : new DevCommandResult(DevCommandStatus.Failed, "реестр команд не подключён");

            // Молчаливых команд не бывает: без ответа человек не понимает, дошёл ли ввод вообще.
            // Команда, которой нечего сказать, всё равно отчитывается «готово».
            if (!string.IsNullOrEmpty(result.Message))
                _log?.Append(result.IsError ? DevLogKind.Error : DevLogKind.Reply, result.Message);
            else if (result.Status == DevCommandStatus.Ok)
                _log?.Append(DevLogKind.Reply, "готово");

            SetFieldValue(string.Empty);
            UpdateStatus();
        }

        private void StepHistory(int direction)
        {
            if (_history.Count == 0) return;

            int cursor = _historyCursor + direction;
            if (cursor < -1) cursor = -1;
            if (cursor > _history.Count - 1) cursor = _history.Count - 1;

            _historyCursor = cursor;
            SetFieldValue(cursor < 0 ? string.Empty : _history[_history.Count - 1 - cursor]);
        }

        // ── Автодополнение ────────────────────────────────────────────────────────────────

        private void RefreshCompletion(string typed)
        {
            if (_registry == null) return;

            string head = FirstWord(typed);
            bool typingName = !string.IsNullOrEmpty(head) && head.Length == typed.Length;

            _selectedHit = 0;
            int found = typingName ? _registry.Match(head, _hits) : 0;

            // Ghost показывает ТОЛЬКО пока набирается имя: дописывать что-то к аргументам консоль не может,
            // а висящий призрак читался бы как обещание, которого она не выполнит.
            if (_ghost != null)
            {
                string completion = typingName ? _registry.CommonPrefix(head) : null;
                bool hasTail = !string.IsNullOrEmpty(completion) && completion.Length > head.Length;

                // Печатаем ТОЛЬКО хвост, отодвинув его пробелами на длину набранного. Целая строка под
                // полем давала наложение двух текстов — символы совпадали, и ввод выглядел полужирным.
                // Отступ пробелами точен лишь на моноширинном шрифте, и это ещё одна причина, по которой
                // он у консоли не вкусовщина.
                _ghost.text = hasTail
                    ? new string(' ', head.Length) + completion.Substring(head.Length)
                    : string.Empty;
            }

            RebuildPalette(found);
        }

        private void AcceptCompletion()
        {
            if (_registry == null || _field == null) return;

            string typed = _field.value ?? string.Empty;
            string head = FirstWord(typed);
            if (string.IsNullOrEmpty(head) || head.Length != typed.Length) return;

            string completion = _registry.CommonPrefix(head);
            if (completion.Length > head.Length) SetFieldValue(completion);
        }

        private void RebuildPalette(int found)
        {
            if (_palette == null) return;

            _palette.Clear();

            // Единственное совпадение уже дорисовано ghost'ом — палитра из одной строки только шумит.
            bool show = found > 1;
            _palette.EnableInClassList(HiddenPaletteClass, !show);
            if (!show) return;

            int shown = found < MaxPaletteHits ? found : MaxPaletteHits;
            for (int i = 0; i < shown; i++)
            {
                DevCommand command = _hits[i];

                var row = new VisualElement();
                row.AddToClassList("gm-console__hit");
                if (i == _selectedHit) row.AddToClassList("gm-console__hit--selected");

                var name = new Label(command.Usage);
                name.AddToClassList("gm-console__hit-name");

                var summary = new Label(command.Summary);
                summary.AddToClassList("gm-console__hit-summary");

                row.Add(name);
                row.Add(summary);

                // Клик мышью подставляет имя в поле (а не запускает сразу): у команд бывают аргументы,
                // и «выполнить по клику» отняло бы возможность их дописать.
                string commandName = command.Name;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    SetFieldValue(command.Params.Count > 0 ? commandName + " " : commandName);
                    _field?.Focus();
                });

                _palette.Add(row);
            }

            if (found > shown)
            {
                var more = new Label($"…и ещё {found - shown}");
                more.AddToClassList("gm-console__hit-summary");
                _palette.Add(more);
            }
        }

        // ── Вывод ─────────────────────────────────────────────────────────────────────────

        private void OnLineAppended(DevLogLine line)
        {
            AddLineView(line);
            ScrollToEnd();
        }

        private void RebuildLog()
        {
            if (_logView == null) return;

            _logView.Clear();
            if (_log == null) return;

            foreach (DevLogLine line in _log.Lines) AddLineView(line);
            ScrollToEnd();
        }

        private void AddLineView(DevLogLine line)
        {
            if (_logView == null) return;

            var label = new Label(line.Text);
            label.AddToClassList(LineClass);
            label.AddToClassList(ModifierFor(line.Kind));
            _logView.Add(label);
        }

        /// <remarks>
        /// <b>До привязки к панели <c>ScrollTo</c> бросает NRE</b> (внутри он спрашивает у панели, можно ли
        /// скроллить сейчас), а <see cref="Build"/> наполняет вывод ДО того, как навигатор добавит корень в
        /// слой. Поэтому прокрутка откладывается до аттача и идёт через <c>schedule</c>: на самом аттаче
        /// геометрия ещё не посчитана, и скролл ушёл бы в ноль.
        /// </remarks>
        private void ScrollToEnd()
        {
            if (_logView == null || _logView.childCount == 0) return;

            if (_logView.panel == null)
            {
                _scrollPending = true;
                return;
            }

            _logView.ScrollTo(_logView[_logView.childCount - 1]);
        }

        private void UpdateStatus()
        {
            if (_status == null) return;

            // Три строки подвала — постоянная шпаргалка: сколько команд, чем управлять, где остальное.
            int count = _registry?.Count ?? 0;
            _status.text = $"{count} команд · история: {_history.Count}\n" +
                           "Tab дополняет · ↑↓ история · Enter выполняет\n" +
                           "«все команды» — по группам · F2 лог движка · F3 бои";
        }

        private void SetFieldValue(string value)
        {
            if (_field == null) return;

            // Без уведомления: обработчик ввода сбрасывает курсор истории, и приём подсказки или шаг по
            // истории тут же затирали бы то, ради чего их и вызвали.
            _field.SetValueWithoutNotify(value ?? string.Empty);
            _field.cursorIndex = _field.value.Length;
            _field.selectIndex = _field.value.Length;
            RefreshCompletion(_field.value);
        }

        private static string ModifierFor(DevLogKind kind)
        {
            switch (kind)
            {
                case DevLogKind.Echo:  return "gm-console__line--echo";
                case DevLogKind.Reply: return "gm-console__line--reply";
                case DevLogKind.Warn:  return "gm-console__line--warn";
                case DevLogKind.Error: return "gm-console__line--error";
                default:               return "gm-console__line--info";
            }
        }

        /// <summary>Убрать символы клавиш-тоглов, которые ОС успевает напечатать в поле при открытии.</summary>
        private static string StripToggleChars(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOf('`') < 0 && value.IndexOf('~') < 0) return value;

            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
                if (value[i] != '`' && value[i] != '~') sb.Append(value[i]);
            return sb.ToString();
        }

        private static string FirstWord(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            int end = value.IndexOf(' ');
            return end < 0 ? value : value.Substring(0, end);
        }
    }
}
