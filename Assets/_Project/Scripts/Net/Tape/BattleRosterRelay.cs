using System;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Net;
using Guildmaster.Data.Definitions;
using Guildmaster.Net.Transport;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Состав боя по сети: хост объявляет каждого вышедшего на арену, гость складывает паспорта в свой
    /// <see cref="BattleUnitRegistry"/>.
    /// </summary>
    /// <remarks>
    /// <b>Почему это не едет в ленте.</b> Снимки несут то, что меняется каждый тик; определение юнита,
    /// команда и арт за бой не меняются, и слать их тридцать раз в секунду значило бы платить за
    /// неизменное. В соло ту же роль играет событие спавна — у гостя его нет, потому что нет и
    /// симуляции.
    /// <para><b>Порядок гарантирован самим устройством раздачи:</b> паспорт уходит в момент спавна, а
    /// кадр с этим юнитом — только когда наберётся чанк (тридцать тиков). Оба сообщения надёжные и
    /// упорядоченные, так что «кадр раньше паспорта» не случается.</para>
    /// <para><b>Пустой id — законный случай:</b> так спавнятся болванчики dev-боёв, у которых
    /// определения нет вовсе. Показ у них берёт умолчания, и отказывать здесь не за что. А вот
    /// НЕизвестный id — расхождение контента, и это громко: рисовать вместо юнита нечего.</para>
    /// </remarks>
    public sealed class BattleRosterRelay : IStartable, IDisposable
    {
        private readonly INetTransport      _transport;
        private readonly CombatSimulation   _simulation;
        private readonly BattleUnitRegistry _registry;
        private readonly IContentDatabase   _content;
        private readonly IBattleAuthority   _authority;

        private readonly NetByteWriter _writer = new NetByteWriter(64);
        private byte[] _envelope;

        public BattleRosterRelay(INetTransport transport, CombatSimulation simulation,
                                 BattleUnitRegistry registry, IContentDatabase content,
                                 IBattleAuthority authority)
        {
            _transport  = transport;
            _simulation = simulation;
            _registry   = registry;
            _content    = content;
            _authority  = authority;
        }

        /// <summary>Сколько паспортов объявлено (хост) или принято (гость) — видно в dev-панели.</summary>
        public int AnnouncedCount { get; private set; }

        public void Start()
        {
            _simulation.OnUnitSpawned  += HandleSpawn;
            _transport.MessageReceived += HandleMessage;
        }

        public void Dispose()
        {
            _simulation.OnUnitSpawned  -= HandleSpawn;
            _transport.MessageReceived -= HandleMessage;
        }

        private void HandleSpawn(RuntimeUnit unit)
        {
            if (_authority.Role != BattleRole.Host) return;

            _writer.Reset();
            _writer.WriteInt(unit.Id);
            _writer.WriteByte((byte)unit.Team);
            _writer.WriteString(unit.Unit != null ? unit.Unit.Id : null);

            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.BattleRoster, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
            AnnouncedCount++;
        }

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (_authority.Role != BattleRole.Guest) return;
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.BattleRoster) return;

            var bytes = new NetByteReader(payload);

            int    id        = bytes.ReadInt();
            int    team      = bytes.ReadByte();
            string contentId = bytes.ReadString();

            UnitData definition = null;
            if (!string.IsNullOrEmpty(contentId) && !_content.TryGet(contentId, out definition))
            {
                Debug.LogError($"[BattleRosterRelay] В составе боя юнит '{contentId}', которого нет в " +
                               "реестре контента — у игроков разный контент, и показать его нечем.");
                return;
            }

            _registry.RegisterRemote(id, definition, team);
            AnnouncedCount++;
        }
    }
}
