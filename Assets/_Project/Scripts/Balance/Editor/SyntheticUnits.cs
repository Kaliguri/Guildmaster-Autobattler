using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Синтетические манекены для бенчей — строятся напрямую как <see cref="RuntimeUnit"/>
    /// (<c>new Stats(null)</c> + плоские моды), без SO и фабрики (образец — <c>CombatSimulationTests.MakeMeleeUnit</c>).
    /// Ограничение: без <c>UnitData</c> манекен всегда физический мили single-target (школа/форма атаки
    /// читаются из кита, которого нет). Поэтому манекены годятся как ЦЕЛИ и как эталонный физ-источник
    /// урона; школо-зависимые сценарии требуют авторенного контента (см. simbench.md, отложено).
    /// </summary>
    internal static class SyntheticUnits
    {
        private static RuntimeUnit Build(int team, Vector2 pos, params (StatType stat, float value)[] mods)
        {
            var stats = new Stats(null);
            var arr = new StatModifier[mods.Length];
            for (int i = 0; i < mods.Length; i++)
                arr[i] = new StatModifier(mods[i].stat, ModifierOp.Flat, mods[i].value);
            stats.AddModifiersFrom("synthetic", arr);

            var unit = new RuntimeUnit
            {
                Team = team,
                Stats = stats,
                Position = pos,
                PreviousPosition = pos,
            };
            unit.CurrentHP = stats.Get(StatType.MaxHP);
            return unit;
        }

        /// <summary>
        /// Бессмертная неподвижная цель (огромный HP). НЕ годится для DPS-китов с механикой «% от HP цели»
        /// (взрывает урон против 1e9 HP) — для DPS-бенча используй <see cref="ReferenceDummy"/> с фикс-HP.
        /// Оставлена для санити-тестов плумбинга метрик (синтетик-атакующий = плоский урон, %HP не задет).
        /// </summary>
        public static RuntimeUnit ImmortalDummy(int team, Vector2 pos)
            => Build(team, pos, (StatType.MaxHP, 1e9f));

        /// <summary>
        /// Эталонная цель фикс-HP: не двигается, не бьёт. Убиваемая — DPS-бенч мерит «урон/сек до убийства
        /// цели HP=<paramref name="hp"/>». Фикс-HP (а не 1e9) даёт корректный урон и для %HP-механик.
        /// </summary>
        public static RuntimeUnit ReferenceDummy(int team, Vector2 pos, float hp)
            => Build(team, pos, (StatType.MaxHP, hp));

        /// <summary>
        /// Бессмертный эталонный источник урона: фикс-DPS (физика, мили), высокая скорость хода (держится на цели).
        /// Не умирает — чтобы бенч мерил чисто стойкость жертвы, а не «кто кого перебил».
        /// </summary>
        public static RuntimeUnit ImmortalAttacker(int team, Vector2 pos, float damagePerSecond)
            => Build(team, pos,
                (StatType.MaxHP, 1e9f),
                (StatType.MoveSpeed, 12f),
                (StatType.AutoAttackDamage, damagePerSecond),
                (StatType.AttackSpeed, 1f),
                (StatType.AttackRange, 2f));
    }
}
