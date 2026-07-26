namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Производные ключи локализации контента (вики «13» §5–6): таблица <c>Content</c>,
    /// ключи <c>{id}.name</c> / <c>{id}.desc</c>.
    /// </summary>
    /// <remarks>
    /// Живёт в рантайме, а не в редакторном <c>ContentLocalization</c>, потому что ключи
    /// нужны обеим сторонам: редактор их СОЗДАЁТ и проверяет, рантайм — ЧИТАЕТ (разбор
    /// статов называет источники модификаторов, описания подставляют имена эффектов).
    /// Суффиксы записаны здесь один раз; редакторная политика ссылается сюда, чтобы
    /// «что создаём» и «что читаем» не разъехались.
    /// </remarks>
    public static class ContentKeys
    {
        public const string TableName = "Content";
        public const string NameSuffix = "name";
        public const string DescSuffix = "desc";

        /// <summary>Полное описание для справочника (<c>{id}.desc.full</c>) — у keyword их два (§II.10.7).</summary>
        public const string FullDescSuffix = "desc.full";

        // --- Падежные формы имени (§II.10.3, решение Макса: формы живут в ДАННЫХ) ---
        // Именительный — это базовый {id}.name; остальные падежи получают свой суффикс. Форма может быть
        // не заведена — тогда читатель откатывается на именительный: текст звучит хуже, но остаётся целым.

        /// <summary>Родительный: «снимает 2 стака {Горения}».</summary>
        public const string GenitiveSuffix = "name.gen";

        /// <summary>Винительный: «накладывает {Горение}».</summary>
        public const string AccusativeSuffix = "name.acc";

        /// <summary>Множественное число: «сжигает {стаки Горения}».</summary>
        public const string PluralSuffix = "name.plural";

        /// <summary>Ключ вида <c>{id}.{suffix}</c>; <c>null</c>, если у определения нет id.</summary>
        public static string KeyFor(ContentDefinition def, string suffix)
            => def == null || string.IsNullOrEmpty(def.Id) ? null : def.Id + "." + suffix;

        /// <summary>Ключ отображаемого имени контента (<c>{id}.name</c>).</summary>
        public static string NameKey(ContentDefinition def) => KeyFor(def, NameSuffix);

        /// <summary>Ключ описания контента (<c>{id}.desc</c>).</summary>
        public static string DescKey(ContentDefinition def) => KeyFor(def, DescSuffix);

        /// <summary>Ключ полного описания (<c>{id}.desc.full</c>) — статья справочника.</summary>
        public static string FullDescKey(ContentDefinition def) => KeyFor(def, FullDescSuffix);

        /// <summary>Суффикс имени в нужном падеже; неизвестный падеж — именительный.</summary>
        public static string FormSuffix(string caseTag)
        {
            switch (caseTag)
            {
                case "gen":    return GenitiveSuffix;
                case "acc":    return AccusativeSuffix;
                case "plural": return PluralSuffix;
                default:       return NameSuffix;
            }
        }

        /// <summary>Ключ имени в падеже по строковому id (<c>kw.burn</c> + <c>gen</c> → <c>kw.burn.name.gen</c>).</summary>
        public static string FormKey(string id, string caseTag)
            => string.IsNullOrEmpty(id) ? null : id + "." + FormSuffix(caseTag);
    }
}
