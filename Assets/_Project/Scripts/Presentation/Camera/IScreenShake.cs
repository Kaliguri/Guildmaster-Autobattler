namespace Guildmaster.Presentation
{
    /// <summary>
    /// Тряска боевой камеры по запросу режиссёра (<c>CombatFeelDirector</c>). За интерфейсом — чтобы
    /// бой не зависел от наличия камеры-рига: если рига в сцене нет, регистрируется <see cref="NullScreenShake"/>.
    /// </summary>
    public interface IScreenShake
    {
        /// <summary>Тряхнуть камеру. <paramref name="intensity"/> 0..1 (клампится); удары складываются вверх (берётся максимум).</summary>
        void Shake(float intensity);
    }
}
