using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать boot title card «Happy Guildmasters» один раз до главного меню.
    /// Публикует <c>GameFlow</c> на старте сессии, слушает UI. Ровно один вызов <see cref="OnDismiss"/>.
    /// </summary>
    public readonly struct OpenTitleCardRequest
    {
        /// <summary>Колбэк закрытия карточки (клик / авто-таймер / снятие экрана).</summary>
        public readonly Action OnDismiss;

        public OpenTitleCardRequest(Action onDismiss) => OnDismiss = onDismiss;
    }
}
