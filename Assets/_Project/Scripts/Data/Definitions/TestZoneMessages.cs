namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// ИНТЕНТ тумблера тест-зоны (QA #2): «Бой» вне реального боя → войти/выйти из расстановки отряда на серой
    /// тест-арене (без врагов — полигон для расстановки/реликвий). Публикует топбар (UI). Единственный владелец
    /// РЕШЕНИЯ вход/выход — <c>DeploymentController</c> (по своему состоянию: есть отряд, не боевая расстановка,
    /// не идёт бой). Он же вещает результат через <see cref="TestZoneChangedEvent"/>.
    /// </summary>
    public readonly struct ToggleTestZoneRequest { }

    /// <summary>
    /// СОСТОЯНИЕ тест-зоны (Ф5 UI-реворка): владелец (<c>DeploymentController</c>) вещает «стало так» после
    /// решения по <see cref="ToggleTestZoneRequest"/>. Единый источник вместо трёх рассинхронящихся флагов
    /// (<c>_testZone</c>/<c>_gray</c>/<c>_testActive</c>). Слушатели — арт серой зоны (<c>TestZoneArenaSkin</c>)
    /// и UI (тест-зона = Sheet-экран навигатора); оба лишь СЛУШАЮТ, не тоглят сами (смерть самотога, QA #28/#31).
    /// </summary>
    public readonly struct TestZoneChangedEvent
    {
        public readonly bool Active;
        public TestZoneChangedEvent(bool active) => Active = active;
    }
}
