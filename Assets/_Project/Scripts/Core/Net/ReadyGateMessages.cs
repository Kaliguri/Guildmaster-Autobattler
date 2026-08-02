namespace Guildmaster.Core.Net
{
    /// <summary>
    /// Сколько игроков подтвердили действие и сколько нужно. Вещается на каждое изменение счёта.
    /// </summary>
    /// <remarks>
    /// Кнопка живёт выше сеанса и переживает несколько сеансов подряд, поэтому счёт приходит к ней
    /// сообщением, а не подпиской на сам гейт — см. <see cref="IReadyGate"/>.
    /// </remarks>
    public readonly struct ReadyGateChangedEvent
    {
        public readonly int  Ready;
        public readonly int  Required;
        public readonly bool LocallyReady;

        public ReadyGateChangedEvent(int ready, int required, bool locallyReady)
        {
            Ready        = ready;
            Required     = required;
            LocallyReady = locallyReady;
        }
    }
}
