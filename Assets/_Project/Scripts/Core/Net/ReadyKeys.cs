namespace Guildmaster.Core.Net
{
    /// <summary>
    /// Ключи гейта готовности: что именно подтверждают участники.
    /// </summary>
    /// <remarks>
    /// Ключ — это ДОГОВОР между тем, кто объявляет согласие, и тем, кто рисует счёт: по нему
    /// подписчик отличает свой счёт от чужого (<see cref="ReadyGateChangedEvent.Key"/>). Пока ключи
    /// жили литералами в трёх файлах, договор держался только на том, что все три строки совпадают, —
    /// и топбар, забывший сверить ключ, показывал на кнопке «Начать» счёт гейта возврата к расстановке.
    /// </remarks>
    public static class ReadyKeys
    {
        /// <summary>Согласие начать бой.</summary>
        public const string BattleStart = "battle.start";

        /// <summary>Согласие уйти с экрана исхода обратно к расстановке.</summary>
        public const string BattleContinue = "battle.continue";
    }
}
