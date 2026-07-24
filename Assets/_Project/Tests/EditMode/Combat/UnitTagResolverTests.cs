using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Сборка тегов «быстрого чтения» карточки (<see cref="UnitTagResolver"/>): авто Role из класса,
    /// авто DamageType из статических источников (автоатака + наносящие урон способности), ручные
    /// Playstyle/Mechanic, и порядок осей Role→DamageType→Playstyle→Mechanic.
    /// </summary>
    public sealed class UnitTagResolverTests
    {
        [Test]
        public void Resolve_OrdersByAxis_RoleDamageTypeManual()
        {
            var db = new FakeDb(
                Tag("tag.assassin", TagCategory.Role),
                Tag("tag.physical", TagCategory.DamageType),
                Tag("tag.pierce", TagCategory.DamageType),
                Tag("tag.slash", TagCategory.DamageType),
                Tag("tag.escape", TagCategory.Playstyle),
                Tag("tag.stealth", TagCategory.Mechanic));

            // Ассасин: автоатака Physical/Pierce; ульта наносит урон с override Slash; ручные — escape+stealth.
            var ability = new AbilityData()
                .With("_damageMultiplier", 2f)
                .With("_physicalSubtypeOverride", PhysicalSubtypeOverride.Slash);
            var relic = ScriptableObject.CreateInstance<RelicData>()
                .With("_combatClass", UnitClass.Assassin)
                .With("_damageSchool", DamageSchool.Physical)
                .With("_physicalSubtype", PhysicalSubtype.Pierce)
                .With("_abilities", new[] { ability })
                .With("_infoTags", new[] { db.Asset("tag.stealth"), db.Asset("tag.escape") });

            List<TagData> tags = UnitTagResolver.Resolve(relic, db);
            var ids = tags.ConvertAll(t => t.Id);

            // Role, затем DamageType (зонтик → конкретика), затем Playstyle, затем Mechanic.
            Assert.AreEqual(new[] { "tag.assassin", "tag.physical", "tag.pierce", "tag.slash", "tag.escape", "tag.stealth" }, ids);
            Object.DestroyImmediate(relic);
        }

        [Test]
        public void Resolve_MagicalUnit_UmbrellaPlusElement()
        {
            var db = new FakeDb(
                Tag("tag.ranged", TagCategory.Role),
                Tag("tag.magical", TagCategory.DamageType),
                Tag("tag.ice", TagCategory.DamageType));

            // Криомант: РДД, автоатака Magical/Ice.
            var relic = ScriptableObject.CreateInstance<RelicData>()
                .With("_combatClass", UnitClass.Ranged)
                .With("_damageSchool", DamageSchool.Magical)
                .With("_magicElement", MagicElement.Ice);

            var ids = UnitTagResolver.Resolve(relic, db).ConvertAll(t => t.Id);
            Assert.AreEqual(new[] { "tag.ranged", "tag.magical", "tag.ice" }, ids);
            Object.DestroyImmediate(relic);
        }

        [Test]
        public void Resolve_NonDamagingAbility_NotCollected()
        {
            var db = new FakeDb(
                Tag("tag.support", TagCategory.Role),
                Tag("tag.physical", TagCategory.DamageType),
                Tag("tag.slash", TagCategory.DamageType));

            // Способность без прямого урона (DamageMultiplier 0) не добавляет DamageType-тегов.
            var buff = new AbilityData().With("_damageMultiplier", 0f).With("_schoolOverride", DamageSchoolOverride.Magical);
            var relic = ScriptableObject.CreateInstance<RelicData>()
                .With("_combatClass", UnitClass.Support)
                .With("_damageSchool", DamageSchool.Physical)
                .With("_physicalSubtype", PhysicalSubtype.Slash)
                .With("_abilities", new[] { buff });

            var ids = UnitTagResolver.Resolve(relic, db).ConvertAll(t => t.Id);
            Assert.AreEqual(new[] { "tag.support", "tag.physical", "tag.slash" }, ids, "маг-школа баффа не попадает в теги");
            Object.DestroyImmediate(relic);
        }

        [Test]
        public void Resolve_MissingTagAsset_SilentlySkipped()
        {
            var db = new FakeDb(Tag("tag.physical", TagCategory.DamageType)); // нет tag.bruiser
            var relic = ScriptableObject.CreateInstance<RelicData>()
                .With("_combatClass", UnitClass.Bruiser)
                .With("_damageSchool", DamageSchool.Physical);

            var ids = UnitTagResolver.Resolve(relic, db).ConvertAll(t => t.Id);
            Assert.AreEqual(new[] { "tag.physical" }, ids, "отсутствующий ассет тега пропущен, UI не падает");
            Object.DestroyImmediate(relic);
        }

        private static TagData Tag(string id, TagCategory cat) =>
            ScriptableObject.CreateInstance<TagData>().With("_id", id).With("_category", cat);

        /// <summary>Минимальный <see cref="IContentDatabase"/>: только TryGet по словарю тегов.</summary>
        private sealed class FakeDb : IContentDatabase
        {
            private readonly Dictionary<string, TagData> _tags = new Dictionary<string, TagData>();

            public FakeDb(params TagData[] tags)
            {
                foreach (TagData t in tags) _tags[t.Id] = t;
            }

            public TagData Asset(string id) => _tags[id];

            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                if (_tags.TryGetValue(id, out TagData t) && t is T typed) { def = typed; return true; }
                def = null;
                return false;
            }

            public T Get<T>(string id) where T : ContentDefinition =>
                TryGet(id, out T def) ? def : null;

            public IReadOnlyList<T> All<T>() where T : ContentDefinition => new List<T>();
        }
    }
}
