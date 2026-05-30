namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Фасад звуковой подсистемы. Фаза 1 — Unity Audio заглушка;
    /// FMOD интегрируется в Фазе 9 за этим интерфейсом.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>Воспроизвести звук по ключу.</summary>
        void Play(string soundKey);

        /// <summary>Остановить звук по ключу.</summary>
        void Stop(string soundKey);

        /// <summary>Установить громкость музыки [0, 1].</summary>
        void SetMusicVolume(float volume);
    }
}
