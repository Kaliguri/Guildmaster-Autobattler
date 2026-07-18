using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать фасад сундука (план [[act-map-run-loop]] §4 B3): ЛКМ по крышке → <see cref="OnOpen"/>
    /// (флоу дальше катит награду 1-из-3). Публикует <c>ChestFlow</c>, слушает UI. Ровно один вызов.
    /// </summary>
    public readonly struct OpenChestRequest
    {
        /// <summary>Колбэк открытия крышки (ровно один вызов) — флоу показывает награду.</summary>
        public readonly Action OnOpen;

        public OpenChestRequest(Action onOpen) => OnOpen = onOpen;
    }
}
