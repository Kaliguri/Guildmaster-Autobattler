using System.Collections.Generic;

namespace Guildmaster.Guild.Commands
{
    /// <summary>
    /// Append-only журнал команд забега в порядке их ПРИМЕНЕНИЯ, плюс память о том, что уже применено.
    /// <para><b>Состояние — проекция этого лога.</b> Отсюда бесплатно берутся реплей забега, аудит «кто
    /// передвинул юнита» и реконнект («снимок плюс хвост»). Порядок в логе — порядок применения, а не
    /// порядок клиентских часов: сортировкой конкурирующих интентов занимается хост, когда собирает их
    /// от нескольких игроков, и делать вид, что она уже произошла, значило бы записать в лог историю,
    /// которой не было.</para>
    /// </summary>
    public sealed class RunCommandLog
    {
        private readonly List<RunCommand> _entries = new List<RunCommand>(64);
        private readonly HashSet<(int, int)> _applied = new HashSet<(int, int)>();

        /// <summary>Сколько команд применено за забег.</summary>
        public int Count => _entries.Count;

        public RunCommand this[int index] => _entries[index];

        /// <summary>Команды в порядке применения — для реплея, аудита и отправки хвоста при реконнекте.</summary>
        public IReadOnlyList<RunCommand> Entries => _entries;

        /// <summary>
        /// Уже применялась ли команда с таким ключом. Единственное лекарство от дублей на стыке
        /// реконнекта: клиент, не получивший подтверждения, отправит её снова — и обязан получить
        /// «уже применено», а не второе списание золота.
        /// </summary>
        public bool WasApplied(in RunCommand command) => _applied.Contains(command.Key);

        /// <summary>
        /// Дописать команду как применённую. Зовёт только обработчик, и только ПОСЛЕ применения:
        /// запись вперёд применения означала бы, что лог знает о том, чего в состоянии нет.
        /// </summary>
        public void Append(in RunCommand command)
        {
            _entries.Add(command);
            _applied.Add(command.Key);
        }

        /// <summary>
        /// Забыть всё (новый забег, загрузка чужого забега). Ключи идемпотентности живут ровно столько,
        /// сколько сам забег: номера у нового забега начинаются заново, и память о старых отбрасывала бы
        /// его первые команды как дубли.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            _applied.Clear();
        }
    }
}
