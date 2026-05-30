using System;

namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Один стат-модификатор: что меняем, как и на сколько. Сериализуемый — годится и для
    /// авторинга на SO (стат-блок реликвии), и как рантайм-нагрузка для <c>Stats</c>.
    /// </summary>
    /// <remarks>
    /// Источник (для снятия модов источника разом) здесь НЕ хранится: им управляет рантайм
    /// <c>Stats</c> как ключом группировки (<c>AddModifiersFrom(source, …)</c> /
    /// <c>RemoveModifiersFrom(source)</c>, вики «10. Архитектура» §5.2). Поля публичные и
    /// изменяемые — этого требует сериализация Unity.
    /// </remarks>
    [Serializable]
    public struct StatModifier
    {
        public StatType Stat;
        public ModifierOp Op;
        public float Value;

        public StatModifier(StatType stat, ModifierOp op, float value)
        {
            Stat = stat;
            Op = op;
            Value = value;
        }
    }
}
