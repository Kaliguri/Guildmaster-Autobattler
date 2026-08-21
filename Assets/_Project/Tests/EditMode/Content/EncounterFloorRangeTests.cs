using System.Reflection;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Диапазон этажей энкаунтера (решение 2026-08-21, ГДД <c>act-1-encounters</c>): вторая ось рядом с
    /// тиром, отвечающая не «что это за узел», а «когда этот бой уместен».
    /// <para>Тест держит две вещи, которые ломаются снаружи и молча: незаданный диапазон обязан читаться
    /// как «где угодно» (иначе все ассеты, заведённые до поля, выпадут из пулов разом и карта встанет на
    /// заглушках), и верхняя граница обязана быть включительной — бой «этажи 1-4» на четвёртом этаже ещё
    /// уместен.</para>
    /// </summary>
    public sealed class EncounterFloorRangeTests
    {
        [Test]
        public void UnsetRange_MeansAnywhere()
        {
            EncounterData encounter = Encounter(minFloor: 0, maxFloor: 0);

            Assert.That(encounter.FitsFloor(0), Is.True);
            Assert.That(encounter.FitsFloor(7), Is.True);
            Assert.That(encounter.FitsFloor(14), Is.True);
        }

        [Test]
        public void BothBoundsAreInclusive()
        {
            EncounterData early = Encounter(minFloor: 1, maxFloor: 4);

            Assert.That(early.FitsFloor(0), Is.False, "До нижней границы — рано.");
            Assert.That(early.FitsFloor(1), Is.True,  "Нижняя граница входит.");
            Assert.That(early.FitsFloor(4), Is.True,  "Верхняя тоже: «этажи 1-4» включает четвёртый.");
            Assert.That(early.FitsFloor(5), Is.False, "А пятый уже нет.");
        }

        /// <summary>Ноль сверху — «и дальше без предела»: так живут поздние бои и босс.</summary>
        [Test]
        public void ZeroUpperBound_LeavesTheCeilingOpen()
        {
            EncounterData late = Encounter(minFloor: 5, maxFloor: 0);

            Assert.That(late.FitsFloor(4), Is.False);
            Assert.That(late.FitsFloor(5), Is.True);
            Assert.That(late.FitsFloor(99), Is.True);
        }

        /// <summary>
        /// Пресет без энкаунтера уместен везде. Ограничивать там нечего, а спрятать его молча значило бы
        /// выкинуть дев-бой из пула по чужой причине.
        /// </summary>
        [Test]
        public void PresetWithoutEncounter_FitsEveryFloor()
        {
            var preset = ScriptableObject.CreateInstance<BattlePresetData>();

            Assert.That(preset.FitsFloor(0), Is.True);
            Assert.That(preset.FitsFloor(12), Is.True);
        }

        [Test]
        public void PresetAsksItsEncounter()
        {
            var preset = ScriptableObject.CreateInstance<BattlePresetData>();
            Set(preset, "_encounter", Encounter(minFloor: 5, maxFloor: 12));

            Assert.That(preset.FitsFloor(3), Is.False);
            Assert.That(preset.FitsFloor(9), Is.True);
        }

        private static EncounterData Encounter(int minFloor, int maxFloor)
        {
            var e = ScriptableObject.CreateInstance<EncounterData>();
            Set(e, "_minFloor", minFloor);
            Set(e, "_maxFloor", maxFloor);
            return e;
        }

        private static void Set(Object target, string field, object value) =>
            target.GetType()
                  .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(target, value);
    }
}
