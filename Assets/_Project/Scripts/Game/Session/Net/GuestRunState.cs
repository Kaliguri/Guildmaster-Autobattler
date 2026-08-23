using System;
using Guildmaster.Guild;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Состояние забега у гостя: приходит снимком от хоста и читается как своё. Считать его гость не
    /// умеет и не должен.
    /// </summary>
    /// <remarks>
    /// <b>Гость не применяет команды — он получает результат.</b> Это та же модель, по которой у нас
    /// устроен бой: гость не тикает симуляцию, а смотрит ленту. Причина одна и та же — второй путь к
    /// одному состоянию расходится. Сегодня четыре транзакции (покупка, взятие Мементо, перезапуск,
    /// расширение запаса) идут мимо шины команд и в лог не попадают; примени гость лог у себя, он
    /// потерял бы ровно эти изменения и узнал бы об этом не сразу, а через час игры.
    /// <para><b>Сервиса, пишущего сейв, у гостя нет вовсе</b> — этот класс не умеет ни сохранять, ни
    /// загружать. Так «случайно записать чужую гильдию» ему просто нечем, и это сильнее любой проверки
    /// «а мы точно хост?».</para>
    /// <para><b>Пустое состояние до первого снимка — законное:</b> между «подключились» и «получили
    /// забег» проходит доля секунды, и <c>null</c> здесь значит ровно то же, что вне забега у владельца.
    /// Читатели это уже умеют.</para>
    /// </remarks>
    public sealed class GuestRunState : ISessionRunState, IStartable, IDisposable
    {
        private readonly INetTransport _transport;

        private byte[] _envelope;

        public GuestRunState(INetTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>
        /// Попросить хоста прислать забег. Просим сами и в момент готовности: посланное нам «навстречу»,
        /// до рождения этого приёмника, ушло бы в пустоту.
        /// </summary>
        public void Start()
        {
            if (!_transport.IsRunning) return;

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.RunSnapshot, default, ref _envelope),
                NetDelivery.Reliable);
        }

        /// <summary>Последнее состояние, присланное хостом. <c>null</c> — снимок ещё не доехал.</summary>
        public RunState Current { get; private set; }

        /// <summary>Сколько снимков принято. Видно в dev-панели: «доезжает ли вообще».</summary>
        public int SnapshotsReceived { get; private set; }

        /// <summary>Пришло новое состояние — показу пора перечитать забег.</summary>
        public event Action<RunState> SnapshotReceived;

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.RunSnapshot) return;

            // Забег объявляет только хост. Чужой снимок от другого гостя — либо ошибка разводки, либо
            // подмена; в обоих случаях принимать его нельзя.
            if (from != NetPeer.HostPeerId) return;

            RunState state = RunSnapshotCodec.Read(payload);
            if (state == null) return; // кодек уже сказал вслух, почему

            Current = state;
            SnapshotsReceived++;
            SnapshotReceived?.Invoke(state);
        }
    }
}
