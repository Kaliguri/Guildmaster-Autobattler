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
    public sealed class DevConsoleScreen : UiScreen
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

            if (_field != null)
            {
                _field.RegisterValueChangedCallback(OnTyped);
                _field.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            }

            RebuildLog();
            UpdateStatus();

            if (_log != null)
            {
                _log.Appended += OnLineAppended;
                _log.Cleared  += RebuildLog;
            }
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
            RefreshCompletion(evt.newValue);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit();
                    evt.StopPropagation();
                    break;

                case KeyCode.Tab:
                    AcceptCompletion();
                    evt.StopPropagation();
                    break;

                case KeyCode.UpArrow:
                    StepHistory(+1);
                    evt.StopPropagation();
                    break;

                case KeyCode.DownArrow:
                    StepHistory(-1);
                    evt.StopPropagation();
                    break;
            }
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

            if (!string.IsNullOrEmpty(result.Message))
                _log?.Append(result.IsError ? DevLogKind.Error : DevLogKind.Reply, result.Message);

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
                _ghost.text = !string.IsNullOrEmpty(completion) && completion.Length > head.Length
                    ? completion
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

        private void ScrollToEnd()
        {
            if (_logView == null || _logView.childCount == 0) return;
            _logView.ScrollTo(_logView[_logView.childCount - 1]);
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            int count = _registry?.Count ?? 0;
            _status.text = $"{count} команд · Tab дополняет · ↑↓ история";
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

        private static string FirstWord(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            int end = value.IndexOf(' ');
            return end < 0 ? value : value.Substring(0, end);
        }
    }
}
