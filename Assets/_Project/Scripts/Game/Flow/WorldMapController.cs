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
    public sealed class WorldMapController : IActMapPresence, IStartable, IDisposable
    {
        private readonly IWorldMapView _view;
        // Владелец показа карты переживает сеансы, поэтому забег он только ЧИТАЕТ — через роутер, а не
        // прямой ссылкой на держателя из скоупа сессии.
        private readonly IRunStateView _runStates;
        private readonly ISubscriber<SetWorldMapRequest> _setSub;
        private readonly IPublisher<WorldMapSpaceChangedEvent> _spacePub;

        private IDisposable _setSubscription;

        // Намерение показать карту и факт, что она нарисована, — РАЗНЫЕ вещи, и разошлись они не от
        // красоты. У гостя состояние забега и объявление «карта открыта» едут разными каналами, порядок
        // между которыми не гарантирован: просьба показать вполне может обогнать данные. Пока флаг был
        // один, такой обгон гасил намерение навсегда — карта не появлялась уже никогда.
        private bool _visible;   // хотим показывать
        private bool _drawn;     // показываем на самом деле
        // Активное ожидание выбора: id узлов, в которые сейчас реально можно войти, и куда отдать результат.
        // null = карта в режиме просмотра (горящих узлов нет).
        private HashSet<string> _choosable;
        private Action<string> _onChosen;

        public WorldMapController(IWorldMapView view,
                                  IRunStateView runStates,
                                  ISubscriber<SetWorldMapRequest> setSub,
                                  IPublisher<WorldMapSpaceChangedEvent> spacePub)
        {
            _view      = view;
            _runStates = runStates;
            _setSub    = setSub;
            _spacePub  = spacePub;
        }

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

            if (!visible) { HideNow(); return; }

            // Карты может ещё не быть (меню, забег без карты, у гостя — снимок не доехал). Намерение при
            // этом остаётся: покажем, как только появится, — см. Refresh.
            ShowIfReady();
        }

        /// <summary>Показана ли карта прямо сейчас. Хост объявляет это гостю, чтобы тот шёл следом.</summary>
        public bool IsShown => _drawn;

        /// <summary>
        /// Состояние забега изменилось — перерисовать карту, если её просили показать.
        /// </summary>
        /// <remarks>
        /// Нужен тем, у кого забег МЕНЯЕТСЯ извне: гость получает его снимком, и в момент просьбы
        /// показать карту данных может ещё не быть. У владельца забег меняется его же руками, и там
        /// перерисовку заказывает сама петля.
        /// </remarks>
        public void Refresh()
        {
            if (!_visible) return;
            ShowIfReady();
        }

        private void ShowIfReady()
        {
            if (!Redraw()) return;
            if (_drawn) return;

            _drawn = true;
            _spacePub?.Publish(new WorldMapSpaceChangedEvent(true));
        }

        private void HideNow()
        {
            if (!_drawn) return;

            _drawn = false;
            _view.Hide(); // вернёт камеру в тот вид, из которого пришли
            _spacePub?.Publish(new WorldMapSpaceChangedEvent(false));
        }

        /// <summary>
        /// Петля забега начала ждать выбор: перечисленные узлы становятся доступными.
        /// </summary>
        /// <param name="show">
        /// Открыть карту сразу. true — на входе в акт (игрок должен увидеть, куда идёт). Дальше по ходу забега
        /// false: узел пройден, игрок остаётся в живом мире и открывает карту сам — табом или кнопкой передышки
        /// (реш. Макса 2026-07-26). Узлы горят в обоих случаях: ждём ВЫБОР, а не показ.
        /// </param>
        public void BeginChoose(IReadOnlyList<MapNode> available, Action<string> onChosen, bool show = true)
        {
            _choosable = new HashSet<string>();
            foreach (MapNode node in available) _choosable.Add(node.Id);
            _onChosen = onChosen;

            if (!show)
            {
                if (_drawn) Redraw(); // карта уже открыта (игрок сам её позвал) — просто зажечь доступные узлы
                return;
            }

            _visible = true;
            ShowIfReady();
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
            HideNow();
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
