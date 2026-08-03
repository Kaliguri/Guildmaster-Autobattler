using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Шов выбора следующего узла на карте (план [[act-map-run-loop]] §3.2). Развязывает петлю акта
    /// (<c>ActRunner</c>) от способа выбора: A2 — авто (headless-прогон), A3 — клик по <c>MapScreen</c>.
    /// Сетевой-ready: в коопе выбор придёт от хоста через интенты (Фаза 6) — тем же интерфейсом.
    /// </summary>
    public interface IMapNodeChooser
    {
        /// <summary>Выбрать один узел из доступных. Возвращает выбранный (или null при отмене/ошибке).
        /// <paramref name="ct"/> прерывает ожидание выбора при выходе из забега (QA #18).
        /// <paramref name="openMap"/> — показать карту сразу (вход в акт) или ждать, пока игрок позовёт её сам
        /// из передышки между узлами.</summary>
        UniTask<MapNode> ChooseAsync(MapState map, IReadOnlyList<MapNode> available, CancellationToken ct = default,
                                     bool openMap = true);
    }

    /// <summary>
    /// A2-реализация: авто-выбор первого доступного узла (без UI) — забег проходится headless по «верхнему»
    /// маршруту. На A3 подменяется реализацией с кликом по экрану карты (регистрация в DI).
    /// </summary>
    public sealed class AutoFirstNodeChooser : IMapNodeChooser
    {
        public UniTask<MapNode> ChooseAsync(MapState map, IReadOnlyList<MapNode> available, CancellationToken ct = default,
                                            bool openMap = true)
        {
            MapNode pick = available != null && available.Count > 0 ? available[0] : null;
            if (pick != null) Debug.Log($"[AutoFirstNodeChooser] - авто-выбор узла '{pick.Id}' ({pick.Type})");
            return UniTask.FromResult(pick);
        }
    }
}
