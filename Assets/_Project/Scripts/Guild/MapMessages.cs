using System;
using System.Collections.Generic;
using System.Threading;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать экран карты акта и выбрать следующий узел (план [[act-map-run-loop]] §3.2, A3). Публикует
    /// петля акта через <c>MapScreenNodeChooser</c>, слушает UI (<c>UiRootBootstrap</c> → <c>MenuRouter</c>).
    /// Пейлоад — только состояние карты + id доступных узлов + колбэк выбора: UI не зависит от UniTask, а Guild —
    /// от UI. Тем же приёмом, что <c>OpenRewardRequest</c>/<c>OpenTextEventRequest</c>.
    /// </summary>
    public readonly struct OpenMapRequest
    {
        /// <summary>Состояние карты (граф узлов + текущая позиция) — только для отрисовки, не мутируется UI.</summary>
        public readonly MapState Map;

        /// <summary>Id узлов, доступных для входа с текущей позиции (их подсвечиваем и делаем кликабельными).</summary>
        public readonly IReadOnlyList<string> AvailableNodeIds;

        /// <summary>Колбэк выбора (ровно один вызов): id выбранного узла, либо null = экран закрыт без выбора.</summary>
        public readonly Action<string> OnChosen;

        /// <summary>Токен отмены забега (QA #37): отмена закрывает экран карты через навигатор — не через
        /// веник CloseAll (тот резолвил карту в null → петля трактовала как Aborted → вылет).</summary>
        public readonly CancellationToken Cancellation;

        public OpenMapRequest(MapState map, IReadOnlyList<string> availableNodeIds, Action<string> onChosen,
                              CancellationToken cancellation = default)
        {
            Map              = map;
            AvailableNodeIds = availableNodeIds;
            OnChosen         = onChosen;
            Cancellation     = cancellation;
        }
    }
}
