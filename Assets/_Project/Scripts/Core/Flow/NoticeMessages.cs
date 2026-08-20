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

    /// <summary>Один ответ в окне: что написано на кнопке и что она делает.</summary>
    public readonly struct NoticeOption
    {
        /// <summary>Ключ локализации подписи.</summary>
        public readonly string LocKey;

        /// <summary>Подпись, пока ключа нет в таблице.</summary>
        public readonly string Fallback;

        /// <summary>Что произойдёт по нажатию. <c>null</c> — просто закрыть окно.</summary>
        public readonly System.Action Act;

        /// <summary>Выделить как основной ответ.</summary>
        public readonly bool Primary;

        public NoticeOption(string locKey, string fallback, System.Action act, bool primary = false)
        {
            LocKey   = locKey;
            Fallback = fallback;
            Act      = act;
            Primary  = primary;
        }
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
    /// <para><b>Число кнопок — это параметр, а не вид окна</b> (решение Макса 09.08.2026: «Это ведь
    /// тоже "окно-уведомление" и все. Просто вариация с двумя кнопками»). Ошибка, приглашение в игру,
    /// разрыв связи и подтверждение сноса — одно устройство с разным списком ответов. Пока их было
    /// три отдельных экрана, они жили ровно той формой, из-за которой у нас разъезжались экраны узла:
    /// правка текста или отступа попадала в один и минула два.</para>
    /// <para><b>Закрывается только кнопкой</b> (там же: «Пока все требует кнопки»). Поэтому поля «можно
    /// ли отклонить» здесь нет: у окна всегда есть хотя бы один ответ, и снятие мимо него не
    /// предусмотрено ничем.</para>
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
        /// Что это значит для игры: «забег продолжается», «три дома вместе с забегами». Пусто — строки
        /// не будет.
        /// </summary>
        /// <remarks>
        /// Отдельно от <see cref="BodyFallback"/>, потому что отвечает на другой вопрос: тело говорит
        /// ЧТО случилось, последствие — чем это обернётся. Слитые в один абзац, они читаются хуже
        /// обоих.
        /// </remarks>
        public readonly string Consequence;

        /// <summary>
        /// Что сказала система — как есть, без перевода. Пусто — строки не будет.
        /// </summary>
        public readonly string Details;

        /// <summary>
        /// Чем игроку ответить, слева направо. Пусто — одна кнопка «Понятно», которая просто закрывает.
        /// </summary>
        public readonly System.Collections.Generic.IReadOnlyList<NoticeOption> Options;

        public NoticeRequest(NoticeKind kind, string titleKey, string titleFallback,
                             string bodyKey, string bodyFallback, string details = null,
                             System.Collections.Generic.IReadOnlyList<NoticeOption> options = null,
                             string consequence = null)
        {
            Kind          = kind;
            TitleKey      = titleKey;
            TitleFallback = titleFallback;
            BodyKey       = bodyKey;
            BodyFallback  = bodyFallback;
            Consequence   = consequence;
            Details       = details;
            Options       = options;
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

        /// <summary>
        /// Подробность строкой ниже: ЧЕГО именно ждём. Пусто — строки не будет.
        /// </summary>
        /// <remarks>
        /// Отдельно от <see cref="TitleFallback"/> по той же причине, что последствие отдельно от
        /// тела у сообщения: заголовок отвечает «что идёт», подробность — «почему это не мгновенно».
        /// Слитые в одну строку, они дают то, из-за чего экран и переделывали: фразу, по которой
        /// непонятно, ждёт игра чего-то или зависла.
        /// </remarks>
        public readonly string Detail;

        public BusyRequest(string titleKey, string titleFallback, CancellationToken until,
                           string detail = null)
        {
            TitleKey      = titleKey;
            TitleFallback = titleFallback;
            Until         = until;
            Detail        = detail;
        }
    }
}
