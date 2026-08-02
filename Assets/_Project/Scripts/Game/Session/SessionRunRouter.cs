using Guildmaster.Guild;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Ответ на вопрос «что в забеге сейчас» для тех, кто переживает сеансы: корневой интерфейс, мир,
    /// показ. Делегирует держателю текущей сессии, а вне сессии честно отвечает «забега нет».
    /// </summary>
    /// <remarks>
    /// Третий роутер того же образца, что <c>StageFrameRouter</c> (кадр показа) и
    /// <c>ActivityClockRouter</c> (часы боя), и по той же причине: долгожитель не может держать прямую
    /// ссылку на объект из уехавшего вниз скоупа — она пережила бы его владельца и показывала бы
    /// состояние сеанса, который давно кончился.
    /// <para>Роутер вечен, держатель под ним меняется — поэтому спрашиваем В МОМЕНТ обращения, а не
    /// запоминаем на старте.</para>
    /// </remarks>
    public sealed class SessionRunRouter : IRunStateView
    {
        private readonly SessionHost _sessions;

        public SessionRunRouter(SessionHost sessions) => _sessions = sessions;

        public RunState Current => _sessions.Run?.Current;
    }
}
