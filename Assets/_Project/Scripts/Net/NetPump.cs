using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Net
{
    /// <summary>
    /// Раз в кадр прокачивает транспорт: без этого пришедшие байты лежат в очереди и не доходят до
    /// подписчиков.
    /// </summary>
    /// <remarks>
    /// Отдельный класс, потому что <see cref="INetTransport"/> обещает доставку **только** в
    /// <c>Poll</c> — обещание нужно тестам, где два узла живут в одном процессе и шагают вызовом
    /// метода. Кто-то должен звать этот метод в игре, и это не обязанность транспорта: он не знает про
    /// игровой цикл и не должен.
    /// <para>В соло качать нечего — транспорт не поднят, очередь пуста, и вызов стоит одного ветвления.</para>
    /// </remarks>
    public sealed class NetPump : ITickable
    {
        private readonly INetTransport _transport;

        public NetPump(INetTransport transport) => _transport = transport;

        public void Tick()
        {
            if (_transport == null || !_transport.IsRunning) return;
            _transport.Poll();
        }
    }
}
