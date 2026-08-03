using Guildmaster.Core.Players;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Чужие курсоры для тех, кто переживает сеансы: слой показа рисует их в мире, а живут они в сеансе.
    /// Вне сеанса честно отвечает «никого».
    /// </summary>
    /// <remarks>
    /// Тот же образец, что <see cref="SessionRunRouter"/> и <see cref="SessionPlayerRouter"/>. Отрисовке
    /// нужен объект, который есть всегда: она поднимается вместе с миром и переживает и вход в кооп, и
    /// выход из него.
    /// </remarks>
    public sealed class SessionPresenceRouter : IPresenceView
    {
        private readonly SessionHost _sessions;

        public SessionPresenceRouter(SessionHost sessions) => _sessions = sessions;

        public int Count => _sessions?.Presence?.Count ?? 0;

        public RemoteCursor this[int index] => _sessions.Presence[index];
    }
}
