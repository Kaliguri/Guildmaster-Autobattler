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
    /// <para>Отдельно от <see cref="OpenMapRequest"/> намеренно: тот открывает UITK-экран карты и остаётся
    /// нетронутым, пока world-карта не принята в play-mode. Переключение путей — одна строка в DI.</para>
    /// </summary>
    public readonly struct WorldMapSpaceChangedEvent
    {
        /// <summary>true — карта показана в мире (нужен Sheet поверх), false — скрыта (снять Sheet).</summary>
        public readonly bool Active;

        public WorldMapSpaceChangedEvent(bool active) => Active = active;
    }

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
