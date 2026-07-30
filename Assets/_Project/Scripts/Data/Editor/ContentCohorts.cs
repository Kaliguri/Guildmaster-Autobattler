using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Готовые выборки контента для массовых правок баланса: «все Танки», «все ближники», «все гоблины».
    /// Read-сторона к <see cref="ContentEditService"/>, который правит уже отобранное.
    /// </summary>
    /// <remarks>
    /// Заведено потому, что балансная правка почти никогда не касается одного кита: подтягивают
    /// когорту целиком, иначе внутри роли расползается разнобой. Писать фильтр заново каждый раз —
    /// значит каждый раз заново решать, кто считается ближником, и получать разный ответ.
    /// <para>Порядок стабилен (наследуется от <c>LoadAll</c>, сортировка по пути) — прогоны должны
    /// быть воспроизводимы.</para>
    /// </remarks>
    public static class ContentCohorts
    {
        /// <summary>Произвольная когорта: все ассеты типа <typeparamref name="T"/>, прошедшие фильтр.</summary>
        public static List<T> Where<T>(Func<T, bool> predicate) where T : ScriptableObject
        {
            var all = ContentEditService.LoadAll<T>();
            if (predicate == null) return all;

            var picked = new List<T>(all.Count);
            for (int i = 0; i < all.Count; i++)
                if (predicate(all[i])) picked.Add(all[i]);
            return picked;
        }

        /// <summary>Реликвии игрока.</summary>
        public static List<RelicData> Relics() => ContentEditService.LoadAll<RelicData>();

        /// <summary>Враги.</summary>
        public static List<EnemyData> Enemies() => ContentEditService.LoadAll<EnemyData>();

        /// <summary>
        /// ВСЕ боевые юниты — реликвии и враги вместе. Класс, оружие и статы у них общие
        /// (<see cref="UnitData"/>), поэтому классовая правка обязана задевать обе стороны:
        /// подтянуть Танков только у игрока значит сломать бой, а не починить роль.
        /// </summary>
        public static List<UnitData> AllUnits()
        {
            var units = new List<UnitData>();
            units.AddRange(Relics());
            units.AddRange(Enemies());
            return units;
        }

        /// <summary>Юниты боевого класса — то, по чему считаются классовые коридоры.</summary>
        public static List<UnitData> OfClass(UnitClass unitClass)
            => Filter(AllUnits(), u => u.CombatClass == unitClass);

        /// <summary>Юниты по дальности боя: ближний бой против дальнего.</summary>
        public static List<UnitData> OfAttackType(AttackType attackType)
            => Filter(AllUnits(), u => u.AttackType == attackType);

        /// <summary>
        /// Юниты по школе урона АВТОАТАКИ (физика или магия) — школа выводится из типа, своего поля у
        /// юнита больше нет.
        /// </summary>
        public static List<UnitData> OfSchool(DamageSchool school)
            => Filter(AllUnits(), u => DamageTypes.SchoolOf(u.AutoAttackDamageType) == school);

        /// <summary>Юниты по конкретному типу урона автоатаки — «все дробящие», «все ледяные».</summary>
        public static List<UnitData> OfDamageType(DamageType damageType)
            => Filter(AllUnits(), u => u.AutoAttackDamageType == damageType);

        /// <summary>Юниты по типу существа — «все гоблины», «вся нежить».</summary>
        public static List<UnitData> OfCreatureType(CreatureType creatureType)
            => Filter(AllUnits(), u => u.CreatureType == creatureType);

        /// <summary>
        /// Юниты, чей id начинается с префикса — когорта по семье контента (<c>enemy.goblin</c>).
        /// Работает по id, а не по имени ассета: имя переименовывается, id — нет.
        /// </summary>
        public static List<UnitData> WithIdPrefix(string prefix)
            => Filter(AllUnits(), u => !string.IsNullOrEmpty(u.Id) && u.Id.StartsWith(prefix, StringComparison.Ordinal));

        private static List<UnitData> Filter(List<UnitData> source, Func<UnitData, bool> predicate)
        {
            var picked = new List<UnitData>(source.Count);
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && predicate(source[i])) picked.Add(source[i]);
            return picked;
        }
    }
}
