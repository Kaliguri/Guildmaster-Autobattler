using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Каст едет по ленте ВМЕСТЕ со своим определением. Инвариант шва: показ узнаёт, чем исполнен приём
    /// (<see cref="CastSource"/>), только отсюда — сим ему уже недоступен, событие приходит из прошлого,
    /// а по одному id кастера способность не восстановить (у юнита их несколько).
    ///
    /// Без этого телеграф светит всем одинаково: щит-бэш зажигает клинок, марш зажигает барабан.
    /// </summary>
    public sealed class TapeCastPayloadTests
    {
        /// <summary>
        /// Способность с заданным источником. Значение ставится рефлексией: публичного сеттера у
        /// <see cref="AbilityData"/> нет и быть не должно — в игре её авторит инспектор.
        /// </summary>
        static AbilityData Ability(CastSource source)
        {
            var ability = new AbilityData();
            typeof(AbilityData)
                .GetField("_castSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(ability, source);
            return ability;
        }

        [Test]
        public void CastStarted_DeliversTheAbilityWithItsDuration()
        {
            var tape = new BattleTape(windowTicks: 64);
            var dispatcher = new BattleTapeDispatcher(tape);
            AbilityData shieldBash = Ability(CastSource.Shield);

            AbilityData delivered = null;
            float seconds = -1f;
            dispatcher.AbilityCastStarted += (_, s, ability) => { seconds = s; delivered = ability; };

            tape.RecordAbility(10, TapeEventKind.AbilityCastStarted, casterId: 3, def: shieldBash, amount: 1.5f);
            dispatcher.PumpTo(10);

            Assert.That(delivered, Is.SameAs(shieldBash), "определение обязано доехать до показа");
            Assert.That(delivered.CastSource, Is.EqualTo(CastSource.Shield));
            Assert.That(seconds, Is.EqualTo(1.5f).Within(1e-4f), "подводка держится ровно столько, сколько готовится сим");
        }

        [Test]
        public void TwoCastsInOneBattle_KeepTheirOwnAbilities()
        {
            var tape = new BattleTape(windowTicks: 64);
            var dispatcher = new BattleTapeDispatcher(tape);
            AbilityData bash = Ability(CastSource.Shield);
            AbilityData march = Ability(CastSource.WholeBody);

            var got = new System.Collections.Generic.List<CastSource>();
            dispatcher.AbilityCast += (_, ability) => got.Add(ability.CastSource);

            tape.RecordAbility(5, TapeEventKind.AbilityCast, casterId: 1, def: bash);
            tape.RecordAbility(6, TapeEventKind.AbilityCast, casterId: 2, def: march);
            dispatcher.PumpTo(10);

            Assert.That(got, Is.EqualTo(new[] { CastSource.Shield, CastSource.WholeBody }),
                "payload'ы не должны перепутаться: у каждого события свой индекс");
        }

        /// <summary>Dev-рестарт чистит и определения: иначе индексы новой ленты указывают в чужие касты.</summary>
        [Test]
        public void Clear_ForgetsRecordedAbilities()
        {
            var tape = new BattleTape(windowTicks: 8);
            tape.RecordAbility(1, TapeEventKind.AbilityCast, casterId: 1, def: Ability(CastSource.Shield));

            tape.Clear();
            tape.RecordAbility(1, TapeEventKind.AbilityCast, casterId: 1, def: Ability(CastSource.WholeBody));

            Assert.That(tape.GetAbility(0).CastSource, Is.EqualTo(CastSource.WholeBody));
            Assert.That(tape.GetAbility(1), Is.Null, "за пределами списка — null, а не чужая способность");
        }

        [Test]
        public void EventWithoutAnAbility_ReadsAsNull_NotAsSomeoneElses()
        {
            var tape = new BattleTape(windowTicks: 8);
            tape.RecordAbility(1, TapeEventKind.AbilityCast, casterId: 1, def: Ability(CastSource.Shield));

            Assert.That(tape.GetAbility(-1), Is.Null, "у события без payload'а индекс -1 — читаем null");
        }
    }
}
