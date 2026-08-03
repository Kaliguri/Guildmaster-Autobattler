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
        /// <summary>
        /// Что подтверждают — «battle.start», «battle.continue». Без ключа экран не отличил бы свой счёт
        /// от чужого и показал бы «(1/2)» рядом с кнопкой, к которой это не относится.
        /// </summary>
        public readonly string Key;

        public readonly int  Ready;
        public readonly int  Required;
        public readonly bool LocallyReady;

        /// <summary>
        /// Согласие собралось целиком и действие произошло.
        /// </summary>
        /// <remarks>
        /// Отличить это от обычного сброса по одному счёту нельзя: и там и там становится ноль. А
        /// разница видимая — экран, который ждал согласия, обязан закрыться именно на срабатывании.
        /// Поэтому признак едет отдельным полем, в том числе по сети: гость действие не выполняет и
        /// узнать о нём иначе не может.
        /// </remarks>
        public readonly bool Fired;

        public ReadyGateChangedEvent(string key, int ready, int required, bool locallyReady, bool fired = false)
        {
            Key          = key;
            Ready        = ready;
            Required     = required;
            LocallyReady = locallyReady;
            Fired        = fired;
        }
    }
}
