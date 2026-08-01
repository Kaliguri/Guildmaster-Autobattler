using System.Collections.Generic;
using Guildmaster.Data;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Отпечаток контента для handshake сессии (ТЗ кооп-вертикали §4.3): им двое сверяются ДО начала
    /// игры, потому что по сети едут строковые id, и неизвестный id на приёме роняет показ, а не
    /// «слегка расходит картинку».
    /// <para>Здесь проверяются три свойства, без которых отпечаток бесполезен: он не зависит от порядка
    /// записей, он замечает любое изменение набора, и он одинаков между запусками процесса.</para>
    /// </summary>
    public sealed class ContentFingerprintTests
    {
        [Test]
        public void SameContent_InAnyOrder_GivesTheSameHash()
        {
            var straight = Defs("relic.a", "relic.b", "unit.c");
            var shuffled = Defs("unit.c", "relic.a", "relic.b");

            ContentFingerprint first  = ContentFingerprint.Compute(straight, schemaVersion: 1, gameVersion: "0.1");
            ContentFingerprint second = ContentFingerprint.Compute(shuffled, schemaVersion: 1, gameVersion: "0.1");

            Assert.AreEqual(first.ContentHash, second.ContentHash,
                "Отпечаток зависит от НАБОРА контента, а не от порядка в списке: иначе пересборка базы у " +
                "одного из игроков разводила бы сессию на ровном месте");
            Assert.IsTrue(first.Matches(second));
        }

        [Test]
        public void AnyChangeToTheSet_ChangesTheHash()
        {
            ContentFingerprint baseline = ContentFingerprint.Compute(Defs("relic.a", "relic.b"), 1, "0.1");

            Assert.AreNotEqual(baseline.ContentHash,
                ContentFingerprint.Compute(Defs("relic.a", "relic.b", "relic.c"), 1, "0.1").ContentHash,
                "Добавили определение — отпечаток другой");

            Assert.AreNotEqual(baseline.ContentHash,
                ContentFingerprint.Compute(Defs("relic.a"), 1, "0.1").ContentHash,
                "Убрали определение — отпечаток другой");

            Assert.AreNotEqual(baseline.ContentHash,
                ContentFingerprint.Compute(Defs("relic.a", "relic.B"), 1, "0.1").ContentHash,
                "Переименовали (даже регистром) — отпечаток другой: id сравниваются как есть");
        }

        // Склейка без разделителя дала бы «a»+«bc» == «ab»+«c» — два разных набора с одним отпечатком.
        [Test]
        public void IdBoundaries_AreNotSmearedTogether()
        {
            ContentFingerprint left  = ContentFingerprint.Compute(Defs("a", "bc"), 1, "0.1");
            ContentFingerprint right = ContentFingerprint.Compute(Defs("ab", "c"), 1, "0.1");

            Assert.AreNotEqual(left.ContentHash, right.ContentHash,
                "Границы id обязаны быть видны хешу");
        }

        [Test]
        public void VersionAndSchema_AreCheckedSeparately_BecauseTheyBreakDifferently()
        {
            var defs = Defs("relic.a");
            ContentFingerprint mine   = ContentFingerprint.Compute(defs, schemaVersion: 1, gameVersion: "0.1");
            ContentFingerprint newer  = ContentFingerprint.Compute(defs, schemaVersion: 1, gameVersion: "0.2");
            ContentFingerprint schema = ContentFingerprint.Compute(defs, schemaVersion: 2, gameVersion: "0.1");

            Assert.IsFalse(mine.Matches(newer), "Разные сборки играть вместе не могут");
            Assert.IsFalse(mine.Matches(schema), "Разные версии схемы — тоже");

            StringAssert.Contains("верси", mine.DescribeMismatch(newer),
                "Отказ объясняется словами: игроку нужно понять, что делать");
            Assert.IsEmpty(mine.DescribeMismatch(
                ContentFingerprint.Compute(defs, 1, "0.1")), "Сошлись — объяснять нечего");
        }

        [Test]
        public void BrokenEntries_DoNotSilentlyCountAsContent()
        {
            var withHoles = new List<ContentDefinition> { Def("relic.a"), null, Def(string.Empty), Def("relic.b") };

            ContentFingerprint dirty = ContentFingerprint.Compute(withHoles, 1, "0.1");
            ContentFingerprint clean = ContentFingerprint.Compute(Defs("relic.a", "relic.b"), 1, "0.1");

            Assert.AreEqual(clean.ContentHash, dirty.ContentHash,
                "Пустая и битая запись в базе — не контент: иначе отпечаток зависел бы от мусора");
            Assert.AreEqual(2, dirty.ContentCount);
        }

        // ── Утилиты ──

        private static List<ContentDefinition> Defs(params string[] ids)
        {
            var list = new List<ContentDefinition>(ids.Length);
            for (int i = 0; i < ids.Length; i++) list.Add(Def(ids[i]));
            return list;
        }

        // RelicData как представитель ContentDefinition: отпечатку важен только id, а тип — нет.
        private static ContentDefinition Def(string id)
        {
            var def = ScriptableObject.CreateInstance<RelicData>();
            def.name = id;
            typeof(ContentDefinition)
                .GetField("_id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(def, id);
            return def;
        }
    }
}
