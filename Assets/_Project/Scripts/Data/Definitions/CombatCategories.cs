namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Школа урона — числовая ось, которую гасит броня (ГДД «8» §«Школа vs сродство»).
    /// Школ намеренно мало: <see cref="Physical"/> (гасится физ. бронёй) и <see cref="Elemental"/>
    /// (Огонь/Лёд/Молния под ОДНОЙ стихийной бронёй; различия элементов живут в механике, не в резистах).
    /// <see cref="True"/> идёт мимо брони.
    /// <para>Int-значения совпадают с легаси <c>DamageType</c> (Magic=1 → Elemental=1) — ассеты не мигрируют.</para>
    /// </summary>
    public enum DamageSchool
    {
        Physical = 0,
        Elemental = 1,
        True = 2,
    }

    /// <summary>
    /// Физический подтип автоатаки (Дробящий/Режущий/Колющий) — актуален при <see cref="DamageSchool.Physical"/>.
    /// Сейчас питает тег «быстрого чтения» (ГДД <c>unit-tag-glossary</c>); может влиять на урон/резисты позже.
    /// <see cref="None"/> = не задан (нефиз-урон или не указан).
    /// </summary>
    public enum PhysicalSubtype
    {
        None = 0,
        Blunt = 1,   // Дробящий
        Slash = 2,   // Режущий
        Pierce = 3,  // Колющий
    }

    /// <summary>
    /// Сродство урона — качественная ось. Бронёй НЕ гасится, взаимодействует с <see cref="CreatureType"/>
    /// цели (ГДД «8»): Нежить/Конструкты иммунны к Яду, Нежить/Демоны уязвимы к Свету, Живое — к Тьме.
    /// Накладывается ПОВЕРХ школы (в т.ч. поверх <see cref="DamageSchool.True"/>). Таблица — <c>AffinityTable</c>.
    /// </summary>
    public enum DamageAffinity
    {
        None = 0,
        Poison = 1,
        Light = 2,
        Dark = 3,
    }

    /// <summary>
    /// Тип существа — таксономия юнита (у реликвий тоже, не только у врагов). Драйвит сродства.
    /// Отдельно от фракции (фракция — организационная группа, тип существа — что юнит есть).
    /// </summary>
    public enum CreatureType
    {
        Living = 0,
        Undead = 1,
        Construct = 2,
        Demon = 3,
        Beast = 4,
    }

    /// <summary>Школа урона способности: <see cref="Inherit"/> = взять школу юнита-кастера (ГДД: школа задаётся каждой атаке/способности отдельно).</summary>
    public enum DamageSchoolOverride
    {
        Inherit = 0,
        Physical = 1,
        Elemental = 2,
        True = 3,
    }

    /// <summary>Сродство урона способности: <see cref="Inherit"/> = взять сродство юнита-кастера.</summary>
    public enum DamageAffinityOverride
    {
        Inherit = 0,
        None = 1,
        Poison = 2,
        Light = 3,
        Dark = 4,
    }

    /// <summary>Разрешение override-ов школы/сродства способности в конкретные значения.</summary>
    public static class DamageCategories
    {
        public static DamageSchool Resolve(DamageSchoolOverride ovr, DamageSchool unitSchool)
        {
            switch (ovr)
            {
                case DamageSchoolOverride.Physical:  return DamageSchool.Physical;
                case DamageSchoolOverride.Elemental: return DamageSchool.Elemental;
                case DamageSchoolOverride.True:      return DamageSchool.True;
                default:                             return unitSchool;
            }
        }

        public static DamageAffinity Resolve(DamageAffinityOverride ovr, DamageAffinity unitAffinity)
        {
            switch (ovr)
            {
                case DamageAffinityOverride.None:   return DamageAffinity.None;
                case DamageAffinityOverride.Poison: return DamageAffinity.Poison;
                case DamageAffinityOverride.Light:  return DamageAffinity.Light;
                case DamageAffinityOverride.Dark:   return DamageAffinity.Dark;
                default:                            return unitAffinity;
            }
        }
    }

    /// <summary>Способ доставки автоатаки (вики «11» §2).</summary>
    public enum AttackType
    {
        /// <summary>Ближний, урон мгновенно.</summary>
        Melee = 0,

        /// <summary>Снаряд до одной цели.</summary>
        Ranged = 1,

        /// <summary>Снаряд + AOE в точке попадания.</summary>
        ProjectileAoe = 2,

        /// <summary>Пробивающий снаряд (летит сквозь цели).</summary>
        ProjectilePierce = 3,
    }

    /// <summary>Тип ресурса геройской реликвии. Реген и модель восстановления — позже (вики «11» §2, §3.7).</summary>
    public enum ResourceType
    {
        None = 0,
        Mana = 1,
        Rage = 2,
    }

    /// <summary>
    /// Полярность таймированного эффекта. Определяет, какая пара эфф-эффектов длительности
    /// применяется (Apply/Receive × Buff/Debuff) — вики «11» §5.
    /// </summary>
    public enum EffectPolarity
    {
        Buff = 0,
        Debuff = 1,
        Neutral = 2,
    }

    /// <summary>
    /// Фильтр полярности для диспела: что снимать. <see cref="Any"/> покрывает оба механизма —
    /// purge (снять баффы врага) и cleanse (снять дебаффы союзника) — одним компонентом (вики «6» §5.4).
    /// </summary>
    public enum DispelTargetPolarity
    {
        Any = 0,
        Buff = 1,
        Debuff = 2,
    }
}
