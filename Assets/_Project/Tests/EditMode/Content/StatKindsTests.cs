using System;
using System.Linq;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Таблица размерностей статов <see cref="StatKinds"/>: инварианты, которые комментарием не
    /// удержать — нарушаются они в ДРУГОМ файле, когда в <see cref="StatType"/> добавляют стат.
    /// </summary>
    public sealed class StatKindsTests
    {
        /// <summary>
        /// Стат с именем «…RegenFlat» — слагаемое к СКОРОСТИ, а не разовая порция: <c>RegenSystem</c>
        /// складывает такой стат с величиной в секунду и умножает на dt.
        /// </summary>
        /// <remarks>
        /// Тест ловит пропуск в таблице: стат, не попавший в switch, молча уезжает в ветку default и
        /// становится Flat. Так и случилось с <c>ResourceRegenFlat</c> — мана-дрейн печатался как
        /// «-4» вместо «-4/с», то есть разовым списанием вместо скорости.
        /// </remarks>
        [Test]
        public void EveryRegenFlatStat_IsPerSecond()
        {
            StatType[] offenders = Enum.GetValues(typeof(StatType))
                .Cast<StatType>()
                .Where(s => s.ToString().EndsWith("RegenFlat", StringComparison.Ordinal))
                .Where(s => StatKinds.KindOf(s) != ValueKind.PerSecond)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "Стат «…RegenFlat» показывается как величина в секунду. Не объявлены PerSecond в StatKinds: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// Множители вокруг 1.0 («…Eff») показываются как <see cref="ValueKind.Multiplier"/>: игрок
        /// читает «×1.15», а не «1.15» и тем более не «115 %».
        /// </summary>
        [Test]
        public void EveryEffStat_IsMultiplier()
        {
            StatType[] offenders = Enum.GetValues(typeof(StatType))
                .Cast<StatType>()
                .Where(s => s.ToString().EndsWith("Eff", StringComparison.Ordinal))
                .Where(s => StatKinds.KindOf(s) != ValueKind.Multiplier)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "Стат «…Eff» — множитель вокруг 1.0. Не объявлены Multiplier в StatKinds: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// Доли 0..1 («…Pct») показываются процентом, иначе игрок увидит «0.35» вместо «35 %».
        /// </summary>
        [Test]
        public void EveryPctStat_IsPercent()
        {
            StatType[] offenders = Enum.GetValues(typeof(StatType))
                .Cast<StatType>()
                .Where(s => s.ToString().EndsWith("Pct", StringComparison.Ordinal))
                .Where(s => StatKinds.KindOf(s) != ValueKind.Percent)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "Стат «…Pct» — доля 0..1, показывается процентом. Не объявлены Percent в StatKinds: "
                + string.Join(", ", offenders));
        }
    }
}
