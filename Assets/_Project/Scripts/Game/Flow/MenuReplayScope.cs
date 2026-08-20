namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Префаб боевого скоупа в режиме <see cref="BattleScopeMode.Replay"/> — из него директор фона меню
    /// поднимает воспроизведение записанной дуэли. Отдельный тип от боевого
    /// <see cref="Activity.BattleScopePrefab"/>: у них разные ассеты (у реплея <c>_mode = Replay</c>), а
    /// инъекция различает их по типу.
    /// </summary>
    public sealed class MenuReplayScope
    {
        public readonly CombatLifetimeScope Value;

        public MenuReplayScope(CombatLifetimeScope value) => Value = value;
    }
}
