using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Классовый профиль баланса — 2-й уровень стат-каскада (ГДД «Combat - Stats» §Классы, решение
    /// 2026-07-24). Задаёт базовый HP и скорость передвижения от <see cref="UnitClass"/> как
    /// множители от эталона-Брузера. Единственный экземпляр на проект.
    /// </summary>
    /// <remarks>
    /// Каскад стат-базы: <c>StatsConfig</c> (глобальный фолбэк) → <b>ClassBalanceConfig</b> (класс)
    /// → персона (реликвия/юнит) → Vessel. Технически классовая база отдаётся как группа
    /// <see cref="ModifierOp.Override"/>-модификаторов, добавляемая в <c>Stats</c> ПЕРВОЙ (до
    /// персоны): «последний Override побеждает» даёт каскад бесплатно, а Flat/Percent-дельты
    /// персоны и Vessel копятся поверх. Формулу сборки (<c>Stats.cs</c>) это не меняет.
    /// <para>Класс задаёт ТОЛЬКО HP и скорость. Прочие статы (урон, дальность, броня, размер) — от
    /// персоны/оружия; профиль их не трогает (охват минимальный, расширяем по реальной боли).</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Combat/Class Balance Config", fileName = "ClassBalanceConfig")]
    public sealed class ClassBalanceConfig : ScriptableObject
    {
        [Header("Эталон (Брузер = 100%)")]
        [Tooltip("Базовое HP золотой середины (Брузера). Классы — множители от него: Танк ×1.5 = 3000, Убийца ×0.75 = 1500, бэклайн ×0.65 = 1300.")]
        [SerializeField] private float _baseHp = 2000f;

        [Tooltip("Базовая скорость передвижения Брузера. Классы — множители от неё.")]
        [SerializeField] private float _baseMoveSpeed = 3f;

        [Header("Профили классов (множители от эталона)")]
        [Tooltip("Множители HP/скорости на каждый класс. Класс без записи → эталон (1.0 / 1.0). Три бэклайновых класса пока делят один профиль 0.65/0.75.")]
        [SerializeField] private ClassProfile[] _profiles = Array.Empty<ClassProfile>();

        public float BaseHp => _baseHp;
        public float BaseMoveSpeed => _baseMoveSpeed;

        /// <summary>
        /// Классовая стат-база как группа <see cref="ModifierOp.Override"/>-модификаторов для
        /// скармливания в <c>Stats</c> ПЕРВОЙ группой. Абсолютные значения: <c>base × mult</c>.
        /// </summary>
        public StatModifier[] GetBaseModifiers(UnitClass unitClass)
        {
            (float hpMult, float moveMult) = GetMultipliers(unitClass);
            return new[]
            {
                new StatModifier(StatType.MaxHP,     ModifierOp.Override, _baseHp * hpMult),
                new StatModifier(StatType.MoveSpeed, ModifierOp.Override, _baseMoveSpeed * moveMult),
            };
        }

        /// <summary>Множители (HP, скорость) класса: из таблицы, иначе эталон (1.0 / 1.0).</summary>
        public (float hp, float move) GetMultipliers(UnitClass unitClass)
        {
            for (int i = 0; i < _profiles.Length; i++)
            {
                if (_profiles[i].Class == unitClass)
                {
                    return (_profiles[i].HpMult, _profiles[i].MoveSpeedMult);
                }
            }

            return (1f, 1f);
        }

        [Serializable]
        public struct ClassProfile
        {
            public UnitClass Class;
            public float HpMult;
            public float MoveSpeedMult;

            public ClassProfile(UnitClass unitClass, float hpMult, float moveSpeedMult)
            {
                Class = unitClass;
                HpMult = hpMult;
                MoveSpeedMult = moveSpeedMult;
            }
        }
    }
}
