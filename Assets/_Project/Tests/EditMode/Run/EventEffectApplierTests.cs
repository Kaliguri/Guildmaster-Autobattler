using System;
using System.Reflection;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

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
            var config = ScriptableObject.CreateInstance<GameConfig>(); // Base=8, Max=16
            _run = new RunStateService(new InMemorySaveService(), config);
            _run.NewRun(1, Array.Empty<RosterSlot>());
            _run.Current.Gold = 0; // старт-золото забега — не предмет этих тестов (проверяем дельту эффекта)
            _applier = new EventEffectApplier(_run);
        }

        [Test]
        public void Gold_AddsAndClampsAtZero()
        {
            _applier.Apply(new[] { Effect(EventEffectKind.Gold, amount: 50) });
            Assert.AreEqual(50, _run.Current.Gold);

            _applier.Apply(new[] { Effect(EventEffectKind.Gold, amount: -100) });
            Assert.AreEqual(0, _run.Current.Gold, "золото не должно уходить в минус");
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

    }
}
