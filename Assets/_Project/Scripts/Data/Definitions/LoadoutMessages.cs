using UnityEngine;

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

    /// <summary>
    /// Запрос надеть релик на сосуд ПОД КУРСОРОМ (публикует UITK-панель расстановки на дропе карточки релика
    /// в поле). Юнита панель не знает — несёт лишь экранную позицию дропа; фаза расстановки резолвит сосуд
    /// сама (<c>ScreenToWorld</c> + <c>PickUnit</c>, тот же путь и источник экрана, что и деплой-пикинг, — потому
    /// попадание совпадает до пикселя). Так UI-слой не тащит камеру/пикинг, а Game-слой не знает про UITK.
    /// </summary>
    public readonly struct EquipRelicAtCursorRequest
    {
        public readonly RelicData Relic;
        public readonly Vector2   ScreenPosition;

        public EquipRelicAtCursorRequest(RelicData relic, Vector2 screenPosition)
        {
            Relic          = relic;
            ScreenPosition = screenPosition;
        }
    }
}
