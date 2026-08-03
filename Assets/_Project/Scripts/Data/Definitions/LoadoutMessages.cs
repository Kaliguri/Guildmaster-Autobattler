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

    /// <summary>Фаза перетаскивания карточки реликвии из инвентаря к юниту на поле (QA #5).</summary>
    public enum RelicDragPhase { Start, Move, Drop }

    /// <summary>
    /// Перетаскивание карточки реликвии из грида инвентаря на юнита в мире (QA #5). Публикует UITK-грид
    /// (pointer-capture на карточке), слушает фаза расстановки: пока тащим (Start/Move) рисует world-призрак
    /// силуэта реликвии у курсора (единый вид «в руке», как drag юнита), на Drop надевает реликвию на юнита
    /// под курсором. Позицию курсора фаза расстановки берёт из своего источника ввода (тот же, что deployment-
    /// pick), поэтому событие несёт лишь реликвию и фазу — UI не тащит камеру/пикинг/конверсию координат.
    /// </summary>
    public readonly struct RelicDragEvent
    {
        public readonly RelicData      Relic;
        public readonly RelicDragPhase Phase;

        public RelicDragEvent(RelicData relic, RelicDragPhase phase)
        {
            Relic = relic;
            Phase = phase;
        }
    }
}
