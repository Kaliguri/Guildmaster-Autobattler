namespace Guildmaster.Guild
{
    /// <summary>
    /// ИНТЕНТ «показать/скрыть карту в мире» — радио-табы топбара («Карта» / «Бой»/«Инвентарь»).
    /// Владелец состояния (<c>WorldMapController</c>) приводит мир к цели идемпотентно; результат вещается
    /// как <see cref="WorldMapSpaceChangedEvent"/>. Тем же приёмом, что <c>SetTestZoneRequest</c>.
    /// <para>Карту можно открыть и посреди боя: бой продолжается, камера просто уезжает в зону карты
    /// (зоны для того и разнесены), а закрытие возвращает взгляд ровно туда, где он был.</para>
    /// </summary>
    public readonly struct SetWorldMapRequest
    {
        /// <summary>true — показать карту в мире, false — убрать и вернуть камеру.</summary>
        public readonly bool Visible;

        public SetWorldMapRequest(bool visible) => Visible = visible;
    }

    /// <summary>
    /// СОСТОЯНИЕ world-карты (фаза D): карта показана в мире или нет. Вещает <c>WorldMapNodeChooser</c>
    /// (владелец состояния), слушает UI — держит поверх прозрачное Sheet-пространство с тегом режима "map"
    /// (топбар подсвечивает таб «Карта», навигатор переводит ввод в <c>InputContext.Map</c>).
    /// </summary>
    public readonly struct WorldMapSpaceChangedEvent
    {
        /// <summary>true — карта показана в мире (нужен Sheet поверх), false — скрыта (снять Sheet).</summary>
        public readonly bool Active;

        public WorldMapSpaceChangedEvent(bool active) => Active = active;
    }
}
