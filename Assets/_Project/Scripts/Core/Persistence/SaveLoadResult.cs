namespace Guildmaster.Core.Persistence
{
    /// <summary>
    /// Исход попытки загрузки (ТЗ [[save-system]] §5). Именно исход, а не «значение или null»: вызывающему
    /// нужно различать «сейва нет» и «сейв есть, но мы не имеем права его трогать» — от этого зависит,
    /// показывать ли кнопку «Продолжить» и что сказать игроку.
    /// </summary>
    public enum SaveLoadStatus
    {
        /// <summary>Загружено, значение валидно.</summary>
        Ok,

        /// <summary>Файла нет. Не ошибка: первый запуск, новый профиль.</summary>
        Missing,

        /// <summary>Файл есть, но не читается. Уехал в <c>.corrupt</c>, бэкап не помог.</summary>
        Corrupted,

        /// <summary>
        /// Сейв записан более новой версией игры. <b>Не грузим и не затираем</b> — иначе откат на прошлый
        /// билд через Steam beta-branch съел бы прогресс молча.
        /// </summary>
        TooNew,

        /// <summary>
        /// Схема старее текущей, а поднять её нечем (миграций нет или цепочка оборвана намеренно).
        /// Для забега это допустимо по решению S4, для профиля — нет.
        /// </summary>
        Unsupported,
    }

    /// <summary>
    /// Результат загрузки с диагностикой. Версии сохранены даже для неуспешных исходов — их показывают
    /// игроку («сохранение из версии 0.5.1») и пишут в багрепорт.
    /// </summary>
    public readonly struct SaveLoadResult<T>
    {
        public SaveLoadStatus Status { get; }

        /// <summary>Значение; осмысленно только при <see cref="SaveLoadStatus.Ok"/>.</summary>
        public T Value { get; }

        /// <summary>Версия схемы, записанная в файле (0, если прочитать не удалось).</summary>
        public int SavedSchemaVersion { get; }

        /// <summary>Версия игры, записавшей файл (пусто, если прочитать не удалось). Только для диагностики.</summary>
        public string SavedGameVersion { get; }

        public bool IsOk => Status == SaveLoadStatus.Ok;

        /// <summary>Файл на диске есть, но использовать его нельзя. Отличается от <see cref="SaveLoadStatus.Missing"/>.</summary>
        public bool IsBlocked => Status == SaveLoadStatus.TooNew || Status == SaveLoadStatus.Unsupported;

        private SaveLoadResult(SaveLoadStatus status, T value, int schemaVersion, string gameVersion)
        {
            Status             = status;
            Value              = value;
            SavedSchemaVersion = schemaVersion;
            SavedGameVersion   = gameVersion ?? string.Empty;
        }

        public static SaveLoadResult<T> Ok(T value, int schemaVersion, string gameVersion) =>
            new(SaveLoadStatus.Ok, value, schemaVersion, gameVersion);

        public static SaveLoadResult<T> Missing() =>
            new(SaveLoadStatus.Missing, default, 0, string.Empty);

        public static SaveLoadResult<T> Corrupted() =>
            new(SaveLoadStatus.Corrupted, default, 0, string.Empty);

        public static SaveLoadResult<T> TooNew(int schemaVersion, string gameVersion) =>
            new(SaveLoadStatus.TooNew, default, schemaVersion, gameVersion);

        public static SaveLoadResult<T> Unsupported(int schemaVersion, string gameVersion) =>
            new(SaveLoadStatus.Unsupported, default, schemaVersion, gameVersion);
    }
}
