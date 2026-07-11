namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Читаемый интерфейс аудио-каталога для <see cref="AudioResolver"/> — только предикаты наличия,
    /// без FMOD-типов, чтобы резолвер покрывался юнит-тестом с фейк-каталогом (acceptance §13.7).
    /// </summary>
    public interface IAudioCatalog
    {
        /// <summary>Есть ли точная запись под ключ <c>{contentId}.{action}</c>.</summary>
        bool Contains(string key);

        /// <summary>Есть ли дефолт для действия (fallback, когда точной записи нет).</summary>
        bool HasDefault(AudioAction action);
    }
}
