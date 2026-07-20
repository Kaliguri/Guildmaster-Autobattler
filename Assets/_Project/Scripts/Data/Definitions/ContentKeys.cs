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

        /// <summary>Ключ вида <c>{id}.{suffix}</c>; <c>null</c>, если у определения нет id.</summary>
        public static string KeyFor(ContentDefinition def, string suffix)
            => def == null || string.IsNullOrEmpty(def.Id) ? null : def.Id + "." + suffix;

        /// <summary>Ключ отображаемого имени контента (<c>{id}.name</c>).</summary>
        public static string NameKey(ContentDefinition def) => KeyFor(def, NameSuffix);

        /// <summary>Ключ описания контента (<c>{id}.desc</c>).</summary>
        public static string DescKey(ContentDefinition def) => KeyFor(def, DescSuffix);
    }
}
