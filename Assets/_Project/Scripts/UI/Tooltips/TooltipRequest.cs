namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Что именно просят показать. Ось «контент» из плана UI-реворка §II.10.5: видов тултипов
    /// не список, а произведение поведения на контент, поэтому здесь перечисляются РОДЫ данных,
    /// а не экраны, с которых пришёл запрос.
    /// </summary>
    public enum TooltipKind
    {
        /// <summary>Пустой запрос — показывать нечего (используется как «нет тултипа»).</summary>
        None = 0,

        /// <summary>Готовая строка: свёрнутый хвост тегов, подпись-пояснение у контрола.</summary>
        Text = 1,

        /// <summary>Реликвия по id: имя, кит, теги, описание.</summary>
        Relic = 2,

        /// <summary>Тег «быстрого чтения» по id: имя и что он значит.</summary>
        Tag = 3,

        /// <summary>Сосуд/юнит по id: имя, класс, базовая стат-сводка.</summary>
        Vessel = 4,

        /// <summary>Одна характеристика: итог и, в подробном режиме, из чего собралась.</summary>
        Stat = 5,

        /// <summary>Ключевое слово из текста описания (вложенный тултип, §II.10.5 п.4).</summary>
        Keyword = 6,
    }

    /// <summary>
    /// Запрос тултипа — ПО ДАННЫМ, а не по элементу (план §II.10.5 п.2). Место показа говорит
    /// «покажи реликвию <c>relic.ember_heart</c>», а не «покажи вот эту разметку».
    /// </summary>
    /// <remarks>
    /// Ради этого запрос и сделан значением с id: тултип юнита на арене, карточка в инвентаре и
    /// (в будущем) пинг партнёра в коопе просят одно и то же одинаково, и содержимое собирается
    /// в одном месте — <c>ITooltipContentFactory</c>. Если бы запрос нёс готовую разметку, каждое
    /// место рисовало бы своё, и «реликвия» выглядела бы по-разному на трёх экранах.
    /// </remarks>
    public readonly struct TooltipRequest
    {
        public readonly TooltipKind Kind;

        /// <summary>Идентификатор контента (<c>relic.*</c>, <c>tag.*</c>, <c>kw:*</c>, имя стата).</summary>
        public readonly string Id;

        /// <summary>Готовый текст для <see cref="TooltipKind.Text"/>; у остальных родов <c>null</c>.</summary>
        public readonly string Text;

        /// <summary>Заголовок готового текста (необязателен): «Ещё теги», «Слот способности».</summary>
        public readonly string Title;

        /// <summary>
        /// Владелец значения для <see cref="TooltipKind.Stat"/>: id сосуда/юнита, чьи статы разбираем.
        /// Стат сам по себе не существует — «Урон» без носителя нечего считать.
        /// </summary>
        public readonly string OwnerId;

        private TooltipRequest(TooltipKind kind, string id, string text, string ownerId, string title = null)
        {
            Kind    = kind;
            Id      = id;
            Text    = text;
            OwnerId = ownerId;
            Title   = title;
        }

        public bool IsEmpty => Kind == TooltipKind.None;

        /// <summary>Готовая строка (свёрнутые теги, короткое пояснение) с необязательным заголовком.</summary>
        public static TooltipRequest Plain(string text, string title = null)
            => string.IsNullOrEmpty(text) ? default : new TooltipRequest(TooltipKind.Text, null, text, null, title);

        public static TooltipRequest Relic(string relicId)
            => string.IsNullOrEmpty(relicId) ? default : new TooltipRequest(TooltipKind.Relic, relicId, null, null);

        public static TooltipRequest Tag(string tagId)
            => string.IsNullOrEmpty(tagId) ? default : new TooltipRequest(TooltipKind.Tag, tagId, null, null);

        public static TooltipRequest Vessel(string vesselId)
            => string.IsNullOrEmpty(vesselId) ? default : new TooltipRequest(TooltipKind.Vessel, vesselId, null, null);

        /// <summary><paramref name="statId"/> — имя <c>StatType</c>; <paramref name="ownerId"/> — чей стат.</summary>
        public static TooltipRequest Stat(string statId, string ownerId)
            => string.IsNullOrEmpty(statId) ? default : new TooltipRequest(TooltipKind.Stat, statId, null, ownerId);

        public static TooltipRequest Keyword(string keywordId)
            => string.IsNullOrEmpty(keywordId) ? default : new TooltipRequest(TooltipKind.Keyword, keywordId, null, null);

        /// <summary>Одинаковые запросы не пересобирают окно — hover по тому же контенту дёшев.</summary>
        public bool SameAs(in TooltipRequest other)
            => Kind == other.Kind && Id == other.Id && Text == other.Text
            && OwnerId == other.OwnerId && Title == other.Title;
    }
}
