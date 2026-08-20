using System;
using System.Collections.Generic;
using Guildmaster.Core.Input;
using Guildmaster.Core.Players;
using Guildmaster.Net;
using Guildmaster.Net.Presence;
using Guildmaster.Net.Transport;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Владельческая половина присутствия: собирает курсоры всех участников и раздаёт каждому только
    /// курсоры его стороны.
    /// </summary>
    /// <remarks>
    /// <b>Отбор по стороне стоит ЗДЕСЬ, на отправке</b> (решение Макса 03.08.2026). Разослать всем и не
    /// нарисовать лишнее у получателя было бы проще на один метод и бесполезно по сути: в PvP байты
    /// чужого курсора лежали бы в памяти противника, а курсор в фазе расстановки — это и есть чужой строй.
    /// <para><b>Пакет собирается на каждого получателя отдельно.</b> Цена — по сборке на участника в
    /// кадр; выигрыш в том, что ветки «а если все свои» нет вовсе, и в кампании работает ровно тот же
    /// путь, что в PvP.</para>
    /// <para><b>Автора берём из конверта, а не из содержимого.</b> Гость присылает своё состояние с
    /// собственным номером внутри, и доверять ему значит разрешить представиться чужим номером — это та
    /// же линия, по которой отбрасываются команды забега с чужим автором.</para>
    /// <para><b>Время — не игровое.</b> Присутствие идёт по <c>unscaledTime</c>: пауза боя останавливает
    /// симуляцию, а не людей, и замерший на паузе чужой курсор читается как разрыв связи.</para>
    /// </remarks>
    public sealed class HostPresence : IPresenceView, IStartable, ITickable, IDisposable
    {
        private readonly INetTransport   _transport;
        private readonly IPointerWorld   _pointer;
        private readonly ISessionRoster  _roster;

        private readonly PresenceCursors _cursors = new PresenceCursors();
        private readonly PresenceSender  _sender  = new PresenceSender();

        private readonly Dictionary<int, PresenceState> _latest = new Dictionary<int, PresenceState>(4);
        private readonly List<PresenceState>            _outgoing = new List<PresenceState>(4);
        // Приёмный список переиспользуется: пакеты идут до 128 раз в секунду от каждого, и свежий
        // List на сообщение отдавал бы сборщику мусора столько же.
        private readonly List<PresenceState>            _incoming = new List<PresenceState>(1);
        private readonly NetByteWriter                  _writer   = new NetByteWriter(128);
        private byte[] _envelope;

        private bool _dirty;

        public HostPresence(INetTransport transport, IPointerWorld pointer, ISessionRoster roster)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _pointer   = pointer;
            _roster    = roster;
        }

        public int Count => _cursors.Count;

        public RemoteCursor this[int index] => _cursors[index];

        public void Start()
        {
            _transport.MessageReceived  += OnMessage;
            _transport.PeerDisconnected += OnPeerDisconnected;
        }

        public void Dispose()
        {
            _transport.MessageReceived  -= OnMessage;
            _transport.PeerDisconnected -= OnPeerDisconnected;
        }

        public void Tick()
        {
            float now = Time.unscaledTime;
            int   me  = _roster?.LocalId ?? NetPeer.HostPeerId;

            if (_pointer != null && _pointer.IsAvailable &&
                _sender.TrySample(_pointer.Position, me, now, out PresenceState mine))
            {
                _latest[me] = mine;
                _dirty      = true;
            }

            if (_dirty && _transport.IsRunning)
            {
                Broadcast();
                _dirty = false;
            }

            _cursors.Sample(now, _roster, me);
        }

        private void OnPeerDisconnected(int peerId)
        {
            _latest.Remove(peerId);
            _cursors.Forget(peerId);
            _dirty = true;
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.Presence) return;

            if (!PresenceCodec.TryRead(payload, _incoming) || _incoming.Count == 0) return;

            PresenceState said = _incoming[0];
            var state = new PresenceState(from, said.Sequence, said.Cursor, said.Velocity,
                                          said.HoveredId, said.HeldId);

            _latest[from] = state;
            _dirty        = true;

            // Себе показываем только своих: хост видит все курсоры по устройству — он их и раздаёт, —
            // но в PvP рисовать курсор противника у себя было бы тем же подглядыванием, только в свою
            // пользу.
            if (_roster == null || _roster.SharesTeamWithLocal(from))
                _cursors.Push(in state, Time.unscaledTime);
        }

        /// <summary>Разослать каждому участнику курсоры его стороны — и только их.</summary>
        private void Broadcast()
        {
            if (_roster == null) return;

            IReadOnlyList<SessionPlayer> players = _roster.Players;
            for (int i = 0; i < players.Count; i++)
            {
                SessionPlayer target = players[i];
                if (target.Id == _roster.LocalId) continue; // себе слать нечего

                _outgoing.Clear();
                for (int j = 0; j < players.Count; j++)
                {
                    SessionPlayer author = players[j];
                    if (author.Id == target.Id)   continue; // свой курсор получатель знает лучше нас
                    if (author.Team != target.Team) continue; // чужая сторона — не его дело

                    if (_latest.TryGetValue(author.Id, out PresenceState state)) _outgoing.Add(state);
                }

                if (_outgoing.Count == 0) continue;

                _writer.Reset();
                PresenceCodec.Write(_writer, _outgoing);
                _transport.Send(target.Id,
                    NetEnvelope.Wrap(NetChannel.Presence, _writer.WrittenSegment, ref _envelope),
                    NetDelivery.Unreliable);
            }
        }
    }
}
