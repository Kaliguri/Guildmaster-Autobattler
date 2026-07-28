namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// ИНТЕНТ целевого состояния тест-зоны (радио-режимы табов): топбар просит «войти в бой» (Active=true, из
    /// таба «Бой»/«Инвентарь») или «выйти» (Active=false, из таба «Карта»). ИДЕМПОТЕНТНО — повтор того же
    /// состояния = no-op (табы переключают режим, не тоглят). Единственный владелец РЕШЕНИЯ — <c>DeploymentController</c>
    /// (по своему состоянию: есть отряд, не боевая расстановка, не идёт бой); результат — <see cref="TestZoneChangedEvent"/>.
    /// </summary>
    public readonly struct SetTestZoneRequest
    {
        public readonly bool Active;
        public SetTestZoneRequest(bool active) => Active = active;
    }

    /// <summary>
    /// СОСТОЯНИЕ тест-зоны (Ф5 UI-реворка): владелец (<c>DeploymentController</c>) вещает «стало так» после
    /// решения по <see cref="SetTestZoneRequest"/>. Единый источник вместо трёх рассинхронящихся флагов
    /// (<c>_testZone</c>/<c>_gray</c>/<c>_testActive</c>). Слушатели — презентер арены (<c>ArenaStagePresenter</c>:
    /// обесцвечивание и сборка полигона) и UI (тест-зона = Sheet-экран навигатора); оба лишь СЛУШАЮТ,
    /// не тоглят сами (смерть самотога, QA #28/#31).
    /// </summary>
    public readonly struct TestZoneChangedEvent
    {
        public readonly bool Active;
        public TestZoneChangedEvent(bool active) => Active = active;
    }

    /// <summary>
    /// ИНТЕНТ «к построению» — кнопка передышки между узлами: встать в расстановку прямо на боевой арене
    /// (враги и трупы уже убраны возвратом мира). От <see cref="SetTestZoneRequest"/> отличается ровно одним:
    /// это НЕ полигон вне забега, поэтому арена остаётся цветной, а не серой. Решение принимает тот же
    /// владелец — <c>DeploymentController</c>; отказ (нет отряда, идёт бой) — молча, интент не приказ.
    /// </summary>
    public readonly struct SetFormationRequest
    {
        public readonly bool Active;
        public SetFormationRequest(bool active) => Active = active;
    }
}
