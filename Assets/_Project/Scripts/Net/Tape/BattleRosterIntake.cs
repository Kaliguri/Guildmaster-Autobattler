using System;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Net.Transport;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Гостевая половина состава боя: складывает присланные паспорта в свой
    /// <see cref="BattleUnitRegistry"/>, чтобы показу было чем рисовать бойцов.
    /// </summary>
    /// <remarks>
    /// <b>Пустой id — законный случай:</b> так спавнятся болванчики dev-боёв, у которых определения нет
    /// вовсе. Показ у них берёт умолчания, и отказывать здесь не за что. А вот НЕизвестный id —
    /// расхождение контента, и это громко: рисовать вместо юнита нечего.
    /// <para><b>Собирается только у гостя</b> (см. <c>CombatLifetimeScope</c>): у владельца состав
    /// приходит событием спавна собственной симуляции, и второй источник тех же паспортов означал бы
    /// два владельца одного факта.</para>
    /// </remarks>
    public sealed class BattleRosterIntake : IStartable, IDisposable
    {
        private readonly INetTransport      _transport;
        private readonly BattleUnitRegistry _registry;
        private readonly IContentDatabase   _content;

        public BattleRosterIntake(INetTransport transport, BattleUnitRegistry registry,
                                  IContentDatabase content)
        {
            _transport = transport;
            _registry  = registry;
            _content   = content;
        }

        /// <summary>Сколько паспортов принято — видно в dev-панели.</summary>
        public int ReceivedCount { get; private set; }

        public void Start() => _transport.MessageReceived += HandleMessage;

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.BattleRoster) return;

            var bytes = new NetByteReader(payload);

            int    id        = bytes.ReadInt();
            int    team      = bytes.ReadByte();
            string contentId = bytes.ReadString();

            UnitData definition = null;
            if (!string.IsNullOrEmpty(contentId) && !_content.TryGet(contentId, out definition))
            {
                Debug.LogError($"[BattleRosterIntake] В составе боя юнит '{contentId}', которого нет в " +
                               "реестре контента — у игроков разный контент, и показать его нечем.");
                return;
            }

            _registry.RegisterRemote(id, definition, team);
            ReceivedCount++;
        }
    }
}
