using Guildmaster.Core.DevConsole;
using UnityEngine.UIElements;

namespace Guildmaster.UI.DevConsole
{
    /// <summary>
    /// Лог-консоль (<c>F2</c>): хвост сообщений движка без строки ввода.
    /// </summary>
    /// <remarks>
    /// <b>Отдельный экран, а не вкладка командной</b> (решение Макса 31.07). Причина не в верстке:
    /// в командной консоли идёт разговор «спросил — ответили», и поток `Debug.Log` из боя затапливает
    /// его за секунды — ответ на свою же команду приходится искать в чужих строках. Здесь наоборот:
    /// нужен непрерывный поток и ничего больше, поэтому ни ввода, ни палитры тут нет.
    /// </remarks>
    public sealed class DevLogScreen : UiScreen, IDevOverlayScreen
    {
        private readonly VisualTreeAsset _tree;
        private readonly DevConsoleLog _log;

        private ScrollView _list;
        private Label _status;
        private bool _scrollPending;

        public DevLogScreen(VisualTreeAsset tree, DevConsoleLog log)
        {
            _tree = tree;
            _log  = log;
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

            _list   = Root.Q<ScrollView>("devlog-list");
            _status = Root.Q<Label>("devlog-status");

            var clear = Root.Q<Button>("devlog-clear");
            if (clear != null) clear.clicked += () => _log?.Clear();

            var save = Root.Q<Button>("devlog-save");
            if (save != null) save.clicked += SaveToFile;

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (!_scrollPending) return;
                _scrollPending = false;
                _list?.schedule.Execute(ScrollToEnd);
            });

        }

        /// <inheritdoc />
        /// <remarks>
        /// Подписка живёт ровно столько, сколько экран показан, и потому стоит здесь, симметрично
        /// <see cref="OnExit"/>. В <c>Build</c> её держать нельзя: навигатор строит экран один раз за
        /// сессию (пока <c>Root == null</c>), а показывают его многократно — после первого закрытия
        /// список логов застывал бы на строках первого показа.
        /// </remarks>
        public override void OnEnter()
        {
            if (_log != null)
            {
                _log.Appended += OnAppended;
                _log.Cleared  += Rebuild;
            }

            Rebuild(); // заодно догоняем строки, набежавшие, пока экран был закрыт
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            if (_log == null) return;
            _log.Appended -= OnAppended;
            _log.Cleared  -= Rebuild;
        }

        /// <summary>
        /// Сложить хвост в файл рядом с сейвами. Полезно ровно тогда, когда лог уже уехал за 200 строк
        /// или игра вот-вот закроется, а разобраться надо потом.
        /// </summary>
        private void SaveToFile()
        {
            if (_log == null) return;

            try
            {
                string dir = System.IO.Path.Combine(Core.Persistence.GameDataPath.Root, "Logs");
                System.IO.Directory.CreateDirectory(dir);

                string path = System.IO.Path.Combine(dir, $"devlog-{System.DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
                var sb = new System.Text.StringBuilder();
                foreach (DevLogLine line in _log.Lines) sb.AppendLine($"[{line.Kind}] {line.Text}");
                System.IO.File.WriteAllText(path, sb.ToString());

                // Путь печатаем в тот же лог: искать его в чужой консоли — лишний шаг.
                _log.Append(DevLogKind.Reply, $"сохранено: {path}");
            }
            catch (System.Exception e)
            {
                _log.Append(DevLogKind.Error, $"не сохранилось: {e.GetType().Name}: {e.Message}");
            }
        }

        private void OnAppended(DevLogLine line)
        {
            AddLine(line);
            UpdateStatus();
            ScrollToEnd();
        }

        private void Rebuild()
        {
            if (_list == null) return;

            _list.Clear();
            if (_log != null)
                foreach (DevLogLine line in _log.Lines) AddLine(line);

            UpdateStatus();
            ScrollToEnd();
        }

        private void AddLine(DevLogLine line)
        {
            if (_list == null) return;

            var label = new Label(line.Text);
            label.AddToClassList("gm-console__line");
            label.AddToClassList(ModifierFor(line.Kind));
            _list.Add(label);
        }

        private void UpdateStatus()
        {
            if (_status == null) return;

            int count = _log?.Count ?? 0;
            _status.text = $"{count} из {DevConsoleLog.Capacity} строк\n" +
                           "хвост копится, пока лог открыт · старые строки вытесняются\n" +
                           "F1 команды · F3 бои · F2 закрывает";
        }

        // Та же готча, что у командной консоли: до привязки к панели ScrollTo бросает NRE, а вывод
        // наполняется в Build — до того, как навигатор добавит корень в слой.
        private void ScrollToEnd()
        {
            if (_list == null || _list.childCount == 0) return;

            if (_list.panel == null)
            {
                _scrollPending = true;
                return;
            }

            _list.ScrollTo(_list[_list.childCount - 1]);
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
    }
}
