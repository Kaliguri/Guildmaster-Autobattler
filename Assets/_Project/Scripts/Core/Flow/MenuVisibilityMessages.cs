namespace Guildmaster.Core.Flow
{
    /// <summary>
    /// СОСТОЯНИЕ главного меню: открыто оно или нет. Вещает <c>MenuRouter</c> (владелец показа), слушает
    /// презентационный слой — за меню подкладывается тот же стол, что под картой акта, вместо пустоты,
    /// которую камера иначе заливает цветом очистки.
    /// </summary>
    /// <remarks>
    /// Лежит в <c>Core</c>, а не рядом с остальными сообщениями меню в <c>Guild</c>, намеренно: это шов
    /// между UI и презентацией, а <c>Presentation</c> на сборку <c>Guild</c> не ссылается и ссылаться не
    /// должна — слой отрисовки ничего не знает про забег.
    /// </remarks>
    public readonly struct MainMenuVisibilityChangedEvent
    {
        /// <summary>true — главное меню на экране, false — снято.</summary>
        public readonly bool Visible;

        public MainMenuVisibilityChangedEvent(bool visible) => Visible = visible;
    }
}
