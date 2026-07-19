namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Сигнал «отряд забега готов встать на тест-арену» (persist-мир, план 12 Ф2). Публикуется из
    /// <see cref="Guildmaster.Game.Services.GameFlow"/> после <c>BeginAct</c>, когда <c>RunState.Guild</c>
    /// собран. Слушатель — <c>WorldStageController</c> в боевом скоупе: резолвит гильдию в боевой ростер и
    /// ставит отряд (team 0) на арену вне боя, замороженным. Пустой сигнал — данные слушатель берёт из
    /// <c>RunStateService.Current</c> (единственный источник истины забега).
    /// </summary>
    public readonly struct RunPartyReadyEvent { }
}
