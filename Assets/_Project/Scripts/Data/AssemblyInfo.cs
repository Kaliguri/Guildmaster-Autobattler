using System.Runtime.CompilerServices;

// Editor-инструменты слоя данных (id-lifecycle, Sync БД, миграции) обращаются к internal-членам
// editor-only API (напр. ContentDatabase.EditorSetEntries) — без публичного сеттера в рантайм-контракте.
[assembly: InternalsVisibleTo("Guildmaster.Data.Editor")]
