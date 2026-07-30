using Guildmaster.Core.Persistence;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Бэкенд <see cref="ISaveService"/> для данных ИГРОКА — наш собственный и единственный: JSON-файлы
    /// под <c>GameDataPath.Root/Saves</c> (ТЗ [[save-system]] §4). Сюда ложатся забег, профили, гильдии
    /// и предпочтения — всё, что должно доехать на второй компьютер.
    /// <para>Готовый плагин сюда не звали намеренно (реш. 2026-07-26): мы сохраняем данные, а не
    /// объекты — durable-состояние это плоский DTO по строковым id, — поэтому сильные стороны таких
    /// плагинов (графы объектов, ссылки на UnityEngine.Object, полиморфизм) решают проблему, которой у
    /// нас нет. Easy Save 3 лежал в проекте референсом и удалён 31.07.2026 вместе с пакетом
    /// visualscripting, который тянул за собой.</para>
    /// <para><b>Каталог <c>Saves/</c> — контракт со Steam Cloud:</b> Auto-Cloud синхронизирует его по маске
    /// <c>*.json</c> рекурсивно. Поэтому суффиксы служебных файлов идут ПОСЛЕ расширения
    /// (<c>run.json.bak</c>, не <c>run.bak.json</c>) — так они не подпадают под маску и мусор не едет в
    /// облако. Местами не менять.</para>
    /// <para>Настройки дисплея сюда НЕ кладутся — им место в <see cref="LocalJsonFileSaveService"/>.</para>
    /// </summary>
    public sealed class JsonFileSaveService : JsonFileSaveServiceBase
    {
        /// <summary>Корень синхронизируемых сохранений. По этой маске настроен Auto-Cloud.</summary>
        public const string SavesFolder = "Saves";

        public JsonFileSaveService() : base(SavesFolder) { }
    }
}
