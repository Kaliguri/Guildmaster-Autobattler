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
    /// Гостевая половина присутствия: отправляет свой курсор хосту и показывает те, что хост прислал.
    /// </summary>
    /// <remarks>
    /// <b>Гость не решает, кого ему видно.</b> Пришедший пакет уже отобран по стороне — это работа хоста,
    /// и единственная, где отбор что-то значит (см. <see cref="HostPresence"/>). Своего фильтра здесь нет
    /// намеренно: он создавал бы впечатление защиты, которой на клиенте быть не может.
    /// </remarks>
    public sealed class GuestPresence : IPresenceView, IStartable, ITickable, IDisposable
    {
        private readonly INetTransport  _transport;
        private readonly IPointerWorld  _pointer;
        private readonly ISessionRoster _roster;

        private readonly PresenceCursors _cursors = new PresenceCursors();
        private readonly PresenceSender  _sender  = new PresenceSender();

        private readonly List<PresenceState> _outgoing = new List<PresenceState>(1);
        private readonly List<PresenceState> _incoming = new List<PresenceState>(4);
        private readonly NetByteWriter       _writer   = new NetByteWriter(64);
        private byte[] _envelope;

        public GuestPresence(INetTransport transport, IPointerWorld pointer, ISessionRoster roster)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _pointer   = pointer;
            _roster    = roster;
        }

        public int Count => _cursors.Count;

        public RemoteCursor this[int index] => _cursors[index];

        public void Start() => _transport.MessageReceived += OnMessage;

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        public void Tick()
        {
            float now = Time.unscaledTime;
            int   me  = _roster?.LocalId ?? _transport.LocalPeerId;

            if (_transport.IsRunning && _pointer != null && _pointer.IsAvailable &&
                _sender.TrySample(_pointer.Position, me, now, out PresenceState mine))
            {
                _outgoing.Clear();
                _outgoing.Add(mine);

                _writer.Reset();
                PresenceCodec.Write(_writer, _outgoing);
                _transport.Send(NetPeer.HostPeerId,
                    NetEnvelope.Wrap(NetChannel.Presence, _writer.WrittenSegment, ref _envelope),
                    NetDelivery.Unreliable);
            }

            _cursors.Sample(now, _roster, me);
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.Presence) return;

            // Присутствие раздаёт хост: пакет от другого гостя означал бы курсоры, никем не отобранные.
            if (from != NetPeer.HostPeerId) return;
            if (!PresenceCodec.TryRead(payload, _incoming)) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < _incoming.Count; i++)
            {
                PresenceState state = _incoming[i];
                _cursors.Push(in state, now);
            }
        }
    }
}
