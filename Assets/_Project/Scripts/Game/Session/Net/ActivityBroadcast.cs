using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Activity;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Владельческая половина «где мы»: объявляет гостям вид мероприятия, границы арены и фазу боя.
    /// </summary>
    /// <remarks>
    /// <b>Спрашиваем состояние, а не слушаем события,</b> и это выбор, а не лень. Дверей к открытию боя
    /// несколько — узел забега, вход на Ристалище, дев-команда, — и подписаться на каждую значило бы
    /// заводить событие в каждой из них и не забыть ни одной. Состояние же одно, читается двумя
    /// свойствами, а лишняя работа сводится к сравнению пяти полей раз в кадр.
    /// <para><b>Первое состояние гость просит сам</b>, а не получает на рукопожатии: его приёмник
    /// рождается позже — сеанс открывается, когда игрок уже принят, — и посланное навстречу ушло бы в
    /// пустоту. Без этого он до первой смены фазы простоял бы в пустом мире.</para>
    /// </remarks>
    public sealed class ActivityBroadcast : ITickable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly ActivityHost  _activities;
        // Карта живёт в мире, а не в мероприятии, поэтому спрашивается отдельно — но объявляется тем же
        // сообщением: для гостя «где мы» это одно состояние, а не два независимых.
        private readonly Flow.IActMapPresence _map;
        // Двор — по той же причине и на тех же правах: он вне мероприятия, но это место, и гость обязан
        // оказаться в нём вместе с хостом.
        private readonly Core.Flow.IHubPresence _hub;

        private readonly NetByteWriter _writer = new NetByteWriter(16);
        private byte[] _envelope;

        private ActivityState _last = ActivityState.Nowhere;

        public ActivityBroadcast(INetTransport transport, ActivityHost activities,
                                 Flow.IActMapPresence map, Core.Flow.IHubPresence hub)
        {
            _transport  = transport  ?? throw new ArgumentNullException(nameof(transport));
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
            _map        = map;
            _hub        = hub;

            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Сколько раз состояние объявлялось — видно в dev-панели.</summary>
        public int AnnouncedCount { get; private set; }

        public void Tick()
        {
            ActivityState now = Read();
            if (now.Equals(_last)) return;

            _last = now;

            // Соло: транспорт не поднят, объявлять некому. Состояние всё равно запоминаем — иначе
            // первый же гость получил бы «изменение» вместо «как есть».
            if (!_transport.IsRunning) return;

            Send(NetPeer.NoPeer, in now);
        }

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        // Пустое сообщение на этом канале — просьба гостя «где вы сейчас». Отвечаем ему одному.
        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.ActivityState || payload.Count != 0) return;

            ActivityState now = Read();
            _last = now;
            Send(from, in now);
        }

        private ActivityState Read()
        {
            ActivitySetup setup = _activities.Current;
            bool battleOpen     = _activities.Battle?.IsOpen ?? false;
            BattlePhase phase   = _activities.Clock?.Phase ?? BattlePhase.None;
            bool mapOpen        = _map?.IsShown ?? false;
            bool hubOpen        = _hub?.IsShown ?? false;

            return new ActivityState(setup.Kind, setup.HideOpponent, setup.Opposition,
                                     battleOpen, phase, mapOpen, hubOpen);
        }

        private void Send(int peerId, in ActivityState state)
        {
            ArraySegment<byte> packet = NetEnvelope.Wrap(
                NetChannel.ActivityState, ActivityStateCodec.Write(in state, _writer), ref _envelope);

            if (peerId == NetPeer.NoPeer) _transport.SendToAll(packet, NetDelivery.Reliable);
            else                          _transport.Send(peerId, packet, NetDelivery.Reliable);

            AnnouncedCount++;
        }
    }
}
