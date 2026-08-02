using Newtonsoft.Json;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Правила сериализации durable-состояния — один владелец на все дороги, по которым оно уезжает из
    /// памяти: файл сейва и снимок забега, отправляемый гостю по сети.
    /// </summary>
    /// <remarks>
    /// <b>Зачем вынесено из сейв-сервиса.</b> Гость получает состояние забега тем же плоским DTO, каким
    /// оно ложится на диск (ТЗ кооп-вертикали §7: снимок сессии — это <c>run.json</c> по сети). Настройки,
    /// собранные вторым экземпляром рядом, разошлись бы молча: файл читался бы, а снимок — нет.
    /// <para><b>Готча, ради которой это особенно важно:</b> <c>Vector2</c> без своего конвертера уводит
    /// Newtonsoft в рекурсию по <c>normalized</c>. Забыть конвертер в одной из двух копий настроек — это
    /// не «слегка другой JSON», а зависший сериализатор на первой же позиции слота.</para>
    /// </remarks>
    public static class SaveJson
    {
        /// <summary>
        /// Настройки для durable-DTO. Каждый вызов отдаёт свой экземпляр: <c>JsonSerializerSettings</c>
        /// изменяем, и общий на всех позволил бы одному потребителю тихо перенастроить остальных.
        /// </summary>
        public static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                // Тип с новым полем должен читать старый файл: отсутствующее поле остаётся дефолтом
                // DTO, а не роняет загрузку. Бампа схемы такое изменение не требует (ТЗ [[save-system]] §5).
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Formatting            = Formatting.Indented,
            };
            settings.Converters.Add(new Vector2JsonConverter());
            return settings;
        }

        /// <summary>Готовый сериализатор с этими правилами.</summary>
        public static JsonSerializer CreateSerializer() => JsonSerializer.Create(CreateSettings());
    }
}
