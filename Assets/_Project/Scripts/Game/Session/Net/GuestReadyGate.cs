using System;
using Guildmaster.Core.Net;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина общего согласия: отправляет своё «готов» и показывает счёт, присланный хостом.
    /// </summary>
    /// <remarks>
    /// <b>Гость не решает, все ли готовы.</b> Он и не может: кто в сессии, знает хост. Здесь только своё
    /// согласие и последний услышанный счёт — та же модель, по которой у нас устроен забег и бой.
    /// <para><b>Связанное действие гость не выполняет вовсе.</b> Бой начнётся у него оттого, что хост
    /// сменит фазу, а не оттого, что гость сам себя запустил: второй путь к одному состоянию расходится.
    /// Поэтому <see cref="Bind"/> здесь запоминает только ключ — чтобы кнопка знала, чего ждут.</para>
    /// </remarks>
    public sealed class GuestReadyGate : IReadyGate, IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly IPublisher<ReadyGateChangedEvent> _changedPub;

        private readonly NetByteWriter _writer = new NetByteWriter(4);
        private byte[] _envelope;

        private string _key;
        private bool   _localReady;

        public GuestReadyGate(INetTransport transport, IPublisher<ReadyGateChangedEvent> changedPub)
        {
            _transport  = transport ?? throw new ArgumentNullException(nameof(transport));
            _changedPub = changedPub;
        }

        public int Ready { get; private set; }

        public int Required { get; private set; } = 1;

        public bool LocallyReady => _localReady;

        public void Start() => _transport.MessageReceived += OnMessage;

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        public void Bind(string key, Action onAllReady)
        {
            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"гость: Bind({key}), было «{_key}»");
            if (_key == key) return;

            _key = key;
            SetLocal(false); // подтверждали другое — своё согласие снимаем и говорим об этом хосту
        }

        public void Unbind(string key)
        {
            if (_key != key) return;

            _key = null;
            SetLocal(false);
        }

        public void ToggleLocal() => SetLocal(!_localReady);

        public void Reset(string reason)
        {
            // Счёт сбрасывает хост — он же его и объявит. Своё согласие снимаем сами: оно относилось к
            // тому, чего больше нет.
            if (!_localReady) return;

            Guildmaster.Diagnostics.UiTrace.Log($"своё согласие снято: {reason}");
            SetLocal(false);
        }

        private void SetLocal(bool ready)
        {
            _localReady = ready;
            Announce();

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"гость: своё согласие = {ready}, ключ «{_key}», связь {(_transport.IsRunning ? "есть" : "НЕТ")}");

            if (!_transport.IsRunning) return;

            _writer.Reset();
            _writer.WriteBool(ready);
            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.ReadyGate, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            // Объявленный счёт: счёт, планка, признак срабатывания и ключ. Один байт на этом канале —
            // чужое согласие, а не ответ нам.
            if (channel != NetChannel.ReadyGate || payload.Count < 3) return;

            // Счёт объявляет только хост: чужой счёт от другого гостя показал бы кнопке неправду.
            if (from != NetPeer.HostPeerId) return;

            var bytes = new NetByteReader(payload);
            Ready      = bytes.ReadByte();
            Required   = bytes.ReadByte();
            bool fired = bytes.ReadBool();
            _key       = bytes.ReadString();

            // Хост обнулил счёт — значит согласие снято у всех, включая нас. Иначе кнопка осталась бы
            // нажатой, а хост нас в готовых уже не числил.
            if (Ready == 0) _localReady = false;

            Announce(fired);
        }

        private void Announce(bool fired = false) =>
            _changedPub?.Publish(new ReadyGateChangedEvent(_key, Ready, Required, _localReady, fired));
    }
}
