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
        private string _localChoice = DecisionOptions.None;

        // Поимённые голоса, объявленные хостом: показу нужно «кто за что», и считает это не гость.
        private readonly System.Collections.Generic.List<PlayerChoice> _choices =
            new System.Collections.Generic.List<PlayerChoice>();

        public GuestReadyGate(INetTransport transport, IPublisher<ReadyGateChangedEvent> changedPub)
        {
            _transport  = transport ?? throw new ArgumentNullException(nameof(transport));
            _changedPub = changedPub;
        }

        public int Ready { get; private set; }

        public int Required { get; private set; } = 1;

        public bool LocallyReady => _localChoice != DecisionOptions.None;

        public string LocalChoice => _localChoice;

        public void Start() => _transport.MessageReceived += OnMessage;

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        public void Bind(string key, Action onAllReady) => Bind(key, (Action<string>)null);

        public void Bind(string key, Action<string> onAgreed)
        {
            // Действие гость не выполняет вовсе — см. докстринг класса; аргумент здесь только ради
            // общего контракта.
            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"гость: Bind({key}), было «{_key}»");
            if (_key == key) return;

            _key = key;
            SetLocal(DecisionOptions.None); // решали другое — свой голос снимаем и говорим об этом хосту
        }

        public void Unbind(string key)
        {
            if (_key != key) return;

            _key = null;
            SetLocal(DecisionOptions.None);
        }

        public void ToggleLocal() => Choose(DecisionOptions.Agree);

        public void Choose(string optionId)
        {
            // Повтор того же варианта снимает голос — то же правило, что у хозяина. Считать его тут
            // заново нельзя: два места, решающих «снял или сменил», разъедутся на первой же правке,
            // поэтому правило одно и записано у обоих одинаково.
            SetLocal(_localChoice == optionId ? DecisionOptions.None : optionId);
        }

        public void Reset(string reason)
        {
            // Счёт сбрасывает хост — он же его и объявит. Свой голос снимаем сами: он относился к
            // тому, чего больше нет.
            if (!LocallyReady) return;

            Guildmaster.Diagnostics.UiTrace.Log($"свой голос снят: {reason}");
            SetLocal(DecisionOptions.None);
        }

        private void SetLocal(string option)
        {
            _localChoice = option ?? DecisionOptions.None;
            Announce();

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"гость: свой голос = «{_localChoice}», ключ «{_key}», связь {(_transport.IsRunning ? "есть" : "НЕТ")}");

            if (!_transport.IsRunning) return;

            _writer.Reset();
            _writer.WriteByte(ReadyWire.Vote);
            _writer.WriteString(_localChoice);
            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.ReadyGate, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.ReadyGate || payload.Count < 1) return;

            // Счёт объявляет только хост: чужой счёт от другого гостя показал бы кнопке неправду.
            if (from != NetPeer.HostPeerId) return;

            var bytes = new NetByteReader(payload);
            if (bytes.ReadByte() != ReadyWire.Tally) return; // голос другого гостя нам не адресован

            bool fired;
            try
            {
                Required = bytes.ReadByte();
                fired    = bytes.ReadBool();
                _key     = bytes.ReadString();

                int count = bytes.ReadByte();
                _choices.Clear();
                for (int i = 0; i < count; i++)
                {
                    int    voter  = bytes.ReadByte();
                    string option = bytes.ReadString();
                    _choices.Add(new PlayerChoice(voter, option));
                }
            }
            catch (InvalidOperationException)
            {
                return; // чужая версия объявления — прежний счёт честнее половины нового
            }

            Ready = _choices.Count;

            // Свой голос берём из объявления, а не помним отдельно: хост мог сбросить всех, и вторая
            // память об этом молча разошлась бы с его счётом — кнопка осталась бы нажатой у того, кого
            // в проголосовавших уже не числят.
            _localChoice = DecisionOptions.None;
            for (int i = 0; i < _choices.Count; i++)
                if (_choices[i].PlayerId == _transport.LocalPeerId) _localChoice = _choices[i].Option;

            Announce(fired);
        }

        private void Announce(bool fired = false) =>
            _changedPub?.Publish(new ReadyGateChangedEvent(_key, Ready, Required, LocallyReady, fired,
                                                           _localChoice, _choices));
    }
}
