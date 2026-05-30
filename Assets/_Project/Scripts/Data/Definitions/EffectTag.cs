using System;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Категориальные теги эффекта: для диспела по категории, AI-фильтров и быстрой
    /// битовой маски активных эффектов на юните (вики «6» §5.3–§5.4, «12» §2.1).
    /// <c>[Flags]</c> — один эффект может нести несколько категорий (напр. Debuff | DoT).
    /// </summary>
    [Flags]
    public enum EffectTag
    {
        None    = 0,
        Buff    = 1 << 0,
        Debuff  = 1 << 1,
        Control = 1 << 2,
        DoT     = 1 << 3,
        HoT     = 1 << 4,
        Heal    = 1 << 5,
        Shield  = 1 << 6,
    }
}
