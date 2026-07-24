using System.Collections.Generic;
using Guildmaster.Data.Stats;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Собирает теги «быстрого чтения» юнита для карточки в порядке осей
    /// <c>Role → DamageType → Playstyle → Mechanic</c> (ГДД <c>unit-tag-glossary</c>).
    /// <para><b>Авто-оси</b> (Role, DamageType) выводятся из данных — не дублируются руками:
    /// Role из <see cref="UnitClass"/>, DamageType из <see cref="DamageType"/> всех статических
    /// источников урона юнита (автоатака + наносящие урон способности). Стихии/сродства, живущие
    /// в эффектах (Burn→Fire, споры→Poison), сюда пока НЕ попадают — они в слое Combat, недоступном
    /// UI; это осознанный задел (см. журнал рефактора модели урона).</para>
    /// <para><b>Ручные оси</b> (Playstyle, Mechanic) берутся из <see cref="UnitData.InfoTags"/>.</para>
    /// </summary>
    public static class UnitTagResolver
    {
        /// <summary>
        /// Упорядоченный список тегов юнита. Авто-теги резолвятся из <paramref name="db"/> по id
        /// <c>tag.&lt;snake&gt;</c>; отсутствующий ассет тега молча пропускается (не роняем UI из-за тега).
        /// Ручные <see cref="UnitData.InfoTags"/> добавляются как есть и сортируются в свою ось.
        /// </summary>
        public static List<TagData> Resolve(UnitData unit, IContentDatabase db)
        {
            var result = new List<TagData>();
            if (unit == null || db == null) return result;

            var seen = new HashSet<string>();

            void AddById(string id)
            {
                if (id == null || !seen.Add(id)) return;
                if (db.TryGet(id, out TagData tag) && tag != null) result.Add(tag);
            }

            // --- Ось 1: Role (авто из класса) ---
            AddById(RoleTagId(unit.CombatClass));

            // --- Ось 2: DamageType (авто из статических источников урона) ---
            // Собираем раздельно, чтобы вывести зонтики → конкретику → сродства.
            var umbrellas = new List<string>();
            var specifics = new List<string>();
            var affinities = new List<string>();
            var dtSeen = new HashSet<string>();

            void AddDamageType(in DamageType dt)
            {
                AddUnique(umbrellas, dtSeen, UmbrellaTagId(dt.School));
                AddUnique(specifics, dtSeen, SpecificTagId(dt));
                AddUnique(affinities, dtSeen, AffinityTagId(dt.Affinity));
            }

            AddDamageType(unit.ResolveAutoAttackDamageType());
            AbilityData[] abilities = unit.Abilities;
            if (abilities != null)
                for (int i = 0; i < abilities.Length; i++)
                {
                    AbilityData a = abilities[i];
                    if (a == null || a.DamageMultiplier <= 0f) continue; // только наносящие прямой урон
                    AddDamageType(a.ResolveDamageType(unit));
                }

            for (int i = 0; i < umbrellas.Count; i++) AddById(umbrellas[i]);
            for (int i = 0; i < specifics.Count; i++) AddById(specifics[i]);
            for (int i = 0; i < affinities.Count; i++) AddById(affinities[i]);

            // --- Оси 3–4: Playstyle / Mechanic (ручные) ---
            TagData[] manual = unit.InfoTags;
            if (manual != null)
                for (int i = 0; i < manual.Length; i++)
                {
                    TagData t = manual[i];
                    if (t != null && seen.Add(t.Id)) result.Add(t);
                }

            // Стабильная сортировка по оси: Role(0) → DamageType(1) → Playstyle(2) → Mechanic(3) → Other(4).
            // Внутри оси порядок вставки сохраняется (умбрелла раньше конкретики и т.д.).
            StableSortByCategory(result);
            return result;
        }

        private static void AddUnique(List<string> list, HashSet<string> seen, string id)
        {
            if (id != null && seen.Add(id)) list.Add(id);
        }

        private static void StableSortByCategory(List<TagData> tags)
        {
            // Простая устойчивая сортировка вставками (список тегов короткий — единицы элементов).
            for (int i = 1; i < tags.Count; i++)
            {
                TagData key = tags[i];
                int keyCat = (int)key.Category;
                int j = i - 1;
                while (j >= 0 && (int)tags[j].Category > keyCat)
                {
                    tags[j + 1] = tags[j];
                    j--;
                }
                tags[j + 1] = key;
            }
        }

        private static string RoleTagId(UnitClass unitClass) => unitClass switch
        {
            UnitClass.Tank     => "tag.tank",
            UnitClass.Bruiser  => "tag.bruiser",
            UnitClass.Assassin => "tag.assassin",
            UnitClass.Ranged   => "tag.ranged",
            UnitClass.Support  => "tag.support",
            UnitClass.Summoner => "tag.summoner",
            _                  => null,
        };

        private static string UmbrellaTagId(DamageSchool school) => school switch
        {
            DamageSchool.Physical => "tag.physical",
            DamageSchool.Magical  => "tag.magical",
            DamageSchool.True     => "tag.pure",
            _                     => null,
        };

        private static string SpecificTagId(in DamageType dt)
        {
            if (dt.School == DamageSchool.Physical)
                return dt.PhysicalSubtype switch
                {
                    PhysicalSubtype.Blunt  => "tag.blunt",
                    PhysicalSubtype.Slash  => "tag.slash",
                    PhysicalSubtype.Pierce => "tag.pierce",
                    _                      => null,
                };
            if (dt.School == DamageSchool.Magical)
                return dt.MagicElement switch
                {
                    MagicElement.Fire      => "tag.fire",
                    MagicElement.Ice       => "tag.ice",
                    MagicElement.Lightning => "tag.lightning",
                    MagicElement.Arcane    => "tag.arcane",
                    _                      => null,
                };
            return null;
        }

        private static string AffinityTagId(DamageAffinity affinity) => affinity switch
        {
            DamageAffinity.Poison => "tag.poison",
            DamageAffinity.Light  => "tag.light",
            DamageAffinity.Dark   => "tag.dark",
            _                     => null,
        };
    }
}
