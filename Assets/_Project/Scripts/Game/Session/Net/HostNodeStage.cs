using System;
using Guildmaster.Net;
using Guildmaster.Net.Transport;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Владельческая половина «что на экране»: объявляет гостям шаг узла и его содержимое.
    /// </summary>
    /// <remarks>
    /// <b>Объявляет тот, кто ведёт узел.</b> Витрину награды катит хозяин — у него реестр контента,
    /// генератор и состояние забега; гость получает готовый набор id и собирает из него ТУ ЖЕ витрину.
    /// Второй раскат у гостя дал бы другие три реликвии, потому что бросок случаен.
    /// <para><b>Первый шаг гость просит сам</b> — как и «где мы». Его приёмник рождается вместе с
    /// сеансом, то есть позже рукопожатия, и объявление, посланное навстречу, ушло бы в пустоту.</para>
    /// <para><b>Шаг живёт, пока идёт экран.</b> Закрылся — объявляем <see cref="NodeStageState.Idle"/>,
    /// иначе подключившийся следом увидел бы витрину, которой уже нет.</para>
    /// </remarks>
    public sealed class HostNodeStage : IDisposable
    {
        private readonly INetTransport _transport;

        private readonly NetByteWriter _writer = new NetByteWriter(64);
        private byte[] _envelope;

        private NodeStageState _current = NodeStageState.Idle;

        public HostNodeStage(INetTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Что объявлено сейчас — видно в dev-панели.</summary>
        public NodeStageState Current => _current;

        /// <summary>Объявить шаг узла. Повтор того же — молчим: применение у гостя идемпотентно.</summary>
        public void Announce(in NodeStageState state)
        {
            if (state.Equals(_current)) return;

            _current = state;
            if (!_transport.IsRunning) return; // соло: объявлять некому, но состояние помним

            Send(NetPeer.NoPeer, in state);
        }

        /// <summary>Экран закрылся. Отдельным именем, потому что зовут это из finally.</summary>
        public void Clear() => Announce(NodeStageState.Idle);

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        // Пустое сообщение на этом канале — просьба гостя «что у вас на экране». Отвечаем ему одному.
        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.NodeStage || payload.Count != 0) return;

            Send(from, in _current);
        }

        private void Send(int peerId, in NodeStageState state)
        {
            ArraySegment<byte> packet = NetEnvelope.Wrap(
                NetChannel.NodeStage, NodeStageCodec.Write(in state, _writer), ref _envelope);

            if (peerId == NetPeer.NoPeer) _transport.SendToAll(packet, NetDelivery.Reliable);
            else                          _transport.Send(peerId, packet, NetDelivery.Reliable);
        }
    }
}
