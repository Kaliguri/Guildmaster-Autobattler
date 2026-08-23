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
    /// → персона (мементо/юнит) → Vessel. Технически классовая база отдаётся как группа
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

        [Tooltip("Ожидаемый одиночный DPS Брузера — норма для стенда баланса. Классы — множители от неё.")]
        [SerializeField] private float _baseDps = 110f;

        [Tooltip("Эталон ЛЕЧЕНИЯ: сколько HP в секунду возвращает команде класс с HealMult = 1 (Целитель). " +
                 "Равен BaseDps по решению Макса 2026-08-01: одно вылеченное HP стоит одного нанесённого.")]
        [SerializeField] private float _baseHps = 110f;

        [Header("Профили классов (множители от эталона)")]
        [Tooltip("Множители HP/скорости и бюджет брони на каждый класс. Класс без записи → эталон (1.0 / 1.0), броня 0.")]
        [SerializeField] private ClassProfile[] _profiles = Array.Empty<ClassProfile>();

        [Header("Коридор нормы")]
        [Tooltip("Полуширина коридора вокруг нормы, доля: 0.3 = ±30%. Выход за него стенд помечает как отклонение роли.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _bandWidth = 0.3f;

        public float BaseHp => _baseHp;
        public float BaseMoveSpeed => _baseMoveSpeed;
        public float BaseDps => _baseDps;

        /// <summary>Эталон лечения, HP/сек: норма класса с <c>HealMult = 1</c>.</summary>
        public float BaseHps => _baseHps;

        /// <summary>Полуширина коридора нормы (0.3 = ±30%). Не игровой стат — линейка стенда баланса.</summary>
        public float BandWidth => _bandWidth;

        /// <summary>
        /// Бюджет брони класса — СУММА физической и магической (ГДД «Статы» §Броня, решение
        /// 2026-07-26/12). Класс раскладывает его поровну; конкретный герой перекрывает обе брони
        /// своим стат-блоком, сохраняя сумму (латник 100/20, монах 25/35 и т.п.).
        /// </summary>
        /// <remarks>
        /// Раскладка не меняет средний запас прочности: эффективное HP линейно по броне
        /// (<c>HP × (1 + броня/K)</c>), поэтому переложенная из школы в школу единица ровно столько же
        /// отнимает у одной стороны и добавляет другой. Специализация даёт разброс, не силу.
        /// </remarks>
        public float GetArmorBudget(UnitClass unitClass)
        {
            for (int i = 0; i < _profiles.Length; i++)
                if (_profiles[i].Class == unitClass) return _profiles[i].ArmorBudget;
            return 0f;
        }

        /// <summary>
        /// Классовая стат-база как группа <see cref="ModifierOp.Override"/>-модификаторов для
        /// скармливания в <c>Stats</c> ПЕРВОЙ группой. Абсолютные значения: <c>base × mult</c>.
        /// </summary>
        public StatModifier[] GetBaseModifiers(UnitClass unitClass)
        {
            (float hpMult, float moveMult) = GetMultipliers(unitClass);
            float halfArmor = GetArmorBudget(unitClass) * 0.5f;
            return new[]
            {
                new StatModifier(StatType.MaxHP,      ModifierOp.Override, _baseHp * hpMult),
                new StatModifier(StatType.MoveSpeed,  ModifierOp.Override, _baseMoveSpeed * moveMult),
                new StatModifier(StatType.PhysArmor,  ModifierOp.Override, halfArmor),
                new StatModifier(StatType.MagicArmor, ModifierOp.Override, halfArmor),
            };
        }

        /// <summary>
        /// Ожидаемый одиночный DPS класса — норма стенда баланса: <c>BaseDps × DpsMult</c>.
        /// </summary>
        /// <remarks>
        /// В отличие от HP и скорости, урон класс НЕ задаёт: он собирается из оружия, способностей и
        /// стат-блока персоны. Норма здесь — не источник числа, а линейка, по которой стенд говорит
        /// «Танк бьёт как Убийца». Поэтому она живёт рядом с прочими классовыми множителями, но ни во
        /// что не подставляется — её читают только бенчи.
        /// </remarks>
        public float GetDpsNorm(UnitClass unitClass)
        {
            for (int i = 0; i < _profiles.Length; i++)
                if (_profiles[i].Class == unitClass) return _baseDps * _profiles[i].DpsMult;
            return _baseDps;
        }

        /// <summary>
        /// Ожидаемое ЛЕЧЕНИЕ класса, HP в секунду: <c>BaseHps × HealMult</c>. Класс без лечения в роли — 0.
        /// </summary>
        /// <remarks>
        /// Вторая валюта нормы, заведена 2026-08-01. До неё линейка мерила только урон и голое HP, поэтому
        /// у Целителя и Поддержки основная работа не была нормирована вовсе — сравнивать их замер было не с
        /// чем, и «хил ощущается пустым» не превращалось в число. Единица счёта общая с уроном: одно
        /// вылеченное HP = одно нанесённое, поэтому норма Целителя равна DPS-эталону Брузера.
        /// <para>Как и <see cref="GetDpsNorm"/>, ни во что не подставляется: лечение собирается из
        /// способностей кита, а норма — линейка, по которой стенд говорит «Целитель лечит вполсилы».</para>
        /// <para><b>Читать вместе с КПД:</b> паспортная норма — потолок при полной загрузке, а лечить есть
        /// кого не всегда. Замеренный HPS ниже нормы сам по себе не приговор — вопрос, почему: нечего
        /// лечить (тогда это про бой) или нечем (тогда это про кит).</para>
        /// <para><b>Норма ненулевая только у Целителя</b> (вердикт Макса 2026-08-01): держать своих
        /// лечением ИЛИ щитами — его основная механика, а Поддержка живёт баффами и дебаффами. Поэтому
        /// Хранитель углей (чистый антидебаффер) не лечит вовсе и это не отставание, а Друид лечит
        /// точечно в затяжной свалке ближников (600 за бой длиной 240 секунд — 2.5 HPS) и потоком HP
        /// не мерится.</para>
        /// <para><b>Долг измерителя:</b> норма считает лечение и щит одной величиной, а стенд снимает
        /// только <c>HealDone</c> — выданный щит в замер не попадает. Кит, держащий своих щитами, будет
        /// выглядеть пустым при полной работе.</para>
        /// </remarks>
        public float GetHealNorm(UnitClass unitClass)
        {
            for (int i = 0; i < _profiles.Length; i++)
                if (_profiles[i].Class == unitClass) return _baseHps * _profiles[i].HealMult;
            return 0f;
        }

        /// <summary>
        /// Ожидаемый запас прочности класса против ФИЗИЧЕСКОГО урона: <c>HP × (1 + физброня / K)</c>,
        /// где физброня — половина классового бюджета. <paramref name="armorK"/> берётся из
        /// <c>StatsConfig.ArmorConstantK</c>.
        /// </summary>
        /// <remarks>
        /// Считается по физике, потому что эталонный источник урона в бенче выживаемости — физический.
        /// Норма голая: без лечения, щитов и уклонений. Разрыв между ней и замеренным EHP — ровно вклад
        /// механик кита, и читать его надо именно так, а не как «ошибку нормы».
        /// </remarks>
        public float GetEhpNorm(UnitClass unitClass, float armorK)
        {
            (float hpMult, float _) = GetMultipliers(unitClass);
            float physArmor = GetArmorBudget(unitClass) * 0.5f;
            float mitigation = armorK > 0f ? 1f + physArmor / armorK : 1f;
            return _baseHp * hpMult * mitigation;
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

            [Tooltip("Сумма физической и магической брони класса. Танк 120, Брузер 60, Убийца 30, бэклайн 20.")]
            public float ArmorBudget;

            [Tooltip("Множитель ожидаемого DPS от эталона. Только норма для стенда — в бой не подставляется.")]
            public float DpsMult;

            [Tooltip("Множитель ожидаемого ЛЕЧЕНИЯ от эталона BaseHps. Ненулевой ТОЛЬКО у Целителя: " +
                     "у остальных классов лечение — свойство отдельного кита, а не обязанность роли. " +
                     "Только норма для стенда.")]
            public float HealMult;

            public ClassProfile(UnitClass unitClass, float hpMult, float moveSpeedMult,
                float armorBudget = 0f, float dpsMult = 1f, float healMult = 0f)
            {
                Class = unitClass;
                HpMult = hpMult;
                MoveSpeedMult = moveSpeedMult;
                ArmorBudget = armorBudget;
                DpsMult = dpsMult;
                HealMult = healMult;
            }
        }
    }
}
