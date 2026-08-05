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

            var folder = Root.Q<Button>("devlog-folder");
            if (folder != null) folder.clicked += OpenLogFolder;

            BuildChannelToggles(Root.Q<VisualElement>("devlog-channels"));

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

            // Каналы могли переключить командой, пока экран был закрыт: подсветку берём из состояния,
            // а не из памяти кнопок.
            RefreshChannelToggles();

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

        /// <summary>
        /// Полка каналов диагностики: включить их можно и командой, но в кооп-прогоне это надо делать
        /// НА ДВУХ машинах и каждый раз, а команду ещё и вспомнить.
        /// </summary>
        /// <remarks>
        /// Кнопки строятся по самому перечислению, а не перечисляются в разметке: список в UXML
        /// разъехался бы с <see cref="Core.Diagnostics.DiagChannel"/> на первом же новом канале, и
        /// разъехался бы молча — тумблера просто не было бы, а канал считался бы «неработающим».
        /// <para><b>«Всё» — не украшение:</b> ровно его и просят, когда прогон уже идёт, а причина
        /// неизвестна. Отдельная кнопка избавляет от пяти нажатий в тот момент, когда некогда.</para>
        /// </remarks>
        private void BuildChannelToggles(VisualElement shelf)
        {
            if (shelf == null) return;

            shelf.Clear();
            _channelButtons.Clear();

            AddChannelToggle(shelf, "всё", Core.Diagnostics.DiagChannel.Net);
            foreach (Core.Diagnostics.DiagChannel channel in SingleChannels)
                AddChannelToggle(shelf, ChannelLabel(channel), channel);

            RefreshChannelToggles();
        }

        private static readonly Core.Diagnostics.DiagChannel[] SingleChannels =
        {
            Core.Diagnostics.DiagChannel.Session,
            Core.Diagnostics.DiagChannel.Tape,
            Core.Diagnostics.DiagChannel.Follow,
            Core.Diagnostics.DiagChannel.Commands,
            Core.Diagnostics.DiagChannel.Ready,
        };

        private static string ChannelLabel(Core.Diagnostics.DiagChannel channel)
        {
            switch (channel)
            {
                case Core.Diagnostics.DiagChannel.Session:  return "сеанс";
                case Core.Diagnostics.DiagChannel.Tape:     return "лента";
                case Core.Diagnostics.DiagChannel.Follow:   return "где мы";
                case Core.Diagnostics.DiagChannel.Commands: return "команды";
                case Core.Diagnostics.DiagChannel.Ready:    return "готовность";
                default:                                    return channel.ToString().ToLowerInvariant();
            }
        }

        private readonly System.Collections.Generic.List<(Button Button, Core.Diagnostics.DiagChannel Channel)>
            _channelButtons = new System.Collections.Generic.List<(Button, Core.Diagnostics.DiagChannel)>();

        private void AddChannelToggle(VisualElement shelf, string label, Core.Diagnostics.DiagChannel channel)
        {
            var button = new Button { text = label };
            button.AddToClassList("gm-console__tool");
            button.clicked += () =>
            {
                Core.Diagnostics.Diag.Set(channel, !Core.Diagnostics.Diag.IsOn(channel));
                RefreshChannelToggles();
                _log?.Append(DevLogKind.Reply, $"диагностика: {Core.Diagnostics.Diag.Enabled}");
            };

            shelf.Add(button);
            _channelButtons.Add((button, channel));
        }

        /// <summary>Подсветить включённые. Читаем состояние, а не помним своё: командой его тоже меняют.</summary>
        private void RefreshChannelToggles()
        {
            for (int i = 0; i < _channelButtons.Count; i++)
            {
                (Button button, Core.Diagnostics.DiagChannel channel) = _channelButtons[i];
                button.EnableInClassList("gm-console__tool--active", Core.Diagnostics.Diag.IsOn(channel));
            }
        }

        /// <summary>
        /// Открыть папку архива прогонов. Ровно то, чего не хватало на живом разборе: путь печатался
        /// в лог, а дальше его надо было переписывать в проводник руками.
        /// </summary>
        private void OpenLogFolder()
        {
            try
            {
                string folder = Core.Diagnostics.SessionLogArchive.Folder;
                System.IO.Directory.CreateDirectory(folder);
                UnityEngine.Application.OpenURL("file:///" + folder.Replace('\\', '/'));
                _log?.Append(DevLogKind.Reply, $"папка логов: {folder}");
            }
            catch (System.Exception e)
            {
                _log?.Append(DevLogKind.Error, $"не открылась: {e.GetType().Name}: {e.Message}");
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
