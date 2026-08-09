using System.Threading;

namespace Guildmaster.Core.Flow
{
    /// <summary>О чём речь: от «к сведению» до «не получилось». Решает подачу — цвет и заголовок.</summary>
    public enum NoticeKind : byte
    {
        /// <summary>Просто новость: что-то случилось, делать ничего не надо.</summary>
        Info = 0,

        /// <summary>Предупреждение: игра идёт дальше, но не так, как игрок рассчитывал.</summary>
        Warning = 1,

        /// <summary>Не получилось: задуманное действие не состоялось.</summary>
        Error = 2,
    }

    /// <summary>
    /// Сказать игроку то, что он обязан узнать, — одним окном на всю игру.
    /// </summary>
    /// <remarks>
    /// <b>Заведено потому, что сообщать было нечем.</b> До 09.08.2026 каждое сообщение либо не
    /// показывалось вовсе, либо заводило себе персональный экран: у разрыва связи свой диалог, у
    /// вопроса — свой, у остального ничего. Игрок на прогоне вдвоём видел, что подключение не
    /// состоялось, и не мог узнать почему (наход. Макса 08.08.2026: «Щас не понятно, что произошло,
    /// почему не смогли подключиться к пвп»).
    /// <para><b>Одно окно на все виды, а не экран на случай.</b> Вид меняет подачу, но не устройство:
    /// два похожих экрана разошлись бы на первой правке, как это уже случилось с диалогами.</para>
    /// <para><b>Подробность живёт отдельно от текста.</b> <see cref="Details"/> — то, что сказала
    /// система (причина отказа Steam, код ошибки), и оно НЕ локализуется: перевести чужую диагностику
    /// нельзя, а потерять — значит снова оставить игрока без ответа «почему». Локализуются заголовок
    /// и объяснение, у них ключи.</para>
    /// </remarks>
    public readonly struct NoticeRequest
    {
        public readonly NoticeKind Kind;

        /// <summary>Ключ заголовка. Пусто — заголовком станет имя вида («Ошибка»).</summary>
        public readonly string TitleKey;

        /// <summary>Заголовок, пока ключа нет в таблице.</summary>
        public readonly string TitleFallback;

        /// <summary>Ключ объяснения: что произошло, словами игрока.</summary>
        public readonly string BodyKey;

        /// <summary>Объяснение, пока ключа нет в таблице.</summary>
        public readonly string BodyFallback;

        /// <summary>
        /// Что сказала система — как есть, без перевода. Пусто — строки не будет.
        /// </summary>
        public readonly string Details;

        public NoticeRequest(NoticeKind kind, string titleKey, string titleFallback,
                             string bodyKey, string bodyFallback, string details = null)
        {
            Kind          = kind;
            TitleKey      = titleKey;
            TitleFallback = titleFallback;
            BodyKey       = bodyKey;
            BodyFallback  = bodyFallback;
            Details       = details;
        }
    }

    /// <summary>
    /// Игра занята — показать это, пока идёт ожидание.
    /// </summary>
    /// <remarks>
    /// <b>Ожидание без экрана неотличимо от зависания.</b> Подключение к чужому лобби идёт через relay
    /// Valve и занимает секунды, в течение которых не происходило ничего видимого — игрок жал кнопку
    /// повторно, не зная, засчиталось ли первое нажатие (наход. Макса 08.08.2026: «Не хватает UI
    /// загрузки»).
    /// <para><b>Сроком владеет тот, кто ждёт,</b> а не экран: показ снимается отменой
    /// <see cref="Until"/>. Своей кнопки «закрыть» у него нет намеренно — закрытое окно ожидания
    /// означало бы, что ждать перестали, а ждать не перестали.</para>
    /// </remarks>
    public readonly struct BusyRequest
    {
        /// <summary>Ключ строки «чего ждём».</summary>
        public readonly string TitleKey;

        /// <summary>Строка, пока ключа нет в таблице.</summary>
        public readonly string TitleFallback;

        /// <summary>Ожидание кончилось: экран снимается отменой этого токена.</summary>
        public readonly CancellationToken Until;

        public BusyRequest(string titleKey, string titleFallback, CancellationToken until)
        {
            TitleKey      = titleKey;
            TitleFallback = titleFallback;
            Until         = until;
        }
    }
}
