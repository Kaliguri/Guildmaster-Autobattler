namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Запрос открыть loadout-экран для юнита (публикует фаза расстановки по дабл-клику, слушает UI).
    /// Пейлоад — типы уровня Data (не <c>RuntimeUnit</c> из Combat), чтобы UI-сборка не зависела от боевого
    /// слоя — тем же приёмом, что боевые события несут только Data/Combat-safe типы.
    /// </summary>
    public readonly struct OpenLoadoutRequest
    {
        /// <summary>Id боевого юнита (в текущем бою), чей кит меняем.</summary>
        public readonly int UnitId;

        /// <summary>Текущий надетый релик (для подсветки в гриде), может быть null.</summary>
        public readonly RelicData CurrentRelic;

        /// <summary>Сосуд юнита (опц.), сохраняется при смене релика.</summary>
        public readonly VesselData Vessel;

        public OpenLoadoutRequest(int unitId, RelicData currentRelic, VesselData vessel)
        {
            UnitId       = unitId;
            CurrentRelic = currentRelic;
            Vessel       = vessel;
        }
    }

    /// <summary>
    /// Запрос надеть релик на юнита (публикует loadout-UI по «Принять»/«Сохранить», слушает фаза
    /// расстановки — она владеет ростером и пересобирает превью). Релик = весь боевой кит (вики «13» §3.1).
    /// </summary>
    public readonly struct EquipRelicRequest
    {
        public readonly int       UnitId;
        public readonly RelicData Relic;

        public EquipRelicRequest(int unitId, RelicData relic)
        {
            UnitId = unitId;
            Relic  = relic;
        }
    }
}
