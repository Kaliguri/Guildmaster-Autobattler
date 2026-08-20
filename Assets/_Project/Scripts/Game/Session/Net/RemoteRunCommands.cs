using System;
using Guildmaster.Guild.Commands;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using UnityEngine;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Команды забега у гостя: тот же контракт, но вместо применения — отправка интента хосту. Состояние
    /// изменится тогда, когда хост применит команду и пришлёт снимок.
    /// </summary>
    /// <remarks>
    /// <b>Вызывающие про сеть не знают.</b> Ради этого <see cref="IRunCommands"/> и заводился: у глаголов
    /// нет ответа «вышло или нет», есть только «принято локально» — и здесь он честно
    /// <c>false</c>. Метод, возвращающий успех сразу, пришлось бы переписывать вместе со всеми
    /// вызывающими, как только появится второй игрок.
    /// <para><b>Номер команды гость назначает сам</b> (пара «игрок и его номер»), поэтому он не ждёт
    /// разрешения хоста, чтобы отправить следующую, а хост отбрасывает дубли после реконнекта по этому
    /// же ключу.</para>
    /// <para><b>Чего здесь сознательно нет — оптимистичного применения.</b> Гостю нечем применить
    /// команду: держателя состояния, который умеет меняться, у него нет вовсе (см.
    /// <see cref="GuestRunState"/>). Задержка видна на изменениях состояния — золото, снятый релик; сам
    /// жест (перетаскивание) отзывается сразу, потому что это показ, а не состояние.</para>
    /// </remarks>
    public sealed class RemoteRunCommands : ISessionRunCommands
    {
        private readonly INetTransport _transport;
        private readonly NetByteWriter _writer = new NetByteWriter(128);

        private byte[] _envelope;
        private int    _sequence;

        public RemoteRunCommands(INetTransport transport)
            => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        /// <summary>Сколько интентов отправлено. Видно в dev-панели.</summary>
        public int SentCount { get; private set; }

        public bool SetSlotPosition(int slotIndex, Vector2 position)
            => Send(Next(RunCommandKind.SetSlotPosition, slotIndex: slotIndex, x: position.x, y: position.y));

        public bool SetSlotRelic(int slotIndex, string relicId)
            => Send(Next(RunCommandKind.SetSlotRelic, slotIndex: slotIndex, text: relicId));

        public void AddGold(int delta) => Send(Next(RunCommandKind.AddGold, amount: delta));

        public void RemoveRelic(string relicId) => Send(Next(RunCommandKind.RemoveRelic, text: relicId));

        public void AwardBattleReward() => Send(Next(RunCommandKind.AwardBattleReward));

        // Клик гостя по узлу карты: та же команда, что и у хозяина, тем же каналом.
        public void ChooseNode(string nodeId) => Send(Next(RunCommandKind.ChooseNode, text: nodeId));

        /// <summary>
        /// Гостю сохранять нечего и некуда: забег не его, и пишет его хост в свою точку автосейва.
        /// <c>false</c> здесь — не отказ, а факт, и вызывающие его уже читают.
        /// </summary>
        public bool RequestSave() => false;

        private bool Send(in RunCommand command)
        {
            if (!_transport.IsRunning)
            {
                // Гостевой состав без живого соединения — это разъехавшаяся разводка, а не штатный
                // случай: гостем становятся ПОСЛЕ рукопожатия. Молча проглотить интент значило бы
                // искать потом «почему у меня ничего не двигается».
                Debug.LogError("[RemoteRunCommands] - соединения нет, а команда забега уже отправляется: " +
                               $"{command}. Гостевой сеанс открыт без транспорта.");
                return false;
            }

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.RunCommand, RunCommandCodec.Write(in command, _writer), ref _envelope),
                NetDelivery.Reliable);
            SentCount++;

            // Локально не применено ничем — состояние приедет снимком.
            return false;
        }

        private RunCommand Next(RunCommandKind kind, int slotIndex = -1, int amount = 0,
            string text = null, float x = 0f, float y = 0f) =>
            new RunCommand(kind, _transport.LocalPeerId, _sequence++,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), slotIndex, amount, text, x, y);
    }
}
