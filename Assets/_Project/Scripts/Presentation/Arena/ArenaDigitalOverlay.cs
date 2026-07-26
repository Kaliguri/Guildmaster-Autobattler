using System.Collections.Generic;
using Guildmaster.Core.Arena;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Guildmaster.Presentation.Arena
{
    /// <summary>
    /// Цифровой слой над ареной: каркас по содержимому, тонировка и вспышка в момент подмены тайла
    /// (журнал `docs/arena-swap-progress.md`, Ф3). Рисует один квад в мировых координатах поверх пола —
    /// чужой шейдер тайлов не трогаем, а накрываются разом пол, стены и декор.
    /// <para>Вся раскладка по времени приходит из <see cref="ArenaSwapSchedule"/> и печётся в карту клеток
    /// (тексель на клетку). Шейдер только читает готовое: пересчитай он фазы сам — каркас поехал бы мимо
    /// клеток, которые меняются, потому что HLSL и C# считают не до последнего разряда одинаково.</para>
    /// </summary>
    [RequireComponent(typeof(ArenaSkinSwapper))]
    public sealed class ArenaDigitalOverlay : MonoBehaviour
    {
        private static readonly int CellMapId    = Shader.PropertyToID("_CellMap");
        private static readonly int MapRectId    = Shader.PropertyToID("_MapRect");
        private static readonly int CellsId      = Shader.PropertyToID("_Cells");
        private static readonly int CellSizeId   = Shader.PropertyToID("_CellSize");
        private static readonly int ProgressId   = Shader.PropertyToID("_Progress");
        private static readonly int CalmId       = Shader.PropertyToID("_Calm");

        [Tooltip("Материал на шейдере Guildmaster/Arena/Digital. Рисуем КОПИЕЙ — ассет не грязним.")]
        [SerializeField] private Material _digitalMaterial;

        [Tooltip("Слой считается стеной, если его имя содержит это. Стены дают узнаваемый контур арены.")]
        [SerializeField] private string _wallLayerMarker = "Wall";

        [Header("Порядок отрисовки")]
        [SerializeField] private string _sortingLayer = "Default";
        [Tooltip("Выше пола и стен, но ниже юнитов: цифра ложится на мир, а не поверх бойцов.")]
        [SerializeField] private int _sortingOrder = 5;

        [Header("Спокойный режим")]
        [Tooltip("Держать цифру постоянно (тест-зона как модель места): без вспышек, с медленным дыханием.")]
        [SerializeField] private bool _calm;

        private ArenaSkinSwapper _swapper;
        private ArenaSkinSource  _live;
        private SpriteRenderer   _quad;
        private Material         _material;
        private Texture2D        _cellMap;

        private BoundsInt _bounds;
        private float     _cellSize = 1f;
        private Vector2   _worldOrigin;   // мировая точка клетки _bounds.min — сетка живёт в мире, не в клетках

        /// <summary>Держать арену в цифре постоянно — облик тест-зоны как «модели места», а не серого пола.</summary>
        public bool Calm
        {
            get => _calm;
            set
            {
                _calm = value;
                if (_material != null) _material.SetFloat(CalmId, _calm ? 1f : 0f);
                if (_quad != null) _quad.enabled = _calm || (_swapper != null && _swapper.Busy);
            }
        }

        private void Awake()
        {
            _swapper = GetComponent<ArenaSkinSwapper>();

            foreach (ArenaSkinSource src in GetComponentsInChildren<ArenaSkinSource>(true))
                if (src.IsLive) { _live = src; break; }

            if (_live == null || _digitalMaterial == null)
            {
                Debug.LogWarning("[ArenaDigitalOverlay] - нет живого облика или материала → цифровой слой выключен.");
                enabled = false;
                return;
            }

            MeasureArena();
            BuildQuad();
            Calm = _calm;
        }

        private void OnEnable()
        {
            if (_swapper != null) _swapper.SwapStarted += OnSwapStarted;
        }

        private void OnDisable()
        {
            if (_swapper != null) _swapper.SwapStarted -= OnSwapStarted;
        }

        private void OnDestroy()
        {
            // Копия материала и печёная карта — наши, чужого в проекте после себя не оставляем
            // (на грязнении общего ассета уже обжигались со шторкой перехода, QA #53d).
            if (_material != null) Destroy(_material);
            if (_cellMap != null) Destroy(_cellMap);
        }

        private void LateUpdate()
        {
            if (_material == null) return;

            bool busy = _swapper != null && _swapper.Busy;
            if (_quad != null) _quad.enabled = busy || _calm;
            if (!busy) return;

            _material.SetFloat(ProgressId, _swapper.Progress);
        }

        private void OnSwapStarted(string from, string to) => Bake(from, to);

        /// <summary>Границы арены в клетках, размер клетки и МИРОВАЯ точка отсчёта — по слоям живого облика.</summary>
        private void MeasureArena()
        {
            bool first = true;
            Grid grid = null;

            foreach (Tilemap map in _live.Layers)
            {
                BoundsInt b = map.cellBounds;
                if (b.size.x <= 0 || b.size.y <= 0) continue;

                if (first)
                {
                    _bounds   = b;
                    grid      = map.layoutGrid;
                    _cellSize = Mathf.Max(0.01f, grid != null ? grid.cellSize.x : 1f);
                    first     = false;
                }
                else
                {
                    Vector3Int min = Vector3Int.Min(_bounds.min, b.min);
                    Vector3Int max = Vector3Int.Max(_bounds.max, b.max);
                    _bounds = new BoundsInt(min, max - min);
                }
            }

            if (first)
            {
                Debug.LogWarning("[ArenaDigitalOverlay] - слои живого облика пусты → цифровому слою нечего накрывать.");
                return;
            }

            // Клеточные координаты сами по себе НЕ мировые: корень тайлмап стоит со своим смещением.
            // Возьмём его через Grid, иначе каркас ляжет рядом с ареной, а не на неё.
            _worldOrigin = grid != null
                ? (Vector2)grid.CellToWorld(_bounds.min)
                : new Vector2(_bounds.min.x * _cellSize, _bounds.min.y * _cellSize);
        }

        private void BuildQuad()
        {
            var go = new GameObject("Arena Digital Overlay");
            go.transform.SetParent(transform, false);

            _quad = go.AddComponent<SpriteRenderer>();
            _quad.sprite = OnePixelSprite();
            _quad.sortingLayerName = _sortingLayer;
            _quad.sortingOrder     = _sortingOrder;

            _material = new Material(_digitalMaterial) { name = _digitalMaterial.name + " (runtime)" };
            _quad.sharedMaterial = _material;

            float w = _bounds.size.x * _cellSize;
            float h = _bounds.size.y * _cellSize;
            Vector2 origin = _worldOrigin;

            go.transform.position   = new Vector3(origin.x + w * 0.5f, origin.y + h * 0.5f, 0f);
            go.transform.localScale = new Vector3(w, h, 1f);

            _material.SetVector(MapRectId, new Vector4(origin.x, origin.y, w, h));
            _material.SetVector(CellsId,   new Vector4(_bounds.size.x, _bounds.size.y, 0f, 0f));
            _material.SetFloat(CellSizeId, _cellSize);
            _material.SetFloat(ProgressId, 0f);

            _quad.enabled = false;
        }

        /// <summary>
        /// Печёт карту клеток: вид клетки в обоих обликах и три момента её жизни в переходе.
        /// <list type="bullet">
        /// <item>R — вид, упакованный парой: 0.25 за стену в исходном облике, 0.5 за стену в целевом.
        /// Так каркас в первом акте очерчивает арену, ИЗ которой уходим, а дальше — ту, в которую пришли.</item>
        /// <item>G, B, A — когда клетка уходит в каркас, переворачивается и возвращается в реальность.</item>
        /// </list>
        /// </summary>
        private void Bake(string from, string to)
        {
            if (_material == null) return;

            int cols = Mathf.Max(1, _bounds.size.x);
            int rows = Mathf.Max(1, _bounds.size.y);

            if (_cellMap == null || _cellMap.width != cols || _cellMap.height != rows)
            {
                if (_cellMap != null) Destroy(_cellMap);

                // linear: true — ОБЯЗАТЕЛЬНО. Здесь лежат не цвета, а числа (моменты клеток); sRGB-текстура
                // прогнала бы их через гамму, и в шейдер пришло бы 0.014 вместо 0.12 — вспышки съезжали
                // к началу перехода, а каркас поднимался раньше времени.
                _cellMap = new Texture2D(cols, rows, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name       = "ArenaCellMap",
                    filterMode = FilterMode.Point,   // тексель = клетка; любая фильтрация размажет границы
                    wrapMode   = TextureWrapMode.Clamp,
                };
            }

            var schedule = new ArenaSwapSchedule(_swapper.Shape);
            var allLayers = new List<string>(_swapper.LayerNames);
            var wallLayers = new List<string>();
            foreach (string layer in allLayers)
                if (!string.IsNullOrEmpty(_wallLayerMarker) && layer.Contains(_wallLayerMarker))
                    wallLayers.Add(layer);

            var pixels = new Color32[cols * rows];
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var cell = new Vector3Int(_bounds.min.x + x, _bounds.min.y + y, 0);

                pixels[y * cols + x] = new Color(
                    PackState(CellState(from, cell, allLayers, wallLayers),
                              CellState(to,   cell, allLayers, wallLayers)),
                    schedule.CrossTime(ArenaSwapAct.Digitize, cell.x, cell.y),
                    schedule.CrossTime(ArenaSwapAct.Load,     cell.x, cell.y),
                    schedule.CrossTime(ArenaSwapAct.Restore,  cell.x, cell.y));
            }

            _cellMap.SetPixels32(pixels);
            _cellMap.Apply(updateMipmaps: false);

            _material.SetTexture(CellMapId, _cellMap);
            _material.SetFloat(ProgressId, 0f);
            if (_quad != null) _quad.enabled = true;
        }

        /// <summary>Что в клетке у этого облика: 0 — пусто, 1 — пол, 2 — стена/декор.</summary>
        private int CellState(string skinId, Vector3Int cell, List<string> allLayers, List<string> wallLayers)
        {
            foreach (string layer in wallLayers)
                if (_swapper.HasTile(skinId, layer, cell)) return 2;

            foreach (string layer in allLayers)
                if (_swapper.HasTile(skinId, layer, cell)) return 1;

            return 0;
        }

        // Оба состояния клетки в одном канале: девять сочетаний ложатся в шаг 1/8, а Color32 хранит их
        // без потерь. Пустые клетки шейдер выбрасывает — иначе каркас расчерчивал бы пустоту за ареной.
        private static float PackState(int from, int to) => (from * 3 + to) / 8f;

        // Спрайт-носитель: сам рисунок не нужен, шейдер пишет процедурно. Один пиксель на единицу мира,
        // чтобы масштаб квада читался прямо в мировых единицах.
        private static Sprite OnePixelSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "ArenaOverlayPixel" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
