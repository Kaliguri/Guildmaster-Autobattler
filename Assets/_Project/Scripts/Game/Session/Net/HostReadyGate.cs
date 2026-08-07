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

        // Голос участника: кто → за какой вариант. У решения-согласия вариант один на всех, поэтому
        // прежний «набор согласившихся» — это тот же словарь, просто с единственным значением.
        private readonly Dictionary<int, string> _votes = new();

        // Буфер объявления голосов: пересобирается на месте, наружу уходит в событии.
        private readonly List<PlayerChoice> _choices = new();

        private readonly NetByteWriter _writer = new NetByteWriter(8);
        private byte[] _envelope;

        private string _key;
        private Action<string> _action;

        public HostReadyGate(INetTransport transport, IPublisher<ReadyGateChangedEvent> changedPub)
        {
            _transport   = transport ?? throw new ArgumentNullException(nameof(transport));
            _changedPub  = changedPub;
        }

        public int Ready => _votes.Count;

        public int Required => Mathf.Max(1, _participants.Count);

        public bool LocallyReady => _votes.ContainsKey(LocalId);

        public string LocalChoice =>
            _votes.TryGetValue(LocalId, out string mine) ? mine : DecisionOptions.None;

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

        public void Bind(string key, Action onAllReady) =>
            Bind(key, onAllReady == null ? (Action<string>)null : _ => onAllReady());

        public void Bind(string key, Action<string> onAgreed)
        {
            // Смена того, что решаем, обнуляет голоса: выбор относился к прежнему вопросу.
            if (_key != key) ClearVotes();

            _key    = key;
            _action = onAgreed;
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

        public void ToggleLocal() => Choose(DecisionOptions.Agree);

        public void Choose(string optionId)
        {
            Vote(LocalId, optionId);
            Settle();
        }

        /// <summary>
        /// Записать голос участника. Повтор того же варианта снимает голос, другой — заменяет.
        /// </summary>
        private void Vote(int playerId, string optionId)
        {
            bool had = _votes.TryGetValue(playerId, out string was);

            if (string.IsNullOrEmpty(optionId) || (had && was == optionId)) _votes.Remove(playerId);
            else                                                           _votes[playerId] = optionId;
        }

        public void Reset(string reason)
        {
            if (_votes.Count == 0) return;

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
            _votes.Remove(peerId);
            Settle(); // оставшихся могло стать достаточно ровно в этот момент
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.ReadyGate || payload.Count < 1) return;

            // Первый байт говорит, чьё это сообщение. Раньше стороны различались по ДЛИНЕ («один байт —
            // голос гостя»), и такая развилка держалась ровно до первого изменения формата: голос стал
            // строкой варианта, и длины перестали быть разными.
            var bytes = new NetByteReader(payload);
            if (bytes.ReadByte() != ReadyWire.Vote) return; // объявленный счёт — наше собственное эхо

            string option;
            try { option = bytes.ReadString(); }
            catch (InvalidOperationException) { return; } // чужая версия голоса — счёт не трогаем

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"хост: голос пира {from} = «{option}» (ключ «{_key}», действие {(_action == null ? "НЕ ПРИВЯЗАНО" : "есть")})");

            _participants.Add(from); // голосовать может только тот, кто в сессии, — заодно и учтём его
            Vote(from, option);

            Settle();
        }

        /// <summary>
        /// Проверить, не сошлись ли все на одном, и объявить счёт.
        /// </summary>
        /// <remarks>
        /// <b>Расхождение — это не сбой и не повод вмешаться.</b> Проголосовали все, но за разное —
        /// решение просто не принято, счёт объявляется как есть, и игроки видят, кто что выбрал. Звать
        /// арбитра игра не будет: спор — выбор игроков, а не диагноз (канон коопа, вердикт Макса
        /// 30.07.2026).
        /// </remarks>
        private void Settle()
        {
            if (!TryReadAgreement(out string agreed) || _action == null)
            {
                Announce();
                return;
            }

            // Порядок важен: сначала гасим голоса и объявляем, потом действуем. Действие меняет фазу и
            // может убить нас же вместе со скоупом — то, что стоит после него, не выполнится.
            Action<string> fire = _action;
            ClearVotes();
            Announce(fired: true);
            fire(agreed);
        }

        /// <summary>Все ли высказались и сошлись на одном варианте.</summary>
        private bool TryReadAgreement(out string option)
        {
            option = DecisionOptions.None;
            if (_votes.Count < Required) return false;

            foreach (KeyValuePair<int, string> vote in _votes)
            {
                if (option == DecisionOptions.None) { option = vote.Value; continue; }
                if (vote.Value != option) { option = DecisionOptions.None; return false; }
            }

            return option != DecisionOptions.None;
        }

        private void ClearVotes()
        {
            _votes.Clear();
        }

        private void Announce(bool fired = false)
        {
            _choices.Clear();
            foreach (KeyValuePair<int, string> vote in _votes)
                _choices.Add(new PlayerChoice(vote.Key, vote.Value));

            _changedPub?.Publish(new ReadyGateChangedEvent(_key, Ready, Required, LocallyReady, fired,
                                                           LocalChoice, _choices));

            if (!_transport.IsRunning) return; // соло: объявлять некому

            _writer.Reset();
            _writer.WriteByte(ReadyWire.Tally);
            _writer.WriteByte((byte)Mathf.Clamp(Required, 0, 255));
            _writer.WriteBool(fired);
            // Ключ едет строкой, а не номером: он же и есть смысл действия, а таблица номеров разошлась
            // бы между сборками ровно так, как расходятся все таблицы, которые ведут руками.
            _writer.WriteString(_key);

            // Голоса едут поимённо, а не числом: счёт из них выводится, а обратно — нет, и показу нужно
            // именно «кто за что». Один владелец факта вместо двух согласованных чисел.
            _writer.WriteByte((byte)Mathf.Clamp(_choices.Count, 0, 255));
            for (int i = 0; i < _choices.Count; i++)
            {
                _writer.WriteByte((byte)_choices[i].PlayerId);
                _writer.WriteString(_choices[i].Option);
            }

            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.ReadyGate, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }
    }
}
