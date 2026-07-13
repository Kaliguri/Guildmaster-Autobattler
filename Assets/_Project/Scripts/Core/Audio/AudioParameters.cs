namespace Guildmaster.Core.Audio
{
    /// <summary>
    /// Имена глобальных FMOD-параметров, которые пишет игровой код (вики impl «09» §2.6).
    /// Строковый контракт с FMOD-проектом: параметр с таким именем должен существовать в Studio.
    /// </summary>
    public static class AudioParameters
    {
        /// <summary>
        /// Масштаб времени боя → питч боевой шины (slowmo/скорость игры). Единственный писатель —
        /// <see cref="Guildmaster.Core.Audio"/>-потребитель <c>TimeScaleService</c>. FMOD не следит за
        /// <c>Time.timeScale</c> сам, поэтому связь идёт через этот параметр.
        /// </summary>
        public const string TimeScale = "TimeScale";
    }
}
