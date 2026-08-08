using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Net;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Перепись сетевых каналов: у каждого объявлен владелец формата, а у выделенного кодека —
    /// прогон туда-обратно.
    /// </summary>
    /// <remarks>
    /// <b>Гейт заведён 08.08.2026 по заказу Макса: «посадить все на ОДНИ рельсы».</b> Поводом стала
    /// поломка, которую нечем было поймать: у канала состава сеанса выделенного кодека не было —
    /// писатель жил в файле хозяина, читатель в файле гостя, — и правка тронула одну половину.
    /// Хозяин стал писать шесть полей на участника, гость читать пять. Состав перестал доезжать
    /// вовсе, при зелёной компиляции и зелёных тестах.
    /// <para><b>Почему перепись, а не автоматическая проверка «есть класс *Codec».</b> Формат части
    /// каналов честно живёт внутри одного класса (рукопожатие, управление боем), и требовать от них
    /// отдельного кодека значило бы плодить пустые обёртки. Но канал, у которого владелец формата не
    /// назван ВООБЩЕ, — это ровно тот случай, с которого начался разъезд. Поэтому список каналов
    /// выводится из перечисления, а не переписывается рядом: новый канал обязан получить строку
    /// здесь, иначе тест красный.</para>
    /// <para>Тот же приём уже держит таблицу известных каналов в <see cref="NetEnvelope"/>: владелец
    /// факта «какие каналы бывают» ровно один — само перечисление.</para>
    /// </remarks>
    public sealed class NetChannelFormatRegistryTests
    {
        /// <summary>Кто владеет форматом канала и чем это покрыто.</summary>
        private readonly struct FormatOwner
        {
            /// <summary>Полное имя типа, где лежит формат. Ищется рефлексией — опечатка роняет тест.</summary>
            public readonly string TypeName;

            /// <summary>Выделенный кодек: обе половины формата в одном классе. Такой обязан иметь прогон.</summary>
            public readonly bool IsDedicatedCodec;

            /// <summary>Класс тестов с прогоном туда-обратно. Пусто — только для не-кодеков.</summary>
            public readonly string TestClassName;

            public FormatOwner(string typeName, bool isDedicatedCodec, string testClassName)
            {
                TypeName         = typeName;
                IsDedicatedCodec = isDedicatedCodec;
                TestClassName    = testClassName;
            }
        }

        private static readonly Dictionary<NetChannel, FormatOwner> Registry =
            new Dictionary<NetChannel, FormatOwner>
            {
                [NetChannel.TapeChunk] = new FormatOwner(
                    "Guildmaster.Net.Tape.TapeChunkFormat", false, "TapeChunkCodecTests"),

                [NetChannel.TapeResend] = new FormatOwner(
                    "Guildmaster.Net.Tape.TapeIntake", false, "TapeDeliveryTests"),

                [NetChannel.Presence] = new FormatOwner(
                    "Guildmaster.Net.Presence.PresenceCodec", true, "PresenceTests"),

                [NetChannel.RunCommand] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.RunCommandCodec", true, "GuestRunSyncTests"),

                [NetChannel.BattleControl] = new FormatOwner(
                    "Guildmaster.Net.BattleControlRelay", false, "BattleControlRelayTests"),

                [NetChannel.Handshake] = new FormatOwner(
                    "Guildmaster.Net.Session.CoopHandshake", false, "HandshakeDropTests"),

                [NetChannel.BattleRoster] = new FormatOwner(
                    "Guildmaster.Net.Tape.BattleRosterAnnouncer", false, "ArenaUnitsSeamTests"),

                [NetChannel.RunSnapshot] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.RunSnapshotCodec", true, "GuestRunSyncTests"),

                [NetChannel.ActivityState] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.ActivityStateCodec", true, "ActivityStateTests"),

                [NetChannel.Decision] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.DecisionCodec", true, "DecisionCodecTests"),

                [NetChannel.SessionRoster] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.SessionRosterCodec", true, "SessionRosterCodecTests"),

                [NetChannel.DeploymentIntent] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.DeploymentIntentCodec", true, "DeploymentIntentCodecTests"),

                [NetChannel.NodeStage] = new FormatOwner(
                    "Guildmaster.Game.Session.Net.NodeStageCodec", true, "NodeStageTests"),
            };

        /// <summary>
        /// Новый канал обязан объявить владельца формата. Пропуск здесь — это ровно тот канал, у
        /// которого писатель и читатель разъедутся по разным файлам и разойдутся молча.
        /// </summary>
        [Test]
        public void EveryChannel_DeclaresWhoOwnsItsFormat()
        {
            var missing = ((NetChannel[])Enum.GetValues(typeof(NetChannel)))
                .Where(channel => !Registry.ContainsKey(channel))
                .ToList();

            Assert.IsEmpty(missing,
                "канал есть, а владельца формата у него нет — допиши строку в реестр этого теста: " +
                string.Join(", ", missing));
        }

        /// <summary>Реестр не должен пережить удалённый канал: мёртвая строка врёт так же, как забытая.</summary>
        [Test]
        public void Registry_HasNoStaleChannels()
        {
            var known = new HashSet<NetChannel>((NetChannel[])Enum.GetValues(typeof(NetChannel)));
            var stale = Registry.Keys.Where(channel => !known.Contains(channel)).ToList();

            Assert.IsEmpty(stale, "в реестре канал, которого больше нет в перечислении");
        }

        [Test]
        public void EveryDeclaredOwner_Exists()
        {
            foreach (KeyValuePair<NetChannel, FormatOwner> entry in Registry)
                Assert.IsNotNull(FindType(entry.Value.TypeName),
                    $"канал {entry.Key}: владелец формата «{entry.Value.TypeName}» не найден — " +
                    "тип переименован или переехал, а реестр остался");
        }

        /// <summary>
        /// Выделенный кодек существует ради одного: чтобы обе половины формата правились вместе и
        /// сверялись прогоном. Кодек без прогона — это просто два файла в одном, и он разъедется так же.
        /// </summary>
        [Test]
        public void EveryDedicatedCodec_HasARoundTripTest()
        {
            Assembly here = typeof(NetChannelFormatRegistryTests).Assembly;

            foreach (KeyValuePair<NetChannel, FormatOwner> entry in Registry)
            {
                if (!entry.Value.IsDedicatedCodec) continue;

                Assert.IsNotEmpty(entry.Value.TestClassName, $"канал {entry.Key}: кодек без прогона");

                bool found = here.GetTypes().Any(type => type.Name == entry.Value.TestClassName);
                Assert.IsTrue(found,
                    $"канал {entry.Key}: класс тестов «{entry.Value.TestClassName}» не найден");
            }
        }

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false))
                .FirstOrDefault(type => type != null);
    }
}
