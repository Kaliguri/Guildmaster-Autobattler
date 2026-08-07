using System;
using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Владельческая половина общего согласия: считает подтвердивших и выполняет действие, когда согласны все.
    /// </summary>
    /// <remarks>
    /// <b>Кто «все» — знает транспорт, а не догадка.</b> Участники набираются по событиям подключения и
    /// отключения, плюс мы сами. Отсюда важное следствие: <b>уход игрока не подвешивает кнопку</b> — он
    /// перестаёт быть участником, и оставшиеся тут же оказываются «всеми». Кнопка, которую невозможно
    /// нажать из-за того, что кто-то вышел, — худший вид зависания: причина не видна на экране.
    /// <para><b>Согласие снимается при любом изменении заказа.</b> Подтверждали конкретную расстановку;
    /// если её переставили, прежние «готов» относятся к тому, чего больше нет.</para>
    /// </remarks>
    public sealed class HostReadyGate : IReadyGate, IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly IPublisher<ReadyGateChangedEvent> _changedPub;

        private readonly HashSet<int> _participants = new();
        private readonly HashSet<int> _ready        = new();

        private readonly NetByteWriter _writer = new NetByteWriter(8);
        private byte[] _envelope;

        private string _key;
        private Action _action;

        public HostReadyGate(INetTransport transport, IPublisher<ReadyGateChangedEvent> changedPub)
        {
            _transport   = transport ?? throw new ArgumentNullException(nameof(transport));
            _changedPub  = changedPub;
        }

        public int Ready => _ready.Count;

        public int Required => Mathf.Max(1, _participants.Count);

        public bool LocallyReady => _ready.Contains(LocalId);

        private int LocalId => _transport.IsRunning ? _transport.LocalPeerId : NetPeer.HostPeerId;

        public void Start()
        {
            _participants.Add(LocalId);

            // Подключившиеся ДО рождения гейта — та же слепота, что была у состава сеанса: планка «(N/M)»
            // считала бы одного там, где играют двое, и действие срабатывало бы по согласию хозяина,
            // пока напарник ещё смотрит на поле.
            System.Collections.Generic.IReadOnlyList<int> already = _transport.ConnectedPeers;
            for (int i = 0; i < already.Count; i++) _participants.Add(already[i]);

            _transport.PeerConnected    += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;
            _transport.MessageReceived  += OnMessage;
        }

        public void Dispose()
        {
            _transport.PeerConnected    -= OnPeerConnected;
            _transport.PeerDisconnected -= OnPeerDisconnected;
            _transport.MessageReceived  -= OnMessage;
        }

        public void Bind(string key, Action onAllReady)
        {
            // Смена того, что подтверждаем, обнуляет счёт: согласие относилось к прежнему действию.
            if (_key != key) ClearVotes();

            _key    = key;
            _action = onAllReady;
            Announce();
        }

        public void Unbind(string key)
        {
            if (_key != key) return; // ушёл не тот экран, что заказывал — чужое согласие не трогаем

            _key    = null;
            _action = null;
            ClearVotes();
            Announce();
        }

        public void ToggleLocal()
        {
            if (LocallyReady) _ready.Remove(LocalId);
            else              _ready.Add(LocalId);

            Settle();
        }

        public void Reset(string reason)
        {
            if (_ready.Count == 0) return;

            Guildmaster.Diagnostics.UiTrace.Log($"гейт готовности сброшен: {reason}");
            ClearVotes();
            Announce();
        }

        private void OnPeerConnected(int peerId)
        {
            _participants.Add(peerId);
            // Новый участник поднимает планку — значит набранное согласие её больше не берёт. Молча
            // оставить старый счёт значило бы «все готовы» при игроке, который ещё ничего не видел.
            ClearVotes();
            Announce();
        }

        private void OnPeerDisconnected(int peerId)
        {
            _participants.Remove(peerId);
            _ready.Remove(peerId);
            Settle(); // оставшихся могло стать достаточно ровно в этот момент
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            // Ровно один байт — это согласие гостя. Объявленный счёт длиннее (счёт, планка, признак
            // срабатывания и ключ), то есть наше собственное эхо; спутать их значило бы принять свой
            // счёт за чужой голос.
            if (channel != NetChannel.ReadyGate || payload.Count != 1) return;

            var bytes = new NetByteReader(payload);
            bool ready = bytes.ReadBool();

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"хост: согласие от пира {from} = {ready} (ключ «{_key}», действие {(_action == null ? "НЕ ПРИВЯЗАНО" : "есть")})");

            _participants.Add(from); // подтвердить может только тот, кто в сессии, — заодно и учтём его
            if (ready) _ready.Add(from);
            else       _ready.Remove(from);

            Settle();
        }

        /// <summary>Проверить, не собралось ли согласие целиком, и объявить счёт.</summary>
        private void Settle()
        {
            if (_ready.Count < Required || _action == null)
            {
                Announce();
                return;
            }

            // Порядок важен: сначала гасим согласие и объявляем, потом действуем. Действие меняет фазу и
            // может убить нас же вместе со скоупом — то, что стоит после него, не выполнится.
            Action fire = _action;
            ClearVotes();
            Announce(fired: true);
            fire();
        }

        private void ClearVotes()
        {
            _ready.Clear();
        }

        private void Announce(bool fired = false)
        {
            _changedPub?.Publish(new ReadyGateChangedEvent(_key, Ready, Required, LocallyReady, fired));

            if (!_transport.IsRunning) return; // соло: объявлять некому

            _writer.Reset();
            _writer.WriteByte((byte)Mathf.Clamp(_ready.Count, 0, 255));
            _writer.WriteByte((byte)Mathf.Clamp(Required, 0, 255));
            _writer.WriteBool(fired);
            // Ключ едет строкой, а не номером: он же и есть смысл действия, а таблица номеров разошлась
            // бы между сборками ровно так, как расходятся все таблицы, которые ведут руками.
            _writer.WriteString(_key);
            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.ReadyGate, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }
    }
}
