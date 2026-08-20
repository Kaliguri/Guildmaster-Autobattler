using System.Collections.Generic;

namespace Guildmaster.Core.Net
{
    /// <summary>Чей голос и за что он отдан.</summary>
    /// <remarks>
    /// Нужен показу, а не механике: решение сравнивает голоса само. Экран награды рисует чужой выбор
    /// мейн-цветом того, кто его сделал, — «видно, кто что ткнул» из кооп-канона
    /// (<c>gdd/50-modes-ux/coop/interplay</c> §Метка выбранного узла).
    /// </remarks>
    public readonly struct PlayerChoice
    {
        public readonly int    PlayerId;
        public readonly string Option;

        public PlayerChoice(int playerId, string option)
        {
            PlayerId = playerId;
            Option   = option;
        }
    }

    /// <summary>
    /// Сколько игроков сделали выбор, сколько нужно и кто за что. Вещается на каждое изменение.
    /// </summary>
    /// <remarks>
    /// Кнопка живёт выше сеанса и переживает несколько сеансов подряд, поэтому счёт приходит к ней
    /// сообщением, а не подпиской на сам гейт — см. <see cref="ISharedDecision"/>.
    /// </remarks>
    public readonly struct SharedDecisionChangedEvent
    {
        /// <summary>
        /// Что подтверждают — «battle.start», «battle.continue». Без ключа экран не отличил бы свой счёт
        /// от чужого и показал бы «(1/2)» рядом с кнопкой, к которой это не относится.
        /// </summary>
        public readonly string Key;

        public readonly int  Voted;
        public readonly int  Required;
        public readonly bool HasLocalChoice;

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

        /// <summary>Наш вариант; пусто — не выбрали.</summary>
        public readonly string LocalChoice;

        /// <summary>
        /// Кто за что проголосовал. Пусто у решения-согласия: там вариант один, и рисовать нечего.
        /// </summary>
        public readonly IReadOnlyList<PlayerChoice> Choices;

        private static readonly PlayerChoice[] NoChoices = System.Array.Empty<PlayerChoice>();

        public SharedDecisionChangedEvent(string key, int ready, int required, bool locallyReady,
                                     bool fired = false, string localChoice = null,
                                     IReadOnlyList<PlayerChoice> choices = null)
        {
            Key          = key;
            Voted        = ready;
            Required     = required;
            HasLocalChoice = locallyReady;
            Fired        = fired;
            LocalChoice  = localChoice ?? DecisionOptions.None;
            Choices      = choices ?? NoChoices;
        }
    }
}
