using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Конвертация статов в числа (M4): одна формула для параметров способности, перезарядки зарядов и
    /// силы усиления. Проверяется главное обещание — база значит то, что написано, а обратная форма
    /// никогда не доходит до нуля, поэтому капа ей не нужно.
    /// </summary>
    public sealed class StatConversionTests
    {
        /// <summary>Читатель статов на одном значении — конвертации больше ничего и не нужно.</summary>
        private sealed class OneStat : IStatReader
        {
            private readonly StatType _stat;
            private readonly float    _value;

            public OneStat(StatType stat, float value) { _stat = stat; _value = value; }

            public float Get(StatType stat) => stat == _stat ? _value : 0f;
        }

        [Test]
        public void AtBaseline_ValueIsExactlyTheAuthoredBase()
        {
            // Иначе число в ассете нельзя прочитать глазами: «каст 0.5 с» должно означать 0.5 с
            // у нормального носителя кита, а не «0.5 при нулевой скорости атаки».
            var rule = new StatConversion
            {
                Source = StatType.AttackSpeed, Form = ScalingForm.Inverse, Baseline = 0.6f, PerUnit = 0.3f,
            };

            Assert.AreEqual(0.5f, rule.Apply(0.5f, new OneStat(StatType.AttackSpeed, 0.6f)), 1e-4f);
        }

        [Test]
        public void BelowBaseline_NeverMakesTheAbilityWorse()
        {
            // Просадка стата не должна наказывать: иначе замедленный копейщик кастовал бы ДОЛЬШЕ базы.
            var rule = new StatConversion
            {
                Source = StatType.AttackSpeed, Form = ScalingForm.Inverse, Baseline = 0.6f, PerUnit = 0.3f,
            };

            Assert.AreEqual(0.5f, rule.Apply(0.5f, new OneStat(StatType.AttackSpeed, 0.2f)), 1e-4f);
        }

        [Test]
        public void Inverse_ShortensButNeverReachesZero()
        {
            // Ради этого свойства форма и выбрана (решения по Убийце и Магу молний): кулдаун, который
            // не может стать нулём, не требует отдельного капа — а кап всегда забывают выставить.
            var rule = new StatConversion
            {
                Source = StatType.AttackSpeed, Form = ScalingForm.Inverse, Baseline = 1f, PerUnit = 0.25f,
            };

            float atTwo  = rule.Apply(5f, new OneStat(StatType.AttackSpeed, 2f));
            float atFour = rule.Apply(5f, new OneStat(StatType.AttackSpeed, 4f));
            float atHuge = rule.Apply(5f, new OneStat(StatType.AttackSpeed, 1000f));

            Assert.AreEqual(4f,    atTwo,  1e-3f, "AS 2.0 → 5 / 1.25 = 4 с");

            // Карточка Убийцы (§3.2 плана) обещает здесь 3.3 с, но при базе 1.0 и коэффициенте 0.25
            // формула даёт 5 / 1.75 ≈ 2.86. Ошибка в карточке, а не в коде: 3.3 вышло бы при 0.167.
            // Тест закрепляет формулу; число в карточке — на вердикт Макса.
            Assert.AreEqual(2.857f, atFour, 1e-2f, "AS 4.0 → 5 / 1.75 ≈ 2.86 с");
            Assert.Greater(atHuge, 0f, "Сколько бы ни было стата, ноль недостижим");
            Assert.Less(atHuge, atFour);
        }

        [Test]
        public void Linear_AddsPerUnitOfExcess()
        {
            // «Удар из скрытности: ×2.0 плюс 0.5 за каждую 1.0 AS свыше базовой».
            var rule = new StatConversion
            {
                Source = StatType.AttackSpeed, Form = ScalingForm.Linear, Baseline = 1f, PerUnit = 0.5f,
            };

            Assert.AreEqual(2f,   rule.Apply(2f, new OneStat(StatType.AttackSpeed, 1f)),  1e-4f);
            Assert.AreEqual(2.5f, rule.Apply(2f, new OneStat(StatType.AttackSpeed, 2f)),  1e-4f);
            Assert.AreEqual(3f,   rule.Apply(2f, new OneStat(StatType.AttackSpeed, 3f)),  1e-4f);
        }

        [Test]
        public void NoRules_OrNoReader_LeavesTheBaseAlone()
        {
            // Весь текущий контент живёт без конвертаций, и это обязано быть ровно прежним поведением.
            Assert.AreEqual(7f, StatConversion.ApplyAll(null, 7f, new OneStat(StatType.AttackSpeed, 5f)), 1e-4f);
            Assert.AreEqual(7f, StatConversion.ApplyAll(System.Array.Empty<StatConversion>(), 7f, null), 1e-4f);

            var rule = new StatConversion
            {
                Source = StatType.AttackSpeed, Form = ScalingForm.Linear, Baseline = 0f, PerUnit = 1f,
            };
            Assert.AreEqual(7f, rule.Apply(7f, stats: null), 1e-4f, "Без читателя статов конвертировать нечего");
        }

        [Test]
        public void ApplyAll_ChainsRulesInAuthoredOrder()
        {
            // Порядок значим у обратной формы, поэтому он берётся из данных: одно и то же содержимое
            // ассета обязано давать одно и то же число.
            var rules = new[]
            {
                new StatConversion { Source = StatType.AttackSpeed, Form = ScalingForm.Linear,  Baseline = 1f, PerUnit = 1f },
                new StatConversion { Source = StatType.AttackSpeed, Form = ScalingForm.Inverse, Baseline = 1f, PerUnit = 1f },
            };

            // AS 2.0: сначала 10 + 1 = 11, затем 11 / 2 = 5.5.
            Assert.AreEqual(5.5f, StatConversion.ApplyAll(rules, 10f, new OneStat(StatType.AttackSpeed, 2f)), 1e-3f);
        }
    }
}
