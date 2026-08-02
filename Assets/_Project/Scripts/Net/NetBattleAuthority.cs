using System;
using Guildmaster.Core.Net;
using Guildmaster.Net.Transport;

namespace Guildmaster.Net
{
    /// <summary>
    /// Роль узла, выведенная из транспорта: сеть не поднята — соло, поднята и мы авторитет — хост,
    /// иначе гость.
    /// </summary>
    /// <remarks>
    /// Правду о роли держит именно транспорт, а не сессия: сессия описывает намерение игрока («создаю
    /// игру», «подключаюсь»), а бой обязан идти от того, есть ли живой сокет и чей он. Между ними
    /// бывает зазор в кадры — соединение поднимается не мгновенно, — и всё это время бой должен
    /// считаться по факту, а не по намерению.
    /// </remarks>
    public sealed class NetBattleAuthority : IBattleAuthority
    {
        private readonly INetTransport _transport;

        public NetBattleAuthority(INetTransport transport)
            => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        public BattleRole Role =>
            !_transport.IsRunning ? BattleRole.Solo :
            _transport.IsHost     ? BattleRole.Host :
                                    BattleRole.Guest;

        public bool SimulatesLocally => Role != BattleRole.Guest;
    }
}
