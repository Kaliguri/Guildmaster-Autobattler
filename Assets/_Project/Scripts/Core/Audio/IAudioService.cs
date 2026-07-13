namespace Guildmaster.Core.Audio
{
    /// <summary>
    /// Фасад звуковой подсистемы. Живёт в Core, чтобы нижние слои (Presentation-аудио-презентер) звали
    /// его без ссылки на композит-рут Game — тот же приём, что у <see cref="Guildmaster.Core.Localization.ILocalizationService"/>.
    /// Фаза 1 — Unity Audio заглушка; FMOD интегрируется в Фазе 9 за этим интерфейсом.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>Воспроизвести звук по ключу.</summary>
        void Play(string soundKey);

        /// <summary>Остановить звук по ключу.</summary>
        void Stop(string soundKey);

        /// <summary>Установить громкость музыки [0, 1].</summary>
        void SetMusicVolume(float volume);

        /// <summary>
        /// Задать глобальный параметр микса (имена — <see cref="AudioParameters"/>). Напр. TimeScale —
        /// slowmo-питч боевой шины. Строки, не FMOD-типы, чтобы Core не знал о звижке.
        /// </summary>
        void SetGlobalParameter(string name, float value);
    }
}
