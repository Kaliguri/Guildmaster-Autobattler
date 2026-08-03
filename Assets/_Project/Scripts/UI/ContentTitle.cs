namespace Guildmaster.UI
{
    /// <summary>
    /// Заголовок карточки из content id — одна реализация на игру и на витрину превью.
    /// <para>Разбор id живёт здесь, потому что раньше жил в двух местах с РАЗНОЙ семантикой: игра резала
    /// по последней точке, витрина — по первой. На сегодняшних id (<c>relic.flame_swordsman</c>, ровно одна
    /// точка) обе дают одно и то же, поэтому расхождение было невидимым и ждало первого id с двумя точками;
    /// пустой id при этом уже давал «—» в игре и «The » на стенде (аудит 2026-07-26, R1-78).</para>
    /// <para>Правильная семантика — отбросить ДОМЕН, то есть всё до первой точки: id строится как
    /// <c>domain.lower_snake</c> (<c>ContentDomains.MakeId</c>), и домен ровно один.</para>
    /// </summary>
    public static class ContentTitle
    {
        /// <summary>Прочерк для отсутствующего заголовка — общий и для игры, и для стенда.</summary>
        public const string Missing = "—";

        /// <summary>
        /// Титул в стиле аркана таро (решение по ГДД): <c>relic.flame_swordsman</c> → <c>The Flame Swordsman</c>.
        /// Пустой id → <see cref="Missing"/>, а не «The » с пустым хвостом.
        /// </summary>
        public static string Arcana(string id)
        {
            if (string.IsNullOrEmpty(id)) return Missing;

            string name = WithoutDomain(id);
            return string.IsNullOrEmpty(name) ? Missing : "The " + TitleCase(name);
        }

        /// <summary>Имя без домена: всё после первой точки (<c>relic.base</c> → <c>base</c>).</summary>
        public static string WithoutDomain(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int dot = id.IndexOf('.');
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }

        /// <summary>Слова с заглавных, <c>_</c> считается пробелом: <c>flame_swordsman</c> → <c>Flame Swordsman</c>.</summary>
        public static string TitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            string[] parts = s.Replace('_', ' ').Split(' ');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);

            return string.Join(" ", parts);
        }
    }
}
