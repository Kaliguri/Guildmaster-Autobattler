using Guildmaster.Core.Arena;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Намерение игрока на арене по проводу: кто, какого бойца и куда ставит.
    /// </summary>
    /// <remarks>
    /// Формат жил без прогона потому, что кодек был закрыт от тестов, — и это то же положение дел, что
    /// у канала состава сеанса, который 08.08.2026 разъехался и унёс с собой весь список участников.
    /// <para><b>Автор намерения проверяется отдельно и первым:</b> из него у хозяина выводится право
    /// двигать бойца, и потерянное в дороге поле дало бы «неизвестного» — то есть чужие права.</para>
    /// </remarks>
    public sealed class DeploymentIntentCodecTests
    {
        [Test]
        public void Intent_SurvivesTheRoundTrip_WithEveryField()
        {
            var sent = new UnitMoveIntent(unitId: 17, new Vector2(-2.5f, 1.25f), playerId: 1);

            var writer = new NetByteWriter(32);
            DeploymentIntentCodec.Write(writer, in sent);

            UnitMoveIntent got = DeploymentIntentCodec.Read(new NetByteReader(writer.WrittenSegment));

            Assert.AreEqual(sent.PlayerId, got.PlayerId, "автор намерения — из него выводится право");
            Assert.AreEqual(sent.UnitId,   got.UnitId,   "кого двигаем");
            Assert.AreEqual(sent.Position.x, got.Position.x, 1e-6f, "куда, по горизонтали");
            Assert.AreEqual(sent.Position.y, got.Position.y, 1e-6f, "куда, по вертикали");
        }

        /// <summary>
        /// Координаты едут как есть, без квантования: расстановка — не лента, здесь точка одна и
        /// сжимать её незачем. Отрицательные и дробные значения обязаны доехать без потерь.
        /// </summary>
        [Test]
        public void NegativeAndFractionalPosition_SurvivesExactly()
        {
            var sent = new UnitMoveIntent(0, new Vector2(-8.9375f, -0.03125f), 0);

            var writer = new NetByteWriter(32);
            DeploymentIntentCodec.Write(writer, in sent);

            UnitMoveIntent got = DeploymentIntentCodec.Read(new NetByteReader(writer.WrittenSegment));

            Assert.AreEqual(sent.Position.x, got.Position.x, "точность сохранена побитово");
            Assert.AreEqual(sent.Position.y, got.Position.y, "точность сохранена побитово");
        }
    }
}
