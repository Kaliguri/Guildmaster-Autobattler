namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Формат файла повтора боя: та же лента, что кооп гоняет по сети, положенная на диск. Заголовок
    /// плюс поток записей (состав и чанки ленты вперемешку, в порядке появления).
    /// </summary>
    /// <remarks>
    /// <b>Почему повтор — это ЛЕНТА, а не «сид + пересим».</b> Пересимуляция дала бы тот же бой только на
    /// той же версии игры: любая правка баланса, порядка RNG или обхода систем — и запись разошлась бы.
    /// Лента же держит РАЗРЕШЁННЫЕ факты (позиции, HP, урон, смерти), а не рецепт их вывода, поэтому
    /// переживает балансные правки даром: правка чисел в новой версии старую запись не трогает. Цена —
    /// файл толще (десятки КБ на бой против сотен байт), и это принято: «показываем реальный баланс,
    /// поедет так поедет» (Макс, 04.08.2026).
    /// <para><b>Что запись НЕ переживает.</b> Двоичный формат чанка (защищён своим байтом версии) и
    /// удаление/переименование id контента: состав едет строковым id и разрешается через реестр на
    /// воспроизведении. Пока id жив — бой рисуется целиком, даже если все его статы переписаны. id
    /// удалён — рисовать нечем, и это честный отказ, а не мусор (как у сейва <c>Unsupported</c>).</para>
    /// <para><b>Конверт версионируется как сейв:</b> <see cref="FormatVersion"/> в заголовке ловит
    /// запись из более новой игры (<see cref="ReplayLoadResult.TooNew"/>) — не грузим и не читаем как
    /// попало. <see cref="GameVersion"/> и <see cref="Seed"/> едут для провенанса и на будущее (показать
    /// «запись версии X», выбрать сид для пере-прогона), на само воспроизведение не влияют.</para>
    /// </remarks>
    public static class ReplayFile
    {
        /// <summary>Сигнатура файла: "GMRP" (Guildmaster Replay). Чужие байты отсекаются сразу.</summary>
        public static readonly byte[] Magic = { (byte)'G', (byte)'M', (byte)'R', (byte)'P' };

        /// <summary>
        /// Версия формата ФАЙЛА (не путать с <see cref="TapeChunkFormat.Version"/> — версией чанка внутри).
        /// Едет в заголовке; запись из более новой версии отвергается громко.
        /// </summary>
        public const byte FormatVersion = 1;

        /// <summary>Тег записи в потоке. Порядок значений — часть формата, не переставлять.</summary>
        public static class Record
        {
            /// <summary>Паспорт юнита: <c>[id:int][team:byte][contentId:string]</c>. Пишется при спавне.</summary>
            public const byte Roster = 1;

            /// <summary>Чанк ленты: <c>[len:int][байты TapeChunkWriter]</c>. Пишется по мере готовности тиков.</summary>
            public const byte Chunk = 2;
        }

        /// <summary>Расширение файлов повтора. Служебные суффиксы (если появятся) идут ПОСЛЕ него.</summary>
        public const string Extension = ".gmrp";

        /// <summary>
        /// Заголовок файла: то, что известно на открытии записи и не меняется по ходу боя. Состав СЮДА не
        /// кладётся — он растёт по ходу (призывы) и едет записями <see cref="Record.Roster"/> в потоке.
        /// </summary>
        public readonly struct Header
        {
            public readonly byte FormatVersion;

            /// <summary>Версия игры на момент записи — провенанс, на воспроизведение не влияет.</summary>
            public readonly string GameVersion;

            /// <summary>Сид прогона — провенанс и задел на будущий пере-прогон; лента им не считается.</summary>
            public readonly ulong Seed;

            /// <summary>Человеческое имя боя для списка повторов (например «relic.antimage vs relic.treant»).</summary>
            public readonly string Title;

            public Header(byte formatVersion, string gameVersion, ulong seed, string title)
            {
                FormatVersion = formatVersion;
                GameVersion   = gameVersion ?? string.Empty;
                Seed          = seed;
                Title         = title ?? string.Empty;
            }
        }

        /// <summary>Записать заголовок в начало потока.</summary>
        public static void WriteHeader(NetByteWriter bytes, in Header header)
        {
            for (int i = 0; i < Magic.Length; i++) bytes.WriteByte(Magic[i]);
            bytes.WriteByte(header.FormatVersion);
            bytes.WriteString(header.GameVersion);
            bytes.WriteLong(unchecked((long)header.Seed));
            bytes.WriteString(header.Title);
        }

        /// <summary>
        /// Прочитать заголовок с начала потока. Курсор читателя после успеха стоит на первой записи.
        /// <para>Возвращает вердикт как у сейва: <see cref="ReplayLoadResult.Ok"/> —
        /// <paramref name="header"/> заполнен и тело можно читать; <see cref="ReplayLoadResult.Corrupted"/>
        /// — не наш файл или обрезан; <see cref="ReplayLoadResult.TooNew"/> — формат новее нашего.</para>
        /// </summary>
        public static ReplayLoadResult TryReadHeader(NetByteReader bytes, out Header header)
        {
            header = default;

            try
            {
                for (int i = 0; i < Magic.Length; i++)
                    if (bytes.ReadByte() != Magic[i]) return ReplayLoadResult.Corrupted;

                byte version = bytes.ReadByte();
                // Читаем поля даже при чужой версии не станем: раскладка могла смениться, и чтение ушло бы
                // в мусор. Версию узнали — этого хватает на вердикт.
                if (version > FormatVersion) { header = new Header(version, string.Empty, 0UL, string.Empty); return ReplayLoadResult.TooNew; }
                if (version < 1) return ReplayLoadResult.Corrupted;

                string gameVersion = bytes.ReadString();
                ulong  seed        = unchecked((ulong)bytes.ReadLong());
                string title       = bytes.ReadString();

                header = new Header(version, gameVersion, seed, title);
                return ReplayLoadResult.Ok;
            }
            catch (System.InvalidOperationException)
            {
                // Байты кончились раньше формата — файл обрезан. Читатель бросает именно это.
                return ReplayLoadResult.Corrupted;
            }
        }
    }

    /// <summary>Чем кончилась загрузка файла повтора. Зеркалит <c>SaveLoadResult</c> по духу.</summary>
    public enum ReplayLoadResult
    {
        /// <summary>Заголовок прочитан, тело можно воспроизводить.</summary>
        Ok = 0,

        /// <summary>Файла нет на диске — решает вызывающий, до чтения байтов.</summary>
        Missing,

        /// <summary>Не наш файл, обрезан или бит: сигнатура/раскладка не сходятся.</summary>
        Corrupted,

        /// <summary>Формат новее нашего: писала более новая версия игры, читать как попало нельзя.</summary>
        TooNew,
    }
}
