namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Формат чанка боевой ленты — числа, общие для писателя и читателя. Живут в одном месте, потому что
    /// расхождение здесь даёт не ошибку, а тихо неправильную картинку у гостя.
    /// </summary>
    public static class TapeChunkFormat
    {
        /// <summary>
        /// Версия формата. Едет в заголовке: чанк из другой версии игры лучше отвергнуть громко, чем
        /// прочитать как попало. Handshake отпечатка ловит это раньше, но чанк обязан уметь защититься сам —
        /// он же ложится в реплей на диск, который живёт дольше сессии.
        /// </summary>
        public const byte Version = 1;

        /// <summary>
        /// Сколько тиков кладём в один чанк. Секунда показа: чанк остаётся в единицах КБ, а потеря
        /// стоит одной секунды боя.
        /// <para>Верхний предел — 255: смещение тика внутри чанка едет одним байтом. Это не экономия ради
        /// экономии, а причина, по которой чанк не растят «просто так».</para>
        /// </summary>
        public const int DefaultTicksPerChunk = 30;

        /// <summary>
        /// Рабочий потолок размера чанка, байт. Много меньше предела транспорта (512 КБ у Steam)
        /// сознательно: потеря дешевле, прогресс раздачи виден, а фрагментация Steam не выручает от
        /// чанка, который наш код обязан проверить сам — ниже нас его не проверяет никто.
        /// </summary>
        public const int MaxChunkBytes = 64 * 1024;

        /// <summary>Биты маски изменений снимка юнита. Порядок бит — часть формата, не переставлять.</summary>
        public static class UnitField
        {
            public const uint Position        = 1u << 0;
            public const uint CurrentHp       = 1u << 1;
            public const uint MaxHp           = 1u << 2;
            public const uint Shield          = 1u << 3;
            public const uint Resource        = 1u << 4;
            public const uint MaxResource     = 1u << 5;
            public const uint Size            = 1u << 6;
            public const uint Phase           = 1u << 7;
            public const uint WindupTicks     = 1u << 8;
            public const uint WindupRemaining = 1u << 9;
            public const uint AttackCooldown  = 1u << 10;
            public const uint RecoveryTicks   = 1u << 11;
            public const uint RecoveryLeft    = 1u << 12;
            public const uint ChannelPeriod   = 1u << 13;
            public const uint ChannelLeft     = 1u << 14;
            public const uint TargetId        = 1u << 15;
            public const uint EffectTags      = 1u << 16;
            public const uint Flags           = 1u << 17;
            public const uint AttackRange     = 1u << 18;
            public const uint SprintRamp      = 1u << 19;

            /// <summary>Всё сразу — так едет первое появление юнита в чанке.</summary>
            public const uint All = 0xFFFFFu;
        }

        /// <summary>Биты булевых признаков юнита, упакованных в один байт.</summary>
        public static class UnitFlag
        {
            public const byte IsDead       = 1 << 0;
            public const byte CanAct       = 1 << 1;
            public const byte IsDisplaced  = 1 << 2;
            public const byte IsEmpowered  = 1 << 3;
            public const byte ChargedSwing = 1 << 4;

            /// <summary>Полёт начат самим юнитом (рывок), а не чужим толчком — по нему показ тянет шлейф.</summary>
            public const byte SelfDisplaced = 1 << 5;
        }
    }
}
