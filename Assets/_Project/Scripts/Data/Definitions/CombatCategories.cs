namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Школа урона — числовая ось, которую гасит броня (ГДД «8» §«Школа vs сродство»).
    /// Школ намеренно мало: <see cref="Physical"/> (гасится физ. бронёй) и <see cref="Magical"/>
    /// (Огонь/Лёд/Молния/Аркана под ОДНОЙ магической бронёй; различия элементов живут в механике, не в резистах).
    /// <see cref="True"/> идёт мимо брони.
    /// <para>Int-значения стабильны (Magical=1) — ассеты со старым <c>_damageSchool: 1</c> не мигрируют.</para>
    /// </summary>
    public enum DamageSchool
    {
        Physical = 0,
        Magical = 1,
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
    /// Сродство урона — качественная ось поверх школы (в т.ч. поверх <see cref="DamageSchool.True"/>).
    /// <para><b>На число урона не влияет и множителей не имеет.</b> Сродство несёт идентичность
    /// <b>механикой — глаголом</b>: Яд травит (DoT + дебафф), Свет очищает и лечит частью урона, Тьма
    /// бьёт голой мощью. Универсальная матрица «сродство × <see cref="CreatureType"/>» была отклонена
    /// решением 2026-07-15/35 и снята из кода 2026-07-26: сродство должно работать против любого врага,
    /// а не зависеть от того, повезло ли с типом цели.</para>
    /// </summary>
    public enum DamageAffinity
    {
        None = 0,
        Poison = 1,
        Light = 2,
        Dark = 3,
    }

    /// <summary>
    /// Магический элемент урона — актуален при <see cref="DamageSchool.Magical"/> (аналог
    /// <see cref="PhysicalSubtype"/> для физики). Все элементы гасятся ОДНОЙ магической бронёй —
    /// различия живут в механике (поджог/заморозка/цепь), не в резистах. Питает тег «быстрого чтения»;
    /// может влиять на урон позже. <see cref="Arcane"/> = чистая магия без стихии (механика — задел).
    /// <see cref="None"/> = не задан (нефиз-урон без конкретной стихии или не указан).
    /// </summary>
    public enum MagicElement
    {
        None = 0,
        Fire = 1,      // Огонь
        Ice = 2,       // Лёд
        Lightning = 3, // Молния
        Arcane = 4,    // Аркана — чистая магия без стихии (задел, механики пока нет)
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
        Magical = 2,
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

    /// <summary>Физ-подтип урона способности: <see cref="Inherit"/> = взять подтип юнита-кастера.</summary>
    public enum PhysicalSubtypeOverride
    {
        Inherit = 0,
        None = 1,
        Blunt = 2,
        Slash = 3,
        Pierce = 4,
    }

    /// <summary>Магический элемент урона способности: <see cref="Inherit"/> = взять элемент юнита-кастера.</summary>
    public enum MagicElementOverride
    {
        Inherit = 0,
        None = 1,
        Fire = 2,
        Ice = 3,
        Lightning = 4,
        Arcane = 5,
    }

    /// <summary>Разрешение override-ов школы/сродства способности в конкретные значения.</summary>
    public static class DamageCategories
    {
        public static DamageSchool Resolve(DamageSchoolOverride ovr, DamageSchool unitSchool)
        {
            switch (ovr)
            {
                case DamageSchoolOverride.Physical:  return DamageSchool.Physical;
                case DamageSchoolOverride.Magical: return DamageSchool.Magical;
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

        public static PhysicalSubtype Resolve(PhysicalSubtypeOverride ovr, PhysicalSubtype unitSubtype)
        {
            switch (ovr)
            {
                case PhysicalSubtypeOverride.None:   return PhysicalSubtype.None;
                case PhysicalSubtypeOverride.Blunt:  return PhysicalSubtype.Blunt;
                case PhysicalSubtypeOverride.Slash:  return PhysicalSubtype.Slash;
                case PhysicalSubtypeOverride.Pierce: return PhysicalSubtype.Pierce;
                default:                             return unitSubtype;
            }
        }

        public static MagicElement Resolve(MagicElementOverride ovr, MagicElement unitElement)
        {
            switch (ovr)
            {
                case MagicElementOverride.None:      return MagicElement.None;
                case MagicElementOverride.Fire:      return MagicElement.Fire;
                case MagicElementOverride.Ice:       return MagicElement.Ice;
                case MagicElementOverride.Lightning: return MagicElement.Lightning;
                case MagicElementOverride.Arcane:    return MagicElement.Arcane;
                default:                             return unitElement;
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
