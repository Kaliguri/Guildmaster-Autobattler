using System;
using System.Collections.Generic;

namespace Guildmaster.Core.DevConsole
{
    /// <summary>Чем кончился разбор и вызов строки.</summary>
    public enum DevCommandStatus
    {
        /// <summary>Команда выполнилась.</summary>
        Ok,

        /// <summary>Пустая строка — реагировать не на что.</summary>
        Empty,

        /// <summary>Такого имени в реестре нет.</summary>
        UnknownCommand,

        /// <summary>Аргументы не подошли: не хватает, лишние или не разобрались.</summary>
        BadArguments,

        /// <summary>Тело команды бросило исключение.</summary>
        Failed,
    }

    /// <summary>Результат вызова: статус плюс готовая к печати строка (может быть пустой при <see cref="DevCommandStatus.Ok"/>).</summary>
    public readonly struct DevCommandResult
    {
        public readonly DevCommandStatus Status;

        /// <summary>Ответ команды или текст ошибки. Пусто — печатать нечего.</summary>
        public readonly string Message;

        public DevCommandResult(DevCommandStatus status, string message = null)
        {
            Status  = status;
            Message = message;
        }

        public bool IsError => Status != DevCommandStatus.Ok && Status != DevCommandStatus.Empty;
    }

    /// <summary>
    /// Реестр dev-команд: имя → команда, разбор строки и вызов. Модули регистрируют свои команды сами
    /// (<see cref="Register"/>), поэтому реестр не знает ни об одной подсистеме и живёт в тестах без сцены.
    /// </summary>
    /// <remarks>
    /// <b>Почему без атрибутов и рефлексии</b> (так делает QFSW, и это его главное удобство): сканирование
    /// сборок даёт «команды сами находятся», но платит стартовой ценой, ломается на IL2CPP-стриппинге и,
    /// главное, прячет зависимости — метод с атрибутом обязан сам добыть себе сервисы, откуда в проекте и
    /// берутся статические ходы к синглтонам. Явная регистрация в модуле, получившем зависимости через
    /// VContainer, оставляет граф видимым.
    /// <para><b>Дубль имени — исключение, а не «последний победил»:</b> две команды с одним именем это
    /// опечатка при копировании модуля, и тихое замещение означало бы, что часть команд молча перестала
    /// работать (политика фолбэков, §5 code-standards: наше авторство = громкий отказ).</para>
    /// <para>Регистр имени не значим: печатать <c>gm_arena_swap</c> в спешке с Caps Lock — обычное дело.</para>
    /// </remarks>
    public sealed class DevCommandRegistry
    {
        private readonly Dictionary<string, DevCommand> _byName =
            new Dictionary<string, DevCommand>(StringComparer.OrdinalIgnoreCase);

        private readonly List<DevCommand> _sorted = new List<DevCommand>();
        private readonly List<string> _tokens = new List<string>();

        private bool _sortedDirty;

        /// <summary>Все команды по алфавиту. Порядок стабилен — от него зависят список и автодополнение.</summary>
        public IReadOnlyList<DevCommand> All
        {
            get
            {
                if (_sortedDirty)
                {
                    _sorted.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    _sortedDirty = false;
                }
                return _sorted;
            }
        }

        public int Count => _sorted.Count;

        /// <summary>Зарегистрировать команду. Имя уже занято — <see cref="InvalidOperationException"/>.</summary>
        public void Register(DevCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (string.IsNullOrWhiteSpace(command.Name))
                throw new ArgumentException("У команды пустое имя", nameof(command));

            if (_byName.ContainsKey(command.Name))
                throw new InvalidOperationException($"Команда «{command.Name}» уже зарегистрирована");

            _byName.Add(command.Name, command);
            _sorted.Add(command);
            _sortedDirty = true;
        }

        /// <summary>Короткая форма регистрации.</summary>
        public void Register(string name, string summary, DevCommandHandler handler, params DevParam[] parameters) =>
            Register(new DevCommand(name, summary, handler, parameters));

        /// <summary>
        /// Снять команду с регистрации. Нужно модулям, живущим короче консоли: боевые команды уходят вместе
        /// со своим скоупом, иначе их вызов после смены сцены попал бы в мёртвые зависимости.
        /// </summary>
        public bool Unregister(string name)
        {
            if (name == null || !_byName.TryGetValue(name, out DevCommand command)) return false;

            _byName.Remove(name);
            _sorted.Remove(command);
            _sortedDirty = true;
            return true;
        }

        public bool TryGet(string name, out DevCommand command)
        {
            if (name == null)
            {
                command = null;
                return false;
            }
            return _byName.TryGetValue(name, out command);
        }

        /// <summary>
        /// Команды, чьё имя начинается с <paramref name="prefix"/>, дописанные в <paramref name="result"/>
        /// в алфавитном порядке. Пустой префикс даёт все. Возвращает число совпадений.
        /// </summary>
        public int Match(string prefix, List<DevCommand> result)
        {
            if (result == null) return 0;
            result.Clear();

            IReadOnlyList<DevCommand> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.IsNullOrEmpty(prefix) ||
                    all[i].Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(all[i]);
            }

            return result.Count;
        }

        /// <summary>
        /// Самое длинное имя-продолжение, общее для всех совпадений с <paramref name="prefix"/>, или сам
        /// префикс, если общего продолжения нет. Это то, что дописывает Tab: <c>gm_sep</c> при пяти
        /// командах <c>gm_sep_*</c> дорастает до <c>gm_sep_</c>, а не прыгает на первую из них.
        /// </summary>
        public string CommonPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return prefix;

            var matches = new List<DevCommand>();
            if (Match(prefix, matches) == 0) return prefix;

            string common = matches[0].Name;
            for (int i = 1; i < matches.Count; i++)
            {
                string name = matches[i].Name;
                int max = Math.Min(common.Length, name.Length);
                int len = 0;
                while (len < max && char.ToLowerInvariant(common[len]) == char.ToLowerInvariant(name[len])) len++;
                common = common.Substring(0, len);
            }

            return common.Length >= prefix.Length ? common : prefix;
        }

        /// <summary>
        /// Разобрать строку и выполнить команду. Исключения тела не выпускаются наружу: консоль обязана
        /// пережить любую дев-команду, поэтому падение превращается в строку вывода.
        /// </summary>
        public DevCommandResult Execute(string line)
        {
            if (DevCommandLine.Tokenize(line, _tokens) == 0)
                return new DevCommandResult(DevCommandStatus.Empty);

            string name = _tokens[0];
            if (!TryGet(name, out DevCommand command))
                return new DevCommandResult(DevCommandStatus.UnknownCommand,
                    $"неизвестная команда «{name}»");

            var args = new DevArgs(_tokens);

            if (args.Count < command.RequiredCount)
                return new DevCommandResult(DevCommandStatus.BadArguments,
                    $"мало аргументов. Форма: {command.Usage}");

            if (args.Count > command.Params.Count)
                return new DevCommandResult(DevCommandStatus.BadArguments,
                    $"лишние аргументы. Форма: {command.Usage}");

            try
            {
                string reply = command.Handler?.Invoke(args);
                return new DevCommandResult(DevCommandStatus.Ok, reply);
            }
            catch (DevArgException e)
            {
                return new DevCommandResult(DevCommandStatus.BadArguments,
                    $"{e.Message}. Форма: {command.Usage}");
            }
            catch (Exception e)
            {
                return new DevCommandResult(DevCommandStatus.Failed,
                    $"{command.Name} упала: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
