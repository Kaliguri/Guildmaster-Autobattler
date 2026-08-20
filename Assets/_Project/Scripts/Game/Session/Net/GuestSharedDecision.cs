using System;
using Guildmaster.Core.Net;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина общего решения: отправляет свой голос и показывает счёт, присланный хостом.
    /// </summary>
    /// <remarks>
    /// <b>Гость не решает, все ли готовы.</b> Он и не может: кто в сессии, знает хост. Здесь только своё
    /// согласие и последний услышанный счёт — та же модель, по которой у нас устроен забег и бой.
    /// <para><b>Связанное действие гость не выполняет вовсе.</b> Бой начнётся у него оттого, что хост
    /// сменит фазу, а не оттого, что гость сам себя запустил: второй путь к одному состоянию расходится.
    /// Поэтому <see cref="Bind"/> здесь запоминает только ключ — чтобы кнопка знала, чего ждут.</para>
    /// </remarks>
    public sealed class GuestSharedDecision : ISharedDecision, IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly IPublisher<SharedDecisionChangedEvent> _changedPub;

        private readonly NetByteWriter _writer = new NetByteWriter(4);
        private byte[] _envelope;

        private string _key;
        private string _localChoice = DecisionOptions.None;

        // Поимённые голоса, объявленные хостом: показу нужно «кто за что», и считает это не гость.
        private readonly System.Collections.Generic.List<PlayerChoice> _choices =
            new System.Collections.Generic.List<PlayerChoice>();

        public GuestSharedDecision(INetTransport transport, IPublisher<SharedDecisionChangedEvent> changedPub)
        {
            _transport  = transport ?? throw new ArgumentNullException(nameof(transport));
            _changedPub = changedPub;
        }

        public int Voted { get; private set; }

        public int Required { get; private set; } = 1;

        public bool HasLocalChoice => _localChoice != DecisionOptions.None;

        public string LocalChoice => _localChoice;

        public void Start() => _transport.MessageReceived += OnMessage;

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        public void Bind(string key, Action onAgreed) => Bind(key, (Action<string>)null);

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
            if (!HasLocalChoice) return;

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

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.Decision,
                                 DecisionCodec.WriteVote(_localChoice, _writer), ref _envelope),
                NetDelivery.Reliable);
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.Decision || payload.Count < 1) return;

            // Счёт объявляет только хост: чужой счёт от другого гостя показал бы кнопке неправду.
            if (from != NetPeer.HostPeerId) return;

            if (!DecisionCodec.IsTally(payload)) return; // голос другого гостя нам не адресован

            // Прежний счёт честнее половины нового, поэтому подменяем только целиком — это делает кодек.
            if (!DecisionCodec.TryReadTally(payload, _choices, out string key, out int required, out bool fired))
            {
                // Версия сверена рукопожатием: нечитаемое объявление — наша поломка формата.
                Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                    "гость: объявленный счёт не разобрался — формат канала решений разъехался");
                return;
            }

            Required = required;
            _key     = key;
            Voted    = _choices.Count;

            // Свой голос берём из объявления, а не помним отдельно: хост мог сбросить всех, и вторая
            // память об этом молча разошлась бы с его счётом — кнопка осталась бы нажатой у того, кого
            // в проголосовавших уже не числят.
            _localChoice = DecisionOptions.None;
            for (int i = 0; i < _choices.Count; i++)
                if (_choices[i].PlayerId == _transport.LocalPeerId) _localChoice = _choices[i].Option;

            Announce(fired);
        }

        private void Announce(bool fired = false) =>
            _changedPub?.Publish(new SharedDecisionChangedEvent(_key, Voted, Required, HasLocalChoice, fired,
                                                           _localChoice, _choices));
    }
}
