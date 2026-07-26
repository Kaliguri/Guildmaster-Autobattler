using System;
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

        [Tooltip("Материал на шейдере Guildmaster/Arena/Digital. Рисуем КОПИЕЙ — ассет не грязним.")]
        [SerializeField] private Material _digitalMaterial;

        [Tooltip("Слой считается стеной, если его имя содержит это. Стены дают узнаваемый контур арены.")]
        [SerializeField] private string _wallLayerMarker = "Wall";

        [Header("Порядок отрисовки")]
        [SerializeField] private string _sortingLayer = "Default";
        [Tooltip("Выше пола и стен, но ниже юнитов: цифра ложится на мир, а не поверх бойцов.")]
        [SerializeField] private int _sortingOrder = 5;

        private ArenaSkinSwapper _swapper;
        private ArenaSkinSource  _live;
        private SpriteRenderer   _quad;
        private Material         _material;
        private Texture2D        _cellMap;

        private BoundsInt _bounds;
        private float     _cellSize = 1f;
        private Vector2   _worldOrigin;   // мировая точка клетки _bounds.min — сетка живёт в мире, не в клетках

        /// <summary>Цифра без смены облика: короткий всполох (вход-пик-выход) или полный прогон всех трёх актов.</summary>
        private enum SoloStage { None, Entering, Exiting, Sweeping }

        /// <summary>Ход текущей анимации 0..1 — по нему идут те, кто меняется вместе с ареной (цвет пола).</summary>
        public float CurrentProgress { get; private set; }

        /// <summary>
        /// Идёт ли переход, за которым можно следовать. И свой прогон, и настоящая смена облика: тем, кто
        /// меняется ВМЕСТЕ с ареной (цвет пола, проявление декора), безразлично, кто ведёт время.
        /// </summary>
        public bool Sweeping => _solo == SoloStage.Sweeping || (_swapper != null && _swapper.Busy);

        /// <summary>
        /// Одноразовый режим сборки: каркас с первого кадра чертит ЦЕЛЕВОЕ место, хотя тайлов ещё нет.
        /// Мир тогда не возникает из пустоты, а достраивается в уже стоящий чертёж — пустой экран перед
        /// спавном читался как сбой загрузки, а не как замысел. Снимается сам по окончании перехода.
        /// </summary>
        public bool OutlineFromTarget { get; set; }

        /// <summary>Карта клеток и её привязка к миру — чтобы соседние эффекты шли ПО ТЕМ ЖЕ клеткам.</summary>
        public Texture2D CellMap => _cellMap;
        public Vector4 MapRect { get; private set; }
        public Vector4 Cells { get; private set; }
        public float CellSizeWorld => _cellSize;

        /// <summary>
        /// Полный прогон всех трёх актов без смены облика: уход в цифру, длинная середина, возврат.
        /// Нужен, когда меняются не текстуры, а что-то другое (цвет полигона) — акту подгрузки тоже надо
        /// чем-то себя занять, иначе переход схлопывается до пары мгновений.
        /// </summary>
        public void Sweep()
        {
            if (_material == null) return;

            Bake(_swapper.CurrentSkinId, _swapper.CurrentSkinId);
            _atPeak = null;
            _soloT  = 0f;
            _solo   = SoloStage.Sweeping;
            CurrentProgress = 0f;
            if (_quad != null) _quad.enabled = true;
        }

        private SoloStage _solo;
        private float     _soloT;
        private Action    _atPeak;

        /// <summary>
        /// Мигнуть цифрой поверх арены: первый акт, затем <paramref name="atPeak"/> в самой глубокой точке,
        /// затем третий. Облик не меняется — это чистая подача.
        /// <para>Цифра НЕ залипает: держать её постоянно (как было у тест-зоны) значит вечная анимация на
        /// экране, где игрок стоит минутами. Голубой шейдер — язык ПЕРЕХОДА; состояние показывают цветом
        /// самой арены (<see cref="ArenaDesaturation"/>).</para>
        /// </summary>
        public void Blink(Action atPeak = null)
        {
            if (_material == null || _solo == SoloStage.Entering) return;

            Bake(_swapper.CurrentSkinId, _swapper.CurrentSkinId);
            _atPeak = atPeak;
            _soloT  = 0f;
            _solo   = SoloStage.Entering;
            if (_quad != null) _quad.enabled = true;
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

            // Полный переход (со сменой облика) главнее одиночной цифры: если он пошёл, время ведёт свопер.
            bool busy = _swapper != null && _swapper.Busy;
            if (busy)
            {
                _solo = SoloStage.None;
                if (_quad != null) _quad.enabled = true;
                // Ход публикуем и здесь: спутники перехода (цвет арены, проявление декора) идут по нему же,
                // иначе при настоящей смене облика они получали бы застывшее значение от прошлого прогона.
                CurrentProgress = _swapper.Progress;
                _material.SetFloat(ProgressId, _swapper.Progress);
                return;
            }

            OutlineFromTarget = false;   // переход закончился — режим сборки одноразовый

            if (_solo != SoloStage.None) TickSolo();

            if (_quad != null) _quad.enabled = _solo != SoloStage.None;
        }

        private void TickSolo()
        {
            ArenaSwapShape shape = _swapper.Shape;

            if (_solo == SoloStage.Sweeping)
            {
                _soloT += Time.unscaledDeltaTime / Mathf.Max(0.0001f, shape.DurationSeconds);
                if (_soloT >= 1f)
                {
                    _soloT = 1f;
                    _solo  = SoloStage.None;
                }
                CurrentProgress = _soloT;
                _material.SetFloat(ProgressId, _soloT);
                return;
            }

            if (_solo == SoloStage.Entering)
            {
                float span = Mathf.Max(0.0001f, shape.DigitizeShare * shape.DurationSeconds);
                _soloT += Time.unscaledDeltaTime * shape.DigitizeEnd / span;

                if (_soloT >= shape.DigitizeEnd)
                {
                    _soloT = shape.RestoreStart;   // самая глубокая точка: под цифрой и меняем, что должны
                    _solo  = SoloStage.Exiting;

                    Action peak = _atPeak;
                    _atPeak = null;
                    peak?.Invoke();
                }
                _material.SetFloat(ProgressId, Mathf.Min(_soloT, shape.DigitizeEnd));
                return;
            }

            float outSpan = Mathf.Max(0.0001f, shape.RestoreShare * shape.DurationSeconds);
            _soloT += Time.unscaledDeltaTime * shape.RestoreShare / outSpan;

            if (_soloT < 1f)
            {
                _material.SetFloat(ProgressId, _soloT);
                return;
            }

            _soloT = 1f;
            _solo  = SoloStage.None;
            _material.SetFloat(ProgressId, 1f);
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

            MapRect = new Vector4(origin.x, origin.y, w, h);
            Cells   = new Vector4(_bounds.size.x, _bounds.size.y, 0f, 0f);

            _material.SetVector(MapRectId, MapRect);
            _material.SetVector(CellsId,   Cells);
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

                int stateTo   = CellState(to, cell, allLayers, wallLayers);
                int stateFrom = OutlineFromTarget ? stateTo : CellState(from, cell, allLayers, wallLayers);

                pixels[y * cols + x] = new Color(
                    PackState(stateFrom, stateTo),
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
