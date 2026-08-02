using System;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using Guildmaster.Net;
using Guildmaster.Net.Session;
using Guildmaster.Net.Transport;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Владельческая половина коопа на уровне забега: раздаёт гостям состояние снимком и принимает от них
    /// интенты.
    /// </summary>
    /// <remarks>
    /// <b>Снимок уходит там же, где пишется сейв.</b> Точки одни и те же намеренно: гость получает ровно
    /// то, что легло бы на диск, поэтому «у нас разные состояния» и «состояние разошлось с сейвом» — один
    /// и тот же баг, а не два разных. Плюс применение команды: между автосейвами их бывает несколько, и
    /// ждать перехода узла значило бы показывать гостю вчерашнее золото.
    /// <para><b>Снимок за кадр, а не за команду.</b> Серия правок в одном кадре (расстановка отряда)
    /// даёт один снимок: состояние идемпотентно, промежуточные никому не нужны, а платить за них
    /// пришлось бы трафиком.</para>
    /// <para><b>Интент гостя идёт в ту же шину, что и свой</b> — с его номером и его временем. Ради
    /// этого у шины есть вход для готовой команды: переприсвоить номер значило бы потерять
    /// идемпотентность, а вместе с ней и защиту от дублей после реконнекта.</para>
    /// </remarks>
    public sealed class RunStateBroadcast : ITickable, IDisposable
    {
        private readonly INetTransport   _transport;
        private readonly RunCommandBus   _bus;
        private readonly RunStateService _run;
        private readonly CoopHandshake   _handshake;

        private byte[] _envelope;
        private bool   _dirty;

        // Ни одного аргумента со значением по умолчанию: VContainer на таком ctor ищет регистрацию под
        // тип параметра и роняет всю ветку разрешения зависимостей.
        public RunStateBroadcast(INetTransport transport, RunCommandBus bus, RunStateService run,
                                 CoopHandshake handshake)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _bus       = bus       ?? throw new ArgumentNullException(nameof(bus));
            _run       = run       ?? throw new ArgumentNullException(nameof(run));
            _handshake = handshake ?? throw new ArgumentNullException(nameof(handshake));

            _bus.Applied   += HandleApplied;
            _run.Committed += HandleCommitted;

            _transport.MessageReceived += HandleMessage;
            _handshake.GuestApproved   += HandleGuestApproved;
        }

        /// <summary>Сколько снимков разослано и сколько чужих интентов принято. Видно в dev-панели.</summary>
        public int SnapshotsSent { get; private set; }
        public int IntentsAccepted { get; private set; }

        public void Tick()
        {
            if (!_dirty) return;
            _dirty = false;

            // Соло-владелец платит одним ветвлением: транспорт не поднят, слать некому.
            if (!_transport.IsRunning) return;

            SendSnapshot(NetPeer.NoPeer);
        }

        public void Dispose()
        {
            _bus.Applied   -= HandleApplied;
            _run.Committed -= HandleCommitted;

            _transport.MessageReceived -= HandleMessage;
            _handshake.GuestApproved   -= HandleGuestApproved;
        }

        private void HandleApplied(RunCommand command) => _dirty = true;

        private void HandleCommitted(RunState state) => _dirty = true;

        // Новый гость: ждать ближайшего изменения нельзя — до него он сидел бы без забега вовсе.
        private void HandleGuestApproved(int peerId)
        {
            if (!_transport.IsRunning) return;
            SendSnapshot(peerId);
        }

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.RunCommand) return;

            if (!RunCommandCodec.TryRead(payload, out RunCommand command))
            {
                Debug.LogError($"[RunStateBroadcast] - интент от пира {from} не разобран: у вас разные " +
                               "версии сборки. Команда НЕ применена.");
                return;
            }

            // Отправитель — тот, кто прислал, а не тот, кого он назвал: иначе подменить автора стоило бы
            // одного поля, а «кто передвинул» — смысл лога команд, а не украшение.
            if (command.PlayerId != from)
            {
                Debug.LogWarning($"[RunStateBroadcast] - пир {from} прислал команду от имени " +
                                 $"{command.PlayerId}: отброшена.");
                return;
            }

            if (_bus.Submit(in command)) IntentsAccepted++;
            // Не применилось — это либо дубль после реконнекта, либо применять было нечего. Оба случая
            // штатные: шина уже ответила «нет», и снимок гостю не нужен, у него ничего не изменилось.
        }

        private void SendSnapshot(int peerId)
        {
            RunState state = _run.Current;
            if (state == null) return; // забега нет — раздавать нечего

            ArraySegment<byte> payload = RunSnapshotCodec.Write(state);
            ArraySegment<byte> packet  = NetEnvelope.Wrap(NetChannel.RunSnapshot, payload, ref _envelope);

            // Предел надёжного сообщения не проверяет за нас никто: сверх него Steam возвращает отказ,
            // которого транспорт не читает, и снимок уезжает в тишину (ТЗ кооп-вертикали §5.2).
            if (packet.Count > _transport.MaxReliableMessageBytes)
            {
                Debug.LogError($"[RunStateBroadcast] - снимок забега {packet.Count} Б больше предела " +
                               $"{_transport.MaxReliableMessageBytes} Б: НЕ отправлен. Забег вырос — " +
                               "снимок пора резать на части.");
                return;
            }

            if (peerId == NetPeer.NoPeer) _transport.SendToAll(packet, NetDelivery.Reliable);
            else                          _transport.Send(peerId, packet, NetDelivery.Reliable);

            SnapshotsSent++;
        }
    }
}
