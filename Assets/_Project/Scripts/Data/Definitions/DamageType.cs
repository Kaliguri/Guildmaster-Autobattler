namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Полный тип урона ОДНОГО источника (автоатака, способность, DoT) — логический дескриптор,
    /// собираемый из плоских полей источника с учётом <c>Inherit</c>-override
    /// (см. <see cref="DamageCategories"/>). В бою пайплайн считает только <see cref="School"/> +
    /// <see cref="Affinity"/> (броня + тип существа); <see cref="PhysicalSubtype"/>/
    /// <see cref="MagicElement"/> — качественные метки «быстрого чтения» (задел «влияют на урон позже»).
    /// <para>Инвариант нормализации: подтип живёт только при <see cref="DamageSchool.Physical"/>,
    /// элемент — только при <see cref="DamageSchool.Magical"/>; иначе соответствующая ось = None.</para>
    /// </summary>
    public readonly struct DamageType
    {
        public readonly DamageSchool School;
        public readonly PhysicalSubtype PhysicalSubtype;
        public readonly MagicElement MagicElement;
        public readonly DamageAffinity Affinity;

        public DamageType(DamageSchool school, PhysicalSubtype physicalSubtype,
                          MagicElement magicElement, DamageAffinity affinity)
        {
            // Нормализация: конкретика релевантна только своей школе.
            School = school;
            PhysicalSubtype = school == DamageSchool.Physical ? physicalSubtype : PhysicalSubtype.None;
            MagicElement    = school == DamageSchool.Magical  ? magicElement    : MagicElement.None;
            Affinity = affinity;
        }

        public bool IsPhysical => School == DamageSchool.Physical;
        public bool IsMagical  => School == DamageSchool.Magical;
        public bool IsTrue     => School == DamageSchool.True;

        /// <summary>Задана ли конкретика (подтип или элемент) сверх зонтика-школы.</summary>
        public bool HasSpecific => PhysicalSubtype != PhysicalSubtype.None || MagicElement != MagicElement.None;
    }
}
