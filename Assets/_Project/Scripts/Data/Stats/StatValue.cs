namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Смысловая размерность стата — определяет, КАК число показывается игроку
    /// («47», «25 %», «1.2/сек»). Форматирование решается данными, а не <c>ToString()</c>
    /// по месту вызова, иначе одно и то же значение выглядит по-разному на разных экранах
    /// (план UI-реворка §II.10.1).
    /// </summary>
    public enum ValueKind
    {
        /// <summary>Абсолютная величина: урон, HP, броня.</summary>
        Flat = 0,

        /// <summary>Доля от 0 до 1, показывается процентом: вампиризм, % пробивание.</summary>
        Percent = 1,

        /// <summary>Множитель вокруг 1.0: эффективности урона/лечения/кулдауна.</summary>
        Multiplier = 2,

        /// <summary>Секунды. Статов такой размерности сейчас нет — размерность нужна
        /// описаниям способностей (длительности эффектов), которые питает тот же слой.</summary>
        Seconds = 3,

        /// <summary>Величина в секунду: скорость атаки, реген, скорость снаряда/движения.</summary>
        PerSecond = 4,

        /// <summary>Мировые единицы: дальность атаки.</summary>
        Distance = 5,

        /// <summary>Целочисленный счёт: число пробиваемых целей.</summary>
        Count = 6,
    }

    /// <summary>
    /// Источник стат-модификатора, умеющий назвать себя игроку. <c>Stats</c> хранит источник
    /// как <c>object</c> (ему нужна только ссылочная тождественность для снятия модов), поэтому
    /// имя даётся опционально: реализовал интерфейс — попал в разбор тултипа поимённо, не
    /// реализовал — вклад всё равно посчитан, но безымянный.
    /// </summary>
    public interface IModifierSource
    {
        /// <summary>Ключ локализации отображаемого имени источника; <c>null</c> — источник безымянный.</summary>
        string ModifierSourceLocKey { get; }
    }

    /// <summary>
    /// Вклад одного модификатора в итоговое значение стата.
    /// </summary>
    /// <remarks>
    /// <see cref="Contribution"/> — это НЕ <see cref="Value"/>. Сырое значение процентного мода
    /// (<c>0.08</c>) ничего не говорит игроку, потому что зависит от базы и соседних модов.
    /// Вклад считается как разница «итог с этим модом минус итог без него» — единственная
    /// честная величина при смешанных Flat/PercentAdd/PercentMult, и именно она показывается
    /// в подробном режиме («+12 (Пылающий клинок)»).
    /// </remarks>
    public readonly struct StatTerm
    {
        /// <summary>Ключ локализации имени источника; <c>null</c>, если источник не назвался.</summary>
        public readonly string SourceLocKey;

        /// <summary>Операция модификатора — нужна UI, чтобы показать «+12» против «+8 %».</summary>
        public readonly ModifierOp Op;

        /// <summary>Сырое значение модификатора, как оно задано в данных.</summary>
        public readonly float Value;

        /// <summary>Сколько этот модификатор фактически добавил к итогу, в единицах стата.</summary>
        public readonly float Contribution;

        public StatTerm(string sourceLocKey, ModifierOp op, float value, float contribution)
        {
            SourceLocKey = sourceLocKey;
            Op = op;
            Value = value;
            Contribution = contribution;
        }
    }

    /// <summary>
    /// Разложенное значение стата: во что оно собралось и из чего. Возвращается
    /// <c>Stats.Explain</c> и потребляется слоем описаний (план UI-реворка §II.10.1).
    /// </summary>
    /// <remarks>
    /// Это инспекция для показа игроку, а НЕ горячий путь симуляции: бой продолжает читать
    /// <c>Stats.Get</c>. Здесь допустимы аллокации, там — нет.
    /// </remarks>
    public readonly struct StatValue
    {
        public readonly StatType Stat;

        /// <summary>
        /// База, от которой считается всё остальное: <see cref="ModifierOp.Override"/> из данных
        /// юнита, если задан, иначе дефолт конфига. Override сознательно НЕ попадает в
        /// <see cref="Terms"/> — это способ авторинга базовых статов мементо, а не бонус
        /// поверх них, и игроку он показывается как «База», а не как «+N от чего-то».
        /// </summary>
        public readonly float Base;

        /// <summary>Итог после всех модификаторов; совпадает с <c>Stats.Get(Stat)</c>.</summary>
        public readonly float Final;

        /// <summary>Вклады модификаторов в порядке наложения; никогда не <c>null</c>.</summary>
        public readonly StatTerm[] Terms;

        public readonly ValueKind Kind;

        public StatValue(StatType stat, float baseValue, float final, StatTerm[] terms, ValueKind kind)
        {
            Stat = stat;
            Base = baseValue;
            Final = final;
            Terms = terms ?? System.Array.Empty<StatTerm>();
            Kind = kind;
        }

        /// <summary>Значение отличается от базы — UI подкрашивает такие числа.</summary>
        public bool IsModified => Terms.Length > 0;

        /// <summary>Суммарная надбавка сверх базы.</summary>
        public float Bonus => Final - Base;
    }
}
