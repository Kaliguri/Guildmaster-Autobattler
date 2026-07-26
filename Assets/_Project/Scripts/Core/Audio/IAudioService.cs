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

        /// <summary>
        /// Остановить звук по ключу. Работает для длящихся звуков (музыка/амбиент) — у них хранится
        /// инстанс; one-shot останавливать нечего и не нужно.
        /// </summary>
        void Stop(string soundKey);

        /// <summary>Погасить все длящиеся звуки разом: смена сцены, выход в меню, конец забега.</summary>
        void StopAll();

        /// <summary>Установить громкость мастер-шины [0, 1] (общая громкость, bus:/).</summary>
        void SetMasterVolume(float volume);

        /// <summary>Установить громкость музыки [0, 1] (bus:/Music).</summary>
        void SetMusicVolume(float volume);

        /// <summary>Установить громкость SFX-шины [0, 1] (bus:/SFX).</summary>
        void SetSfxVolume(float volume);

        /// <summary>
        /// Задать глобальный параметр микса (имена — <see cref="AudioParameters"/>). Напр. TimeScale —
        /// slowmo-питч боевой шины. Строки, не FMOD-типы, чтобы Core не знал о звижке.
        /// </summary>
        void SetGlobalParameter(string name, float value);
    }
}
