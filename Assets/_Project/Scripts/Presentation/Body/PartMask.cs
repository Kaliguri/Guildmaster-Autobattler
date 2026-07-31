namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Набор частей тела одним значением: бит на часть, номер бита — её индекс в теле
    /// (<see cref="IUnitPartLookup.Parts"/>). Так презентация говорит «светится ВОТ ЭТО», а не «светится
    /// что-то с такой ролью».
    /// </summary>
    /// <remarks>
    /// Маска ролей (оружие / щит / конечность) не годилась: у бойца с двумя кинжалами роль одна, а светиться
    /// должен один из них — именно этот случай и потребовал адресации. Битовая маска дешева в сравнении
    /// (<see cref="BodyVisualState"/> сравнивается каждый кадр) и не аллоцирует.
    /// <para>
    /// Отсюда предел: <see cref="MaxParts"/> частей на тело. У скелетного юнита их 16, так что запас
    /// четырёхкратный; попытка собрать тело шире ловится громкой ошибкой в <see cref="UnitPartRegistry"/>,
    /// а не молчаливой потерей частей за 64-м битом.
    /// </para>
    /// </remarks>
    public readonly struct PartMask : System.IEquatable<PartMask>
    {
        /// <summary>Сколько частей тела адресует маска. Ровно разрядность её носителя.</summary>
        public const int MaxParts = 64;

        private readonly ulong _bits;

        private PartMask(ulong bits) => _bits = bits;

        /// <summary>Ни одной части — свечение выключено.</summary>
        public static PartMask Empty => default;

        /// <summary>Все части тела: столько младших бит, сколько частей. Тело светится целиком.</summary>
        public static PartMask All(int partCount) =>
            partCount <= 0 ? Empty
            : partCount >= MaxParts ? new PartMask(ulong.MaxValue)
            : new PartMask((1UL << partCount) - 1UL);

        /// <summary>Одна часть по её индексу в теле. Индекс вне предела даёт пустую маску.</summary>
        public static PartMask Single(int index) =>
            index < 0 || index >= MaxParts ? Empty : new PartMask(1UL << index);

        public bool IsEmpty => _bits == 0UL;

        /// <summary>Входит ли часть с этим индексом в набор.</summary>
        public bool Has(int index) => index >= 0 && index < MaxParts && (_bits & (1UL << index)) != 0UL;

        /// <summary>Сколько частей в наборе.</summary>
        public int Count
        {
            get
            {
                // Считаем сами, а не через System.Numerics.BitOperations: он появился в .NET Core 3.0 и в
                // профиле Unity его может не быть, а падать этому типу нельзя — он на пути каждого кадра.
                ulong bits = _bits;
                int count = 0;
                while (bits != 0UL) { bits &= bits - 1UL; count++; }
                return count;
            }
        }

        /// <summary>Объединение наборов: «клинком И щитом» — это две части в одной маске.</summary>
        public static PartMask operator |(PartMask a, PartMask b) => new PartMask(a._bits | b._bits);

        public static bool operator ==(PartMask a, PartMask b) => a._bits == b._bits;
        public static bool operator !=(PartMask a, PartMask b) => a._bits != b._bits;

        public bool Equals(PartMask other) => _bits == other._bits;
        public override bool Equals(object obj) => obj is PartMask other && Equals(other);
        public override int GetHashCode() => _bits.GetHashCode();
        public override string ToString() => IsEmpty ? "PartMask(empty)" : $"PartMask(0x{_bits:X})";
    }
}
