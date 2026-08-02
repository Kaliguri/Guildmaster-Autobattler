using System;
using Guildmaster.Combat;
using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Владельческая половина состава боя: объявляет гостям каждого, кто вышел на арену.
    /// </summary>
    /// <remarks>
    /// <b>Почему это не едет в ленте.</b> Снимки несут то, что меняется каждый тик; определение юнита,
    /// команда и арт за бой не меняются, и слать их тридцать раз в секунду значило бы платить за
    /// неизменное. В соло ту же роль играет событие спавна — у гостя его нет, потому что нет и
    /// симуляции.
    /// <para><b>Порядок гарантирован самим устройством раздачи:</b> паспорт уходит в момент спавна, а
    /// кадр с этим юнитом — только когда наберётся чанк (тридцать тиков). Оба сообщения надёжные и
    /// упорядоченные, так что «кадр раньше паспорта» не случается.</para>
    /// <para><b>Собирается только у владельца сеанса</b> (см. <c>CombatLifetimeScope</c>), поэтому
    /// «а не гость ли я» здесь не спрашивается. В соло транспорт не поднят, и отправка сводится к
    /// одному ветвлению внутри него.</para>
    /// </remarks>
    public sealed class BattleRosterAnnouncer : IStartable, IDisposable
    {
        private readonly INetTransport    _transport;
        private readonly CombatSimulation _simulation;

        private readonly NetByteWriter _writer = new NetByteWriter(64);
        private byte[] _envelope;

        public BattleRosterAnnouncer(INetTransport transport, CombatSimulation simulation)
        {
            _transport  = transport;
            _simulation = simulation;
        }

        /// <summary>Сколько паспортов объявлено — видно в dev-панели.</summary>
        public int AnnouncedCount { get; private set; }

        public void Start() => _simulation.OnUnitSpawned += HandleSpawn;

        public void Dispose() => _simulation.OnUnitSpawned -= HandleSpawn;

        private void HandleSpawn(RuntimeUnit unit)
        {
            if (!_transport.IsRunning) return; // соло: объявлять некому

            _writer.Reset();
            _writer.WriteInt(unit.Id);
            _writer.WriteByte((byte)unit.Team);
            _writer.WriteString(unit.Unit != null ? unit.Unit.Id : null);

            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.BattleRoster, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
            AnnouncedCount++;
        }
    }
}
