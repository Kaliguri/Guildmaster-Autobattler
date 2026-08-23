using Guildmaster.Core.Arena;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Намерения игрока на арене по проводу: «поставь бойца сюда» и «надень на него мементо».
    /// </summary>
    /// <remarks>
    /// Формат жил без прогона потому, что кодек был закрыт от тестов, — то же положение дел, что у
    /// канала состава сеанса, который 08.08.2026 разъехался и унёс с собой весь список участников.
    /// <para><b>Автор намерения проверяется отдельно и первым:</b> из него у хозяина выводится право
    /// двигать бойца, и потерянное в дороге поле дало бы «неизвестного» — то есть чужие права.</para>
    /// </remarks>
    public sealed class DeploymentIntentCodecTests
    {
        [Test]
        public void Move_SurvivesTheRoundTrip_WithEveryField()
        {
            var sent = new UnitMoveIntent(unitId: 17, new Vector2(-2.5f, 1.25f), playerId: 1);

            var writer = new NetByteWriter(32);
            Assert.IsTrue(DeploymentIntentCodec.TryRead(
                DeploymentIntentCodec.WriteMove(writer, in sent), out DeploymentIntent got));

            Assert.IsFalse(got.IsEquip, "вид намерения объявляет первый байт");
            Assert.AreEqual(sent.PlayerId, got.Move.PlayerId, "автор — из него выводится право");
            Assert.AreEqual(sent.UnitId,   got.Move.UnitId,   "кого двигаем");
            Assert.AreEqual(sent.Position.x, got.Move.Position.x, 1e-6f, "куда, по горизонтали");
            Assert.AreEqual(sent.Position.y, got.Move.Position.y, 1e-6f, "куда, по вертикали");
        }

        /// <summary>
        /// Мементо едет строковым id: по проводу ездит контент, а не ссылки на ассеты. До 08.08.2026
        /// этот путь не существовал вовсе — жест у гостя уходил в шину без подписчика.
        /// </summary>
        [Test]
        public void Equip_SurvivesTheRoundTrip()
        {
            var writer = new NetByteWriter(32);
            Assert.IsTrue(DeploymentIntentCodec.TryRead(
                DeploymentIntentCodec.WriteEquip(writer, unitId: 5, relicId: "relic.flame_swordsman"),
                out DeploymentIntent got));

            Assert.IsTrue(got.IsEquip);
            Assert.AreEqual(5, got.UnitId);
            Assert.AreEqual("relic.flame_swordsman", got.RelicId);
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
            Assert.IsTrue(DeploymentIntentCodec.TryRead(
                DeploymentIntentCodec.WriteMove(writer, in sent), out DeploymentIntent got));

            Assert.AreEqual(sent.Position.x, got.Move.Position.x, "точность сохранена побитово");
            Assert.AreEqual(sent.Position.y, got.Move.Position.y, "точность сохранена побитово");
        }

        /// <summary>Неизвестный вид намерения — расхождение сборок: исполнять «примерно то же» нельзя.</summary>
        [Test]
        public void UnknownKind_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(200);
            writer.WriteInt(1);

            Assert.IsFalse(DeploymentIntentCodec.TryRead(writer.WrittenSegment, out _));
        }

        [Test]
        public void TruncatedMove_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(0);
            writer.WriteInt(7);   // а координат и автора нет

            Assert.IsFalse(DeploymentIntentCodec.TryRead(writer.WrittenSegment, out _));
        }

        /// <summary>Пустой id мементо надеть нечем — это битый пакет, а не «снять мементо».</summary>
        [Test]
        public void EquipWithoutRelicId_IsRefused()
        {
            var writer = new NetByteWriter(16);
            Assert.IsFalse(DeploymentIntentCodec.TryRead(
                DeploymentIntentCodec.WriteEquip(writer, 3, string.Empty), out _));
        }
    }
}
