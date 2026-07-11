using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Геометрия блинка «за спину» (общий хелпер монаха §10.6 и убийцы §10.5): атакующий встаёт на
    /// ДАЛЬНЮЮ от себя сторону цели, на расстоянии половины радиуса атаки; вид снапает (PreviousPosition = старая).
    /// </summary>
    public sealed class CombatPositioningTests
    {
        private static RuntimeUnit Make(Vector2 pos, float attackRange)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.AttackRange, ModifierOp.Flat, attackRange) });
            return new RuntimeUnit { Stats = stats, Position = pos, PreviousPosition = pos };
        }

        [Test]
        public void TeleportBehind_PlacesAttackerOnFarSide_AtHalfRange()
        {
            var attacker = Make(new Vector2(0f, 0f), attackRange: 2f); // offset = 1
            var target   = Make(new Vector2(5f, 0f), attackRange: 2f);

            CombatPositioning.TeleportBehind(attacker, target);

            // Дальняя сторона по направлению атакующий→цель (+X): цель(5) + dir(1,0)*1 = (6,0).
            Assert.That(attacker.Position.x, Is.EqualTo(6f).Within(1e-4f));
            Assert.That(attacker.Position.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.AreEqual(new Vector2(0f, 0f), attacker.PreviousPosition, "Вид должен снапнуть: PreviousPosition = прежняя позиция.");
        }

        [Test]
        public void TeleportBehind_NullTarget_NoThrow()
        {
            var attacker = Make(Vector2.zero, 2f);
            Assert.DoesNotThrow(() => CombatPositioning.TeleportBehind(attacker, null));
        }
    }
}
