using System;
using System.Collections.Generic;
using Guildmaster.Guild;
using Guildmaster.Presentation.Map;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// ЕДИНЫЙ владелец показа карты акта в мире. Через него идут оба сценария — и просмотр по табу «Карта»
    /// (в том числе посреди боя), и момент, когда петля забега ждёт выбор узла. Один владелец на ресурс:
    /// два независимых источника показа неизбежно разъезжаются флагами (цена этого урока — РАУНД 5 play-QA).
    /// <para>Ключевое правило подсветки: узлы становятся ДОСТУПНЫМИ только пока петля реально ждёт выбор
    /// (то есть после «Продолжить» на награде). До этого карту можно открыть и разглядывать, но гореть и
    /// вести никуда она не будет — иначе игрок видел бы «доступный» узел, в который ещё нельзя войти.</para>
    /// </summary>
    public sealed class WorldMapController : IStartable, IDisposable
    {
        private readonly IWorldMapView _view;
        private readonly RunStateService _runStates;
        private readonly ISubscriber<SetWorldMapRequest> _setSub;
        private readonly IPublisher<WorldMapSpaceChangedEvent> _spacePub;

        private IDisposable _setSubscription;

        private bool _visible;
        // Активное ожидание выбора: id узлов, в которые сейчас реально можно войти, и куда отдать результат.
        // null = карта в режиме просмотра (горящих узлов нет).
        private HashSet<string> _choosable;
        private Action<string> _onChosen;

        public WorldMapController(IWorldMapView view,
                                  RunStateService runStates,
                                  ISubscriber<SetWorldMapRequest> setSub,
                                  IPublisher<WorldMapSpaceChangedEvent> spacePub)
        {
            _view      = view;
            _runStates = runStates;
            _setSub    = setSub;
            _spacePub  = spacePub;
        }

        /// <summary>Ждёт ли петля выбор узла прямо сейчас (узлы горят и кликаются).</summary>
        public bool IsChoosing => _choosable != null;

        public void Start()
        {
            _view.NodeClicked += OnNodeClicked;
            _setSubscription = _setSub?.Subscribe(req => SetVisible(req.Visible));
        }

        public void Dispose()
        {
            _view.NodeClicked -= OnNodeClicked;
            _setSubscription?.Dispose();
        }

        /// <summary>
        /// Показать/скрыть карту (интент табов). Идемпотентно. Скрытие во время ожидания выбора НЕ отменяет
        /// само ожидание: игрок волен уйти посмотреть бой и вернуться — узел он выберет позже.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;

            if (!visible)
            {
                _view.Hide(); // вернёт камеру в тот вид, из которого пришли
                _spacePub?.Publish(new WorldMapSpaceChangedEvent(false));
                return;
            }

            if (!Redraw()) { _visible = false; return; } // карты нет (меню/нет забега) — показывать нечего
            _spacePub?.Publish(new WorldMapSpaceChangedEvent(true));
        }

        /// <summary>
        /// Петля забега начала ждать выбор: карта показывается, перечисленные узлы становятся доступными.
        /// </summary>
        public void BeginChoose(IReadOnlyList<MapNode> available, Action<string> onChosen)
        {
            _choosable = new HashSet<string>();
            foreach (MapNode node in available) _choosable.Add(node.Id);
            _onChosen = onChosen;

            _visible = true;
            if (!Redraw()) { _visible = false; return; }
            _spacePub?.Publish(new WorldMapSpaceChangedEvent(true));
        }

        /// <summary>
        /// Ожидание выбора закончилось (узел выбран, либо забег отменён). Карта уходит: дальше игрок
        /// оказывается в узле, и держать её на экране незачем.
        /// </summary>
        public void EndChoose()
        {
            _choosable = null;
            _onChosen  = null;
            if (!_visible) return;

            _visible = false;
            _view.Hide();
            _spacePub?.Publish(new WorldMapSpaceChangedEvent(false));
        }

        private void OnNodeClicked(string id)
        {
            if (_choosable == null || !_choosable.Contains(id)) return; // просмотр — клик ничего не решает
            _onChosen?.Invoke(id);
        }

        // Перерисовать карту под текущее состояние. false = рисовать нечего.
        private bool Redraw()
        {
            RunState run = _runStates?.Current;
            MapState map = run?.Map;
            if (map?.Nodes == null || map.Nodes.Length == 0) return false;

            // Сид отдаём слою карты: из него он выводит стабильный разброс узлов. В данных карты разброса
            // нет — раскладка это дело презентации, домен знает только топологию.
            _view.Show(BuildVisuals(map), BuildEdges(map), run.Seed);
            return true;
        }

        // Граф → визуальные данные: только топология и как показать. Координаты считает слой карты.
        private List<MapNodeVisual> BuildVisuals(MapState map)
        {
            var list = new List<MapNodeVisual>(map.Nodes.Length);
            foreach (MapNode node in map.Nodes)
                list.Add(new MapNodeVisual(node.Id, node.Floor, node.Row, StateOf(node, map), node.Type.ToString()));
            return list;
        }

        private static List<(string From, string To)> BuildEdges(MapState map)
        {
            var edges = new List<(string, string)>();
            var seen = new HashSet<string>();
            foreach (MapNode node in map.Nodes)
            {
                if (node.Edges == null) continue;
                foreach (string to in node.Edges)
                {
                    // Ребро рисуем один раз: граф хранит связь с обеих сторон, и без этого каждая линия шла бы дважды.
                    string key = string.CompareOrdinal(node.Id, to) < 0 ? node.Id + "|" + to : to + "|" + node.Id;
                    if (seen.Add(key)) edges.Add((node.Id, to));
                }
            }
            return edges;
        }

        private MapNodeVisualState StateOf(MapNode node, MapState map)
        {
            if (node.Id == map.CurrentNodeId) return MapNodeVisualState.Current;
            // Доступным узел выглядит ТОЛЬКО пока петля ждёт выбор. В режиме просмотра не горит ничего.
            if (_choosable != null && _choosable.Contains(node.Id)) return MapNodeVisualState.Available;
            return node.Cleared ? MapNodeVisualState.Cleared : MapNodeVisualState.Locked;
        }

    }
}
