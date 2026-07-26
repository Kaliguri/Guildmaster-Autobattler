using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Guildmaster.Presentation.Arena
{
    /// <summary>
    /// Меняет облик арены поклеточной подменой тайлов (журнал `docs/arena-swap-progress.md`, Ф2).
    /// Смена текстуры здесь буквальная — <see cref="Tilemap.SetTile(Vector3Int, TileBase)"/>, — а не рисунок
    /// поверх: чужой шейдер Cainos остаётся нетронутым, приём работает с любым будущим тайлсетом, и «назад»
    /// стоит ровно столько же, сколько «вперёд».
    /// <para>Живой корень один, остальные облики лежат в сцене выключенными и служат хранилищем: снапшот
    /// снимается в память на старте, дальше сцена не трогается вовсе.</para>
    /// <para>Момент переключения каждой клетки считается ЗАРАНЕЕ (<see cref="BuildPlan"/>) и складывается в
    /// отсортированный план. На кадре мы только снимаем с него готовое — иначе пришлось бы каждый кадр
    /// опрашивать расписание по всем клеткам поля.</para>
    /// </summary>
    public sealed class ArenaSkinSwapper : MonoBehaviour, IArenaSwap
    {
        [Header("Облики")]
        [Tooltip("Корень, который рендерится и принимает подмены. Ровно один.")]
        [SerializeField] private ArenaSkinSource _live;

        [Tooltip("Выключенные корни-хранилища. Живой добавляется сам — его стартовый вид тоже облик.")]
        [SerializeField] private List<ArenaSkinSource> _sources = new List<ArenaSkinSource>();

        [Header("Форма перехода")]
        [SerializeField] private float _durationSeconds = 4.5f;
        [SerializeField, Range(0.02f, 0.45f)] private float _digitizeShare = 0.12f;
        [SerializeField, Range(0.02f, 0.45f)] private float _restoreShare = 0.12f;
        [Tooltip("Разброс моментов старта клеток. Ноль превращает переход в общий фейд.")]
        [SerializeField, Range(0f, 0.9f)] private float _cellSpread = 0.62f;
        [SerializeField, Range(0.02f, 1f)] private float _cellDurationMin = 0.10f;
        [SerializeField, Range(0.02f, 1f)] private float _cellDurationMax = 0.34f;
        [Tooltip("Нарастание темпа к финалу акта подгрузки.")]
        [SerializeField, Range(0f, 1f)] private float _tailAcceleration = 0.55f;

        [Header("Скип")]
        [Tooltip("Во сколько раз ускоряется анимация по Space.")]
        [SerializeField, Range(1f, 8f)] private float _rushFactor = 3.5f;

        // skinId → слой → клетка → тайл. Снимается один раз: источники в сцене статичны.
        private readonly Dictionary<string, Dictionary<string, Dictionary<Vector3Int, TileBase>>> _skins =
            new Dictionary<string, Dictionary<string, Dictionary<Vector3Int, TileBase>>>();

        private readonly Dictionary<string, Tilemap> _liveLayers = new Dictionary<string, Tilemap>();

        private readonly struct CellSwitch
        {
            public readonly float T;
            public readonly Tilemap Map;
            public readonly Vector3Int Pos;
            public readonly TileBase Tile;

            public CellSwitch(float t, Tilemap map, Vector3Int pos, TileBase tile)
            {
                T = t; Map = map; Pos = pos; Tile = tile;
            }
        }

        private readonly List<CellSwitch> _plan = new List<CellSwitch>();
        private int _planHead;

        private ArenaSwapSchedule _schedule;
        private Action _onFinished;
        private string _targetSkin;
        private float _t;
        private float _speed = 1f;
        private bool _playing;

        public bool Busy => _playing;
        public string CurrentSkinId { get; private set; }

        /// <summary>
        /// Переход начался: (откуда, куда). Оверлей печёт по этому событию карту клеток — именно в этот
        /// момент известны оба облика, а на следующем кадре исходный уже начнёт стираться.
        /// </summary>
        public event Action<string, string> SwapStarted;

        /// <summary>Имена слоёв живого корня — по ним оверлей отличает пол от стен.</summary>
        public IEnumerable<string> LayerNames => _liveLayers.Keys;

        /// <summary>
        /// Положить облик, собранный на лету, а не снятый с корня в сцене: дев-инструменты сейчас,
        /// процедурные арены потом. Существующий облик с тем же id заменяется.
        /// </summary>
        public void RegisterSkin(string skinId, Dictionary<string, Dictionary<Vector3Int, TileBase>> layers)
        {
            if (string.IsNullOrEmpty(skinId) || layers == null) return;
            _skins[skinId] = layers;
        }

        /// <summary>Снимок облика — чтобы собрать на его основе вариант. Отдаётся как есть, не копией.</summary>
        public bool TryGetSkin(string skinId, out Dictionary<string, Dictionary<Vector3Int, TileBase>> layers) =>
            _skins.TryGetValue(skinId ?? string.Empty, out layers);

        /// <summary>Есть ли в облике тайл в этой клетке этого слоя. Для оверлея: пол там или стена.</summary>
        public bool HasTile(string skinId, string layerName, Vector3Int cell)
        {
            if (skinId == null || !_skins.TryGetValue(skinId, out var layers)) return false;
            return layers.TryGetValue(layerName, out var cells) && cells.ContainsKey(cell);
        }

        /// <summary>Ход перехода 0..1 — читает оверлей, чтобы рисовать каркас теми же фазами.</summary>
        public float Progress => _t;

        /// <summary>Форма текущего перехода — тот же источник правды, что у расписания.</summary>
        public ArenaSwapShape Shape => BuildShape();

        private void Awake()
        {
            if (_live == null)
            {
                Debug.LogWarning("[ArenaSkinSwapper] - живой корень не назначен → смена облика недоступна.");
                return;
            }

            foreach (Tilemap map in _live.Layers)
                _liveLayers[map.gameObject.name] = map;

            Snapshot(_live);                       // стартовый вид живого корня — тоже полноценный облик
            foreach (ArenaSkinSource src in _sources)
            {
                if (src == null || src == _live) continue;
                Snapshot(src);
            }

            CurrentSkinId = _live.SkinId;
        }

        private void Update()
        {
            if (!_playing) return;

            ArenaSwapShape shape = BuildShape();
            _t += Time.unscaledDeltaTime * _speed / shape.DurationSeconds;

            while (_planHead < _plan.Count && _plan[_planHead].T <= _t)
            {
                CellSwitch s = _plan[_planHead++];
                s.Map.SetTile(s.Pos, s.Tile);
            }

            if (_t < 1f) return;

            _t = 1f;
            Finish();
        }

        public bool Play(string skinId, Action onFinished = null)
        {
            if (string.IsNullOrEmpty(skinId) || !_skins.ContainsKey(skinId))
            {
                Debug.LogWarning($"[ArenaSkinSwapper] - облика '{skinId}' нет среди источников → смена пропущена.");
                return false;
            }
            if (_playing || skinId == CurrentSkinId) return false;

            _schedule = new ArenaSwapSchedule(BuildShape());
            BuildPlan(skinId);

            SwapStarted?.Invoke(CurrentSkinId, skinId); // оверлею — пока оба облика ещё известны

            _targetSkin = skinId;
            _onFinished = onFinished;
            _t          = 0f;
            _speed      = 1f;
            _planHead   = 0;
            _playing    = true;
            return true;
        }

        public void Rush()
        {
            if (_playing) _speed = _rushFactor;
        }

        public void ApplyInstant(string skinId)
        {
            if (string.IsNullOrEmpty(skinId) || !_skins.ContainsKey(skinId)) return;

            _playing = false;
            _plan.Clear();
            _planHead = 0;

            // Сначала чистим, потом кладём. Иначе облик, где клетки НЕТ, оставлял бы на её месте старый
            // тайл — и пустой облик (сборка мира с нуля) вообще ничего бы не делал.
            foreach (KeyValuePair<string, Tilemap> live in _liveLayers)
                live.Value.ClearAllTiles();

            foreach (KeyValuePair<string, Dictionary<Vector3Int, TileBase>> layer in _skins[skinId])
            {
                if (!_liveLayers.TryGetValue(layer.Key, out Tilemap map)) continue;
                foreach (KeyValuePair<Vector3Int, TileBase> cell in layer.Value)
                    map.SetTile(cell.Key, cell.Value);
            }

            CurrentSkinId = skinId;
        }

        // Space во время перехода означает «быстрее», а не «пауза»: переход играет вне боя, паузе там
        // нечего останавливать. Развязка — по состоянию: пока не Busy, событие нас не касается.
        private ArenaSwapShape BuildShape() =>
            new ArenaSwapShape(_durationSeconds, _digitizeShare, _restoreShare,
                               _cellSpread, _cellDurationMin, _cellDurationMax, _tailAcceleration);

        private void Snapshot(ArenaSkinSource source)
        {
            var layers = new Dictionary<string, Dictionary<Vector3Int, TileBase>>();

            foreach (Tilemap map in source.Layers)
            {
                var cells = new Dictionary<Vector3Int, TileBase>();
                BoundsInt bounds = map.cellBounds;
                foreach (Vector3Int pos in bounds.allPositionsWithin)
                {
                    TileBase tile = map.GetTile(pos);
                    if (tile != null) cells[pos] = tile;
                }
                layers[map.gameObject.name] = cells;
            }

            _skins[source.SkinId] = layers;
        }

        /// <summary>
        /// Раскладывает смену облика по времени: что и когда поставить. Клетки, у которых тайл совпадает,
        /// в план не попадают — менять нечего, а лишние записи только удлиняют кадр.
        /// </summary>
        private void BuildPlan(string skinId)
        {
            _plan.Clear();
            _planHead = 0;

            Dictionary<string, Dictionary<Vector3Int, TileBase>> target = _skins[skinId];
            _skins.TryGetValue(CurrentSkinId, out Dictionary<string, Dictionary<Vector3Int, TileBase>> current);

            foreach (KeyValuePair<string, Tilemap> live in _liveLayers)
            {
                target.TryGetValue(live.Key, out Dictionary<Vector3Int, TileBase> targetCells);
                Dictionary<Vector3Int, TileBase> currentCells = null;
                current?.TryGetValue(live.Key, out currentCells);

                var visited = new HashSet<Vector3Int>();

                if (targetCells != null)
                    foreach (KeyValuePair<Vector3Int, TileBase> cell in targetCells)
                    {
                        visited.Add(cell.Key);
                        TileBase now = live.Value.GetTile(cell.Key);
                        if (now == cell.Value) continue;
                        _plan.Add(new CellSwitch(SwitchTime(cell.Key), live.Value, cell.Key, cell.Value));
                    }

                // Клетки, которых в новом облике НЕТ, должны погаснуть — иначе от старой арены останутся ошмётки.
                if (currentCells != null)
                    foreach (KeyValuePair<Vector3Int, TileBase> cell in currentCells)
                    {
                        if (visited.Contains(cell.Key)) continue;
                        if (live.Value.GetTile(cell.Key) == null) continue;
                        _plan.Add(new CellSwitch(SwitchTime(cell.Key), live.Value, cell.Key, null));
                    }
            }

            _plan.Sort(static (a, b) => a.T.CompareTo(b.T));
        }

        // Когда клетка перевернётся на новый тайл — спрашиваем у расписания, а не считаем здесь: этот же
        // момент нужен оверлею для вспышки, и две копии формулы однажды разъехались бы.
        private float SwitchTime(Vector3Int cell) =>
            _schedule.CrossTime(ArenaSwapAct.Load, cell.x, cell.y);

        private void Finish()
        {
            while (_planHead < _plan.Count)
            {
                CellSwitch s = _plan[_planHead++];
                s.Map.SetTile(s.Pos, s.Tile);
            }

            _playing      = false;
            CurrentSkinId = _targetSkin;

            Action done = _onFinished;
            _onFinished = null;
            done?.Invoke();
        }
    }
}
