using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Data.Descriptions
{
    /// <summary>
    /// Сборка player-facing текста и чисел (план UI-реворка §II.10.1–II.10.4). Единственная
    /// дорога, по которой величина попадает игроку на глаза: тултип, панель юнита, карточка
    /// в инвентаре и экран награды берут числа ОТСЮДА, а не считают их у себя.
    /// </summary>
    /// <remarks>
    /// Живёт в слое данных, а не в UI: описание — свойство контента, и оно одинаково нужно
    /// экрану, тултипу и (в будущем) справочнику. UI решает только, КАК это показать.
    /// </remarks>
    public interface IDescriptionService
    {
        /// <summary>Локализованное имя контента (<c>{id}.name</c>).</summary>
        string Name(ContentDefinition def);

        /// <summary>
        /// Описание контента (<c>{id}.desc</c>) с подстановкой именованных величин.
        /// <paramref name="args"/> может быть <c>null</c> — тогда строка берётся как есть.
        /// </summary>
        string Describe(ContentDefinition def, IReadOnlyDictionary<string, object> args);

        /// <summary>
        /// Полное описание (<c>{id}.desc.full</c>) — статья справочника; нет полного текста → краткое.
        /// </summary>
        string DescribeFull(ContentDefinition def, IReadOnlyDictionary<string, object> args);

        /// <summary>
        /// Форма имени ключевого слова в падеже (<c>gen</c>/<c>acc</c>/<c>plural</c>; <c>null</c> —
        /// именительный). Нужна не только разметке: её же берут списки эффектов и будущий справочник.
        /// </summary>
        string KeywordForm(string keywordId, string caseTag);

        /// <summary>
        /// Готовая к показу строка одного стата живого юнита: «47» либо, в подробном режиме,
        /// «30 + 12 (Пылающий клинок) = 47». Имена источников уже локализованы.
        /// </summary>
        string DescribeStat(IStatExplainer stats, StatType stat, bool detailed);

        /// <summary>
        /// Разобранный стат с локализованными именами источников — для UI, который рисует
        /// разбор сам (панель юнита строит из него строки с иконками, а не одну строку).
        /// </summary>
        FormattedStat Explain(IStatExplainer stats, StatType stat, bool detailed);
    }
}
