namespace Guildmaster.Presentation
{
    /// <summary>Заглушка тряски: подставляется, когда камеры-рига в сцене нет (бой не должен падать из-за визуала).</summary>
    public sealed class NullScreenShake : IScreenShake
    {
        public void Shake(float intensity) { }
        public void ResetShake() { }
    }
}
