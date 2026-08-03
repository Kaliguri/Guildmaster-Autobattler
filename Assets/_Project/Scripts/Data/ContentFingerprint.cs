using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Data
{
    /// <summary>
    /// Отпечаток контента и сборки — то, чем два клиента сверяются ДО того, как начнут играть вместе.
    /// <para><b>Почему это обязательно, а не страховка.</b> У нас data-driven контент на строковых id,
    /// публичный репозиторий и живой поток правок в SO, а по сети едут именно id (в чанке боевой ленты
    /// определения эффектов и способностей — строки, потому что ссылку на ассет передать нельзя).
    /// Неизвестный id на приёме роняет ПОКАЗ, а не «слегка расходит картинку». Прямое внешнее
    /// свидетельство: у кооп-мода для Slay the Spire десинки вызывались разными контент-модами у
    /// игроков, а на одинаковых сетапах всё работало.</para>
    /// <para><b>Хеш и версия — вдвоём, потому что порознь каждый пропускает свой класс поломки:</b> хеш
    /// ловит расхождение контента при одинаковом коде, версия сборки — расхождение кода при одинаковом
    /// контенте. NGO сверяет свой <c>NetworkConfig</c>, но про наш контент он не знает ничего.</para>
    /// </summary>
    public readonly struct ContentFingerprint
    {
        /// <summary>Хеш набора id всего контента.</summary>
        public readonly ulong ContentHash;

        /// <summary>Сколько определений в базе. Дешёвая подсказка человеку в тексте отказа.</summary>
        public readonly int ContentCount;

        /// <summary>Версия контент-схемы (<see cref="ContentDatabase.SchemaVersion"/>).</summary>
        public readonly int SchemaVersion;

        /// <summary>Версия сборки (<c>Application.version</c>).</summary>
        public readonly string GameVersion;

        public ContentFingerprint(ulong contentHash, int contentCount, int schemaVersion, string gameVersion)
        {
            ContentHash   = contentHash;
            ContentCount  = contentCount;
            SchemaVersion = schemaVersion;
            GameVersion   = gameVersion ?? string.Empty;
        }

        /// <summary>Совпадают ли отпечатки — то есть можно ли этим двоим играть вместе.</summary>
        public bool Matches(in ContentFingerprint other) =>
            ContentHash == other.ContentHash
            && SchemaVersion == other.SchemaVersion
            && string.Equals(GameVersion, other.GameVersion, System.StringComparison.Ordinal);

        /// <summary>
        /// Чем именно не сошлись — текстом, который можно показать игроку. Пустая строка = сошлись.
        /// Разные причины называются по-разному не ради красоты: «у вас другая версия игры» и «у вас
        /// другой контент» требуют от игрока разных действий.
        /// </summary>
        public string DescribeMismatch(in ContentFingerprint other)
        {
            if (!string.Equals(GameVersion, other.GameVersion, System.StringComparison.Ordinal))
                return $"разные версии игры: {GameVersion} и {other.GameVersion}";

            if (SchemaVersion != other.SchemaVersion)
                return $"разные версии контент-схемы: {SchemaVersion} и {other.SchemaVersion}";

            if (ContentHash != other.ContentHash)
                return $"разный контент: {ContentCount} определений против {other.ContentCount}";

            return string.Empty;
        }

        public override string ToString() =>
            $"content {ContentHash:X16} ({ContentCount} шт, схема {SchemaVersion}), сборка {GameVersion}";

        /// <summary>
        /// Посчитать отпечаток по базе контента. <paramref name="gameVersion"/> подаётся снаружи
        /// (<c>Application.version</c>), чтобы функция осталась чистой и проверяемой.
        /// </summary>
        public static ContentFingerprint Compute(ContentDatabase database, string gameVersion)
        {
            if (database == null) return new ContentFingerprint(0UL, 0, 0, gameVersion);
            return Compute(database.Entries, database.SchemaVersion, gameVersion);
        }

        /// <summary>Тот же расчёт из голого списка — вход для тестов и editor-инструментов.</summary>
        public static ContentFingerprint Compute(
            IReadOnlyList<ContentDefinition> entries, int schemaVersion, string gameVersion)
        {
            if (entries == null || entries.Count == 0)
                return new ContentFingerprint(0UL, 0, schemaVersion, gameVersion);

            // Сортируем сами, хотя база и обещает порядок по id: отпечаток обязан зависеть от НАБОРА
            // контента, а не от того, в каком порядке его положили в список. Иначе пересборка базы у
            // одного из игроков разводила бы сессию на ровном месте.
            var ids = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                ContentDefinition def = entries[i];
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;   // битую запись хеш не заметит
                ids.Add(def.Id);
            }
            ids.Sort(System.StringComparer.Ordinal);

            ulong hash = Fnv1aOffset;
            for (int i = 0; i < ids.Count; i++)
            {
                hash = HashString(hash, ids[i]);
                hash = HashByte(hash, (byte)'\n');   // разделитель: «a»+«bc» не должно совпасть с «ab»+«c»
            }

            return new ContentFingerprint(hash, ids.Count, schemaVersion, gameVersion);
        }

        // FNV-1a 64. Свой, а не string.GetHashCode: тот РАНДОМИЗИРОВАН между запусками процесса, и
        // отпечаток на нём не совпал бы даже у одной и той же сборки с самой собой.
        private const ulong Fnv1aOffset = 14695981039346656037UL;
        private const ulong Fnv1aPrime  = 1099511628211UL;

        private static ulong HashString(ulong hash, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                hash = HashByte(hash, (byte)(c & 0xFF));
                hash = HashByte(hash, (byte)(c >> 8));
            }
            return hash;
        }

        private static ulong HashByte(ulong hash, byte b)
        {
            unchecked
            {
                hash ^= b;
                hash *= Fnv1aPrime;
            }
            return hash;
        }
    }
}
