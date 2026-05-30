namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Только-чтение собранных статов. Реализует рантайм <c>Stats</c> (Combat, Фаза 1 шаг 4);
    /// потребляют <see cref="ScalableValue"/> и (Фаза 2) компоненты эффектов/способностей.
    /// </summary>
    public interface IStatReader
    {
        /// <summary>Итоговое значение стата после всех модификаторов и клампов.</summary>
        float Get(StatType stat);
    }
}
