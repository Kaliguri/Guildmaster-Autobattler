using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
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

        /// <summary>Общий темп атаки стенда: атак в секунду (решение «медленнее, но импактнее»).</summary>
        private const float ReferenceAttackSpeed = 0.75f;

        /// <summary>
        /// Целевой DPS класса — коридор из ГДД «Статы» §Урон автоатаки. В <c>ClassBalanceConfig</c> его нет
        /// (конфиг знает только HP, скорость и бюджет брони), поэтому таблица живёт здесь и правится вслед
        /// за каноном.
        /// </summary>
        private static float TargetDps(UnitClass unitClass) => unitClass switch
        {
            UnitClass.Bruiser  => 120f,
            UnitClass.Tank     => 60f,
            UnitClass.Assassin => 144f,
            UnitClass.Ranged   => 120f,
            _                  => 60f, // Поддержка и Призыватель: сила не в автоатаке
        };

        /// <summary>Дальность манекена: мили держат фронт вплотную, бэклайн бьёт с восьмёрки (норма доставки).</summary>
        private static float ReferenceRange(UnitClass unitClass)
            => unitClass is UnitClass.Tank or UnitClass.Bruiser or UnitClass.Assassin ? 1f : 8f;

        /// <summary>
        /// Эталонный СОЮЗНИК заданного класса — рядовой представитель своей роли, собранный ровно по норме:
        /// HP, скорость и бюджет брони берутся из живого <see cref="ClassBalanceConfig"/> (то же, что видит
        /// бой), урон — из классового коридора, темп — общий для стенда.
        /// </summary>
        /// <remarks>
        /// Числа НЕ прибиты константами специально: манекен изображает «обычного бойца этого класса», и когда
        /// норма класса осознанно поедет, вся линейка обязана поехать вместе с ней — иначе она перестанет
        /// измерять то, ради чего заведена. Ограничение общее для синтетиков: без <c>UnitData</c> манекен
        /// всегда физический single-target и не умеет ни лечить, ни бить по площади, поэтому «манекен-саппорт»
        /// — это просто слабый стрелок, а не настоящая поддержка.
        /// </remarks>
        public static RuntimeUnit ReferenceAlly(UnitClass unitClass, ClassBalanceConfig classes, int team, Vector2 pos)
        {
            float hp = 2000f, moveSpeed = 3f, armorBudget = 0f;
            if (classes != null)
            {
                (float hpMult, float moveMult) = classes.GetMultipliers(unitClass);
                hp = classes.BaseHp * hpMult;
                moveSpeed = classes.BaseMoveSpeed * moveMult;
                armorBudget = classes.GetArmorBudget(unitClass);
            }

            float halfArmor = armorBudget * 0.5f;
            return Build(team, pos,
                (StatType.MaxHP, hp),
                (StatType.MoveSpeed, moveSpeed),
                (StatType.AutoAttackDamage, TargetDps(unitClass) / ReferenceAttackSpeed),
                (StatType.AttackSpeed, ReferenceAttackSpeed),
                (StatType.AttackRange, ReferenceRange(unitClass)),
                (StatType.PhysArmor, halfArmor),
                (StatType.MagicArmor, halfArmor));
        }

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
