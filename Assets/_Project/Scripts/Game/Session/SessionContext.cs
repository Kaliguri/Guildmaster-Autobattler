namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Кем мы играем в этом сеансе. Лежит в скоупе Сессии и за её пределы не выходит: спрашивать роль
    /// имеет право только тот, кто внутри неё живёт.
    /// </summary>
    /// <remarks>
    /// Роль здесь — <b>справка, а не рубильник</b>. Решения по роли принимает <see cref="SessionInstaller"/>
    /// один раз составом; этот объект нужен тем, кому роль надо ПОКАЗАТЬ (интерфейс лобби, отладка) или
    /// записать в лог. Ветвление «if Guest» в игровой логике — признак того, что состав собран неверно.
    /// </remarks>
    public sealed class SessionContext
    {
        public SessionContext(SessionRole role) => Role = role;

        public SessionRole Role { get; }

        /// <summary>Мы владеем состоянием этого сеанса (играем в своём сейве).</summary>
        public bool IsOwner => Role == SessionRole.Owner;
    }
}
