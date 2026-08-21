using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Снятие ран за золото: цена берётся из ассета, а списание и снятие происходят одним шагом.
    /// <para>Инвариант, ради которого тест и написан: не должно существовать состояния «золото списано,
    /// рана на месте». Разнеси оплату и снятие на два вызова — и в него попадёт любой отказ между ними,
    /// а увидит это игрок, а не компилятор.</para>
    /// </summary>
    public sealed class InjuryHealingTests
    {
        private const string BruiseId = "consequence.sprained_leg";
        private const int    HealCost = 40;

        private RunStateService _runStates;
        private RunCommandBus   _commands;
        private ShopController  _shop;

        [SetUp]
        public void SetUp()
        {
            GameConfig config = GameConfig.CreateDefault();
            var content = new FakeContent();
            content.Add(Consequence(BruiseId, InjuryGrade.Bruise, HealCost));

            _runStates = new RunStateService(new InMemorySaveService(), config,
                                             new FixedProfileService(), content);
            _runStates.NewRun(1L, new[] { new RosterSlot(), new RosterSlot() });
            _runStates.Current.Gold = 100;
            _runStates.Current.Guild[0].Injuries = new[] { new Injury(BruiseId) };

            _commands = new RunCommandBus(new RunCommandApplier(_runStates), new RunCommandLog());

            var rng = new XorShiftRng(5UL);
            _shop = new ShopController(new RewardService(content, rng), new RelicPricer(config),
                                       _runStates, _commands, content, rng, config);
        }

        [Test]
        public void Shop_ShowsEveryInjuryOfTheParty_WithItsPrice()
        {
            _runStates.Current.Guild[1].Injuries = new[] { new Injury(BruiseId) };

            IReadOnlyList<ShopInjury> onSale = _shop.Injuries;

            Assert.That(onSale.Count, Is.EqualTo(2));
            Assert.That(onSale.Select(i => i.SlotIndex), Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(onSale[0].Price, Is.EqualTo(HealCost));
        }

        [Test]
        public void Healing_TakesTheGoldAndTheInjuryTogether()
        {
            Assert.That(_shop.Heal(slotIndex: 0, BruiseId), Is.True);

            Assert.That(_runStates.Gold, Is.EqualTo(100 - HealCost));
            Assert.That(_runStates.Current.Guild[0].Injuries, Is.Empty);
        }

        [Test]
        public void NotEnoughGold_LeavesBothTheGoldAndTheInjuryAlone()
        {
            _runStates.Current.Gold = HealCost - 1;

            Assert.That(_shop.Heal(slotIndex: 0, BruiseId), Is.False);

            Assert.That(_runStates.Gold, Is.EqualTo(HealCost - 1), "Отказ ничего не списывает.");
            Assert.That(_runStates.Current.Guild[0].Injuries.Length, Is.EqualTo(1), "И ничего не лечит.");
        }

        /// <summary>
        /// Лечить то, чего на «Сосуде» нет, нельзя — иначе рассинхрон в коопе («у меня она ещё есть»)
        /// оборачивался бы бесплатной тратой золота.
        /// </summary>
        [Test]
        public void HealingAnInjuryTheVesselDoesNotHave_CostsNothing()
        {
            Assert.That(_shop.Heal(slotIndex: 1, BruiseId), Is.True, "Команда отправлена — отказ на применении.");

            Assert.That(_runStates.Gold, Is.EqualTo(100), "Золото не тронуто: применитель отказал.");
            Assert.That(_runStates.Current.Guild[0].Injuries.Length, Is.EqualTo(1), "Чужая рана не задета.");
        }

        /// <summary>
        /// На привале рана уходит бесплатно: за неё уже заплачено действием отряда, а не золотом.
        /// </summary>
        [Test]
        public void CampHealing_TakesNoGold()
        {
            _commands.HealInjury(slotIndex: 0, BruiseId, payGold: false);

            Assert.That(_runStates.Current.Guild[0].Injuries, Is.Empty);
            Assert.That(_runStates.Gold, Is.EqualTo(100));
        }

        /// <summary>
        /// Лечение идёт через лог команд, как и всё, что меняет забег: без этого напарник увидел бы
        /// опустевший слот и убывшее золото без причины в журнале.
        /// </summary>
        [Test]
        public void HealingIsRecordedInTheCommandLog()
        {
            _shop.Heal(slotIndex: 0, BruiseId);

            Assert.That(_commands.Log.Entries.Any(c => c.Kind == RunCommandKind.HealInjury), Is.True);
        }

        private static ConsequenceData Consequence(string id, InjuryGrade grade, int healCostGold)
        {
            var c = ScriptableObject.CreateInstance<ConsequenceData>();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ConsequenceData).GetField("_grade", F).SetValue(c, grade);
            typeof(ConsequenceData).GetField("_healCostGold", F).SetValue(c, healCostGold);
            typeof(ContentDefinition).GetField("_id", F).SetValue(c, id);
            return c;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly List<ContentDefinition> _all = new();

            public void Add(ContentDefinition d) => _all.Add(d);

            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                foreach (ContentDefinition d in _all)
                    if (d.Id == id && d is T t) { def = t; return true; }
                def = null;
                return false;
            }

            public IReadOnlyList<T> All<T>() where T : ContentDefinition => _all.OfType<T>().ToArray();
        }
    }
}
