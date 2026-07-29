using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Один боец в заказанном составе площадки: кто и где стоит.</summary>
    public readonly struct ProvingGroundsSpawn
    {
        public readonly UnitData Unit;
        public readonly Vector2  Position;

        public ProvingGroundsSpawn(UnitData unit, Vector2 position)
        {
            Unit     = unit;
            Position = position;
        }
    }

    /// <summary>
    /// Заказ состава Ристалища: кто встаёт на площадку вместо расклада из <see cref="ProvingGroundsConfig"/>.
    /// Действует на ближайший заход и снимается вместе с выходом с площадки.
    /// <para>
    /// Существует ради того, чтобы у состава арены остался ОДИН владелец. Дев-команды раньше ставили свой
    /// бой напрямую в симуляцию, мимо расстановки, — и на площадке это не работало: расстановка держит свой
    /// состав в слотах и возвращает его первой же пересборкой превью, а сброс боя из команды снимал паузу,
    /// которой владеет расстановка (бой начинался сам, без «Начать»). Заказ отдаёт состав тому, кто им и так
    /// распоряжается: площадка ставит бойцов своим штатным путём, а команда только говорит, каких.
    /// </para>
    /// </summary>
    public readonly struct ProvingGroundsSetupRequest
    {
        /// <summary>Сторона игрока (команда 0). Пусто — площадка берёт свой расклад из ассета.</summary>
        public readonly IReadOnlyList<ProvingGroundsSpawn> Squad;

        /// <summary>Сторона противника (команда 1).</summary>
        public readonly IReadOnlyList<ProvingGroundsSpawn> Opponents;

        /// <summary>Чем заказан состав — уходит в лог площадки, чтобы было видно, кто его переопределил.</summary>
        public readonly string Source;

        public ProvingGroundsSetupRequest(IReadOnlyList<ProvingGroundsSpawn> squad,
            IReadOnlyList<ProvingGroundsSpawn> opponents, string source)
        {
            Squad     = squad;
            Opponents = opponents;
            Source    = source;
        }

        /// <summary>Есть ли что ставить: пустой заказ вернул бы площадку к раскладу из ассета.</summary>
        public bool HasContent => (Squad != null && Squad.Count > 0) || (Opponents != null && Opponents.Count > 0);
    }
}
