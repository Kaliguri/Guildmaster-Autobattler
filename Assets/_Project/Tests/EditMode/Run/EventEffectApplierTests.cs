using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Применение последствий выбора текстового ивента к RunState (план 11 §5.1): золото, релик, вместимость.
    /// Прогоняется через реальный RunStateService (единый центр вместимости) + фейковый сейв.
    /// </summary>
    public sealed class EventEffectApplierTests
    {
        private RunStateService    _run;
        private EventEffectApplier _applier;

        [SetUp]
        public void SetUp()
        {
            var config = GameConfig.CreateDefault(); // заготовка: вместимость 12, потолок 16
            _run = new RunStateService(new InMemorySaveService(), config, new FixedProfileService(), content: null);
            _run.NewRun(1, Array.Empty<RosterSlot>());
            _run.Current.Gold = 0; // старт-золото забега — не предмет этих тестов (проверяем дельту эффекта)
            // Золото и снятие реликвии едут через шину команд; выдача и вместимость — пока напрямую.
            var commands = new Guildmaster.Guild.Commands.RunCommandBus(
                new Guildmaster.Guild.Commands.RunCommandApplier(_run),
                new Guildmaster.Guild.Commands.RunCommandLog());

            _applier = new EventEffectApplier(_run, commands);
        }

        [Test]
        public void Gold_AddsReward()
        {
            _applier.Apply(new[] { Effect(EventEffectKind.Gold, amount: 50) });
            Assert.AreEqual(50, _run.Current.Gold);
        }

        /// <summary>
        /// Не хватает на вариант — не применяется НИЧЕГО, включая награду из того же списка.
        /// </summary>
        /// <remarks>
        /// Прежний тест закреплял ровно обратное («золото не должно уходить в минус», ждал ноль): цена
        /// уходила односторонней записью, <c>AddGold</c> клампил остаток в ноль, и выбор «купить за 50»
        /// с десятью золотыми списывал десять и всё равно выдавал награду. Это был эксплойт экономики,
        /// а не защита от минуса. Механика изменена по решению Макса 2026-08-07 (вариант не по карману
        /// гасится на экране), тест следует за ней.
        /// </remarks>
        [Test]
        public void Gold_WhenNotEnough_AppliesNothing()
        {
            _run.Current.Gold = 10;
            LogAssert.Expect(LogType.Error, new Regex("вариант стоит 50 золота"));

            bool applied = _applier.Apply(new[]
            {
                Effect(EventEffectKind.Gold, amount: -50),
                Effect(EventEffectKind.GrantRelic, contentId: "relic.paid"),
            });

            Assert.IsFalse(applied, "Applier обязан отказать целиком");
            Assert.AreEqual(10, _run.Current.Gold, "золото не тронуто");
            CollectionAssert.DoesNotContain(_run.Current.RelicInventory, "relic.paid",
                "награда не выдаётся, если за неё не заплачено");
        }

        /// <summary>
        /// Цена варианта — НЕТТО его золота, а не расходная часть: «дам сотню, возьму тридцать» доступно
        /// и с пустым кошельком.
        /// </summary>
        [Test]
        public void ChoiceGoldCost_IsNet_NotSpending()
        {
            EventChoice profitable = Choice(
                Effect(EventEffectKind.Gold, amount: 100),
                Effect(EventEffectKind.Gold, amount: -30));
            EventChoice paid = Choice(Effect(EventEffectKind.Gold, amount: -50));

            Assert.AreEqual(0,  profitable.GoldCost, "выбор, приносящий в сумме, ничего не стоит");
            Assert.AreEqual(50, paid.GoldCost);
        }

        [Test]
        public void GrantRelic_AddsToInventory()
        {
            _applier.Apply(new[] { Effect(EventEffectKind.GrantRelic, contentId: "relic.x") });
            CollectionAssert.Contains(_run.Current.RelicInventory, "relic.x");
        }

        [Test]
        public void RemoveRelic_RemovesFromInventory()
        {
            _run.TryAddRelic("relic.x");
            _applier.Apply(new[] { Effect(EventEffectKind.RemoveRelic, contentId: "relic.x") });
            CollectionAssert.DoesNotContain(_run.Current.RelicInventory, "relic.x");
        }

        [Test]
        public void GainRelicCapacity_RaisesCapacity()
        {
            int before = _run.Current.RelicCapacity;
            _applier.Apply(new[] { Effect(EventEffectKind.GainRelicCapacity, amount: 3) });
            Assert.AreEqual(before + 3, _run.Current.RelicCapacity);
        }

        [Test]
        public void GrantRelic_WhenFull_DoesNotAdd()
        {
            _run.Current.RelicCapacity = 1;
            _run.TryAddRelic("relic.a");
            _applier.Apply(new[] { Effect(EventEffectKind.GrantRelic, contentId: "relic.b") });
            Assert.AreEqual(1, _run.Current.RelicInventory.Length, "полный запас не должен принять релик");
        }

        [Test]
        public void ItemAndCustom_DoNotThrow()
        {
            Assert.DoesNotThrow(() => _applier.Apply(new[]
            {
                Effect(EventEffectKind.GrantItem, contentId: "item.x"),
                Effect(EventEffectKind.Custom, note: "случилось что-то дебажное"),
            }));
        }

        [Test]
        public void MultipleEffects_AppliedInOrder()
        {
            _applier.Apply(new[]
            {
                Effect(EventEffectKind.Gold, amount: 100),
                Effect(EventEffectKind.GrantRelic, contentId: "relic.q"),
                Effect(EventEffectKind.Gold, amount: -30),
            });
            Assert.AreEqual(70, _run.Current.Gold);
            CollectionAssert.Contains(_run.Current.RelicInventory, "relic.q");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EventEffect Effect(EventEffectKind kind, int amount = 0, string contentId = null, string note = null)
        {
            var e = new EventEffect();
            SetField(e, "_kind", kind);
            SetField(e, "_amount", amount);
            SetField(e, "_contentId", contentId);
            SetField(e, "_note", note);
            return e;
        }

        private static void SetField(object target, string field, object value) =>
            typeof(EventEffect).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static EventChoice Choice(params EventEffect[] effects)
        {
            var c = new EventChoice();
            typeof(EventChoice).GetField("_effects", BindingFlags.NonPublic | BindingFlags.Instance)
                               .SetValue(c, effects);
            return c;
        }

    }
}
