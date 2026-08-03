namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Статы, умеющие рассказать, из чего они собрались. Отделено от <see cref="IStatReader"/>,
    /// потому что это разные права: симуляции нужно только читать итог, показу — видеть разбор,
    /// и случайно утащить дорогой <c>Explain</c> в тик не должно быть возможности.
    /// </summary>
    public interface IStatExplainer : IStatReader
    {
        /// <summary>Разложить стат на базу и вклады источников (план UI-реворка §II.10.1).</summary>
        StatValue Explain(StatType stat);
    }
}
