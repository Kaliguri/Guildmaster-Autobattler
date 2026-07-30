using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Разгон числа применений нагрузки («Аркановый залп» растёт на стрелу за каст). Проверяется
    /// арифметика контракта, а не бой: формулой пользуется <c>AbilitySystem</c>, и если она начнёт
    /// считать от числа кастов, а не от ПРЕДЫДУЩИХ, первый же залп выйдет на стрелу больше задуманного.
    /// </summary>
    public sealed class PayloadRepeatsTests
    {
        private static AbilityData Ability(int repeats, int growth)
        {
            var data = new AbilityData();
            Set(data, "_payloadRepeats", repeats);
            Set(data, "_payloadRepeatGrowth", growth);
            return data;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType()
                .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(target, value);

        [Test]
        public void FirstCast_UsesBaseRepeatsWithoutGrowth()
        {
            AbilityData volley = Ability(repeats: 3, growth: 1);
            Assert.AreEqual(3, volley.ResolvePayloadRepeats(previousCasts: 0));
        }

        [Test]
        public void EachPreviousCast_AddsOneMore()
        {
            AbilityData volley = Ability(repeats: 3, growth: 1);
            Assert.AreEqual(4, volley.ResolvePayloadRepeats(1));
            Assert.AreEqual(7, volley.ResolvePayloadRepeats(4));
        }

        [Test]
        public void WithoutGrowth_RepeatsStayFlat()
        {
            AbilityData plain = Ability(repeats: 1, growth: 0);
            Assert.AreEqual(1, plain.ResolvePayloadRepeats(0));
            Assert.AreEqual(1, plain.ResolvePayloadRepeats(50));
        }

        [Test]
        public void NegativeCastCount_DoesNotShrinkTheVolley()
        {
            // Защита от арифметики вызывающего: счётчик кастов инкрементируется до применения нагрузки,
            // и «минус один» на первом касте — реальный путь, по которому залп мог бы выйти пустым.
            AbilityData volley = Ability(repeats: 3, growth: 1);
            Assert.AreEqual(3, volley.ResolvePayloadRepeats(-1));
        }

        [Test]
        public void ZeroOrNegativeAuthoredRepeats_StillFireOnce()
        {
            AbilityData broken = Ability(repeats: 0, growth: 0);
            Assert.AreEqual(1, broken.ResolvePayloadRepeats(0),
                "Способность без применений не наносила бы вообще ничего — пол в единицу держит контракт.");
        }
    }
}
