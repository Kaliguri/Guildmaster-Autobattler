using System;

namespace Guildmaster.Core.Net
{
    /// <summary>Где сейчас находится кооп-сессия.</summary>
    public enum CoopSessionState
    {
        /// <summary>Играем одни.</summary>
        Offline = 0,

        /// <summary>Мы хост, ждём гостей.</summary>
        Hosting,

        /// <summary>Гость: соединение поднимается.</summary>
        Connecting,

        /// <summary>Гость: соединение установлено.</summary>
        Connected,
    }

    /// <summary>Почему сессия кончилась. Игроку показывается текстом, поэтому причина именована.</summary>
    public enum CoopEndReason
    {
        None = 0,

        /// <summary>Сами вышли.</summary>
        LocalRequest,

        /// <summary>Хост ушёл — сессия кончается для всех (решение 01.08.2026).</summary>
        HostLeft,

        /// <summary>Хост отказал: другая версия сборки или другой контент.</summary>
        Rejected,

        /// <summary>Не достучались вовсе.</summary>
        ConnectionFailed,
    }

    /// <summary>
    /// Что интерфейсу нужно знать и уметь про кооп-сессию: создать, позвать, выйти, показать состояние.
    /// </summary>
    /// <remarks>
    /// Шов существует ради границы сборок: UI не должен видеть ни Steam, ни наш транспорт — иначе
    /// экран «Сетевая игра» притащит сетевой стек в слой, который рисует кнопки. Здесь же —
    /// единственное, что экран действительно спрашивает.
    /// <para><b>Входа по адресу здесь нет и не будет</b> (решение Макса 02.08.2026): игрок входит только
    /// по приглашению Steam, а списка комнат у нас не существует.</para>
    /// <para>Реализация живёт в <c>Guildmaster.Net</c> и регистрируется корневым скоупом.</para>
    /// </remarks>
    public interface ICoopSessionControl
    {
        /// <summary>Текущее состояние сессии.</summary>
        CoopSessionState State { get; }

        /// <summary>Почему кончилась прошлая сессия.</summary>
        CoopEndReason EndReason { get; }

        /// <summary>Текст последнего отказа или разрыва. Пусто, если их не было.</summary>
        string EndMessage { get; }

        /// <summary>
        /// Что-то, что видит экран, изменилось — пора перерисоваться.
        /// </summary>
        /// <remarks>
        /// Приходит и когда сменилось <see cref="State"/>, и когда сменилось только
        /// <see cref="CanInvite"/>: лобби Steam поднимается асинхронно и появляется уже ПОСЛЕ того, как
        /// сессия стала <see cref="CoopSessionState.Hosting"/>. Событие ровно об одном состоянии
        /// оставило бы кнопку приглашения навсегда погашенной — она бы ждала перерисовки, которой
        /// больше не будет.
        /// </remarks>
        event Action<CoopSessionState> StateChanged;

        /// <summary>
        /// Поднять сессию. Названия и настроек у неё нет: «Создать игру» — это один клик, а зовут
        /// напарника уже приглашением (решение Макса 02.08.2026).
        /// </summary>
        bool StartHost();

        /// <summary>
        /// Позвать друга: открыть оверлей приглашений Steam. Единственный путь входа для игрока —
        /// прямое подключение по адресу остаётся отладочным (решение Макса 02.08.2026).
        /// </summary>
        void InviteFriend();

        /// <summary>Есть ли кого приглашать: сессия поднята и Steam на связи.</summary>
        bool CanInvite { get; }

        /// <summary>Выйти. У хоста это конец сессии для всех.</summary>
        void Leave();
    }
}
