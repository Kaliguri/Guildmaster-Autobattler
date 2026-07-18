using System.Collections.Generic;
using Guildmaster.Core.Presentation;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using Guildmaster.Presentation;
using UnityEngine;

namespace Guildmaster.Game
{
    /// <summary>
    /// Реализация <see cref="IRosterStage"/>: «живая арена расстановки» вне боя (Ф3b.1). Строит на далёком
    /// off-screen стейдже серую мок-площадку с зонами и отряд игрока (из durable гильдии, позиции —
    /// <c>SavedPosition</c>), ставит на него ортокамеру и рендерит её в <see cref="RenderTexture"/> для показа
    /// «окном в мир» в левой зоне инвентаря. Тот же приём, что у <c>RelicCardVisualRig</c>: стейдж далеко от
    /// игровой сцены + камера рождается disabled и включается ТОЛЬКО после назначения targetTexture (иначе
    /// enabled-камера без RT рисует на Display 0).
    /// <para>Ф3b.1 — статика (окно + отряд). Управление камерой, драг релик→эквип и durable позиции — .2–.4.</para>
    /// </summary>
    public sealed class RosterStageController : IRosterStage
    {
        private const int   PlayerTeam   = 0;
        private const float StageOriginY = -20000f; // далеко от игровой сцены/рига (риг на +5000, арена ~0)

        // Серая мок-арена «хоть как-то» (Ф3b.1): площадка + две зоны. Реальный арт/зоны Макс соберёт позже.
        private static readonly Vector2 ArenaSize    = new Vector2(20f, 12f);
        private static readonly Color   FloorColor   = new Color(0.16f, 0.16f, 0.18f, 1f);
        private static readonly Color   BgColor      = new Color(0.10f, 0.10f, 0.12f, 1f);
        private static readonly Color   PlayerZone   = new Color(0.30f, 0.55f, 0.95f, 0.18f);
        private static readonly Color   EnemyZone    = new Color(0.95f, 0.35f, 0.30f, 0.15f);

        private readonly IContentDatabase _content;
        private readonly RunStateService  _runStates;

        private Transform     _root;
        private Camera        _camera;
        private RenderTexture _rt;
        private readonly List<GameObject> _units = new List<GameObject>();
        private Sprite _quad; // общий 1×1 белый спрайт (Texture2D.whiteTexture) — тинтуется/масштабируется под примитивы

        // Управление камерой (Ф3b.2): орто-размер + центр в мировых координатах, кламп зоной арены.
        private const float ZoomStep = 1.2f;
        private const float MinSize  = 2.5f;
        private float   _aspect = 1f;
        private float   _size;
        private float   _maxSize;
        private Vector2 _center;
        private Vector2 _zoneHalf;

        public RosterStageController(IContentDatabase content, RunStateService runStates)
        {
            _content   = content;
            _runStates = runStates;
        }

        public bool IsOpen => _root != null;
        public RenderTexture Texture => _rt;

        public RenderTexture Open(int width, int height)
        {
            if (IsOpen) return _rt;

            width  = Mathf.Max(64, width);
            height = Mathf.Max(64, height);

            var rootGo = new GameObject("RosterStage") { hideFlags = HideFlags.HideAndDontSave };
            rootGo.transform.position = new Vector3(0f, StageOriginY, 0f);
            _root = rootGo.transform;

            _quad = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);

            BuildArena();
            BuildRoster();
            BuildCamera(width, height);

            return _rt;
        }

        public void Close()
        {
            // Порядок как в риге: гасим камеру ПЕРЕД снятием RT, чтобы ни кадра не было enabled-камеры без RT.
            if (_camera != null)
            {
                _camera.enabled = false;
                _camera.targetTexture = null;
                DestroyObject(_camera.gameObject);
                _camera = null;
            }

            for (int i = 0; i < _units.Count; i++)
                if (_units[i] != null) DestroyObject(_units[i]);
            _units.Clear();

            if (_rt != null) { _rt.Release(); DestroyObject(_rt); _rt = null; }
            if (_root != null) { DestroyObject(_root.gameObject); _root = null; }
            _quad = null;
        }

        // ── Мок-арена: серая площадка + две зоны расстановки (плоские квады из 1×1 спрайта). ──
        private void BuildArena()
        {
            Vector2 origin = _root.position;
            Quad("Floor", origin, ArenaSize, FloorColor, sortingOrder: -20);

            // Зоны-половины (демо): игрок слева, враг справа. Реальные зоны придут из ArenaLayoutData позже.
            float zoneW = ArenaSize.x * 0.42f;
            float zoneH = ArenaSize.y * 0.9f;
            Quad("Zone.Player", origin + new Vector2(-ArenaSize.x * 0.24f, 0f), new Vector2(zoneW, zoneH), PlayerZone, -10);
            Quad("Zone.Enemy",  origin + new Vector2( ArenaSize.x * 0.24f, 0f), new Vector2(zoneW, zoneH), EnemyZone,  -10);
        }

        private void Quad(string name, Vector2 center, Vector2 size, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(center.x - _root.position.x, center.y - _root.position.y, 0f);
            go.transform.localScale    = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = _quad;
            sr.color        = color;
            sr.sortingOrder = sortingOrder;
        }

        // ── Отряд: durable-гильдия (позиции из SavedPosition), либо демо-отряд на dev-стенде без забега. ──
        private void BuildRoster()
        {
            RunState run = _runStates != null ? _runStates.Current : null;
            PlayerSlot[] slots = GuildRoster.Resolve(run, _content);
            if (slots.Length == 0) slots = DemoRoster();

            Vector2 origin = _root.position;
            for (int i = 0; i < slots.Length; i++)
            {
                RelicData relic = slots[i].Relic; // RelicData : UnitData — несёт ViewPrefab
                if (relic == null || relic.ViewPrefab == null) continue;

                // Нет сохранённой позиции (стартовая гильдия) → раскладываем сеткой в зоне игрока.
                Vector2 pos = slots[i].Position;
                if (pos.sqrMagnitude < 0.01f) pos = GridPosition(i);

                GameObject go = Object.Instantiate(relic.ViewPrefab, origin + pos, Quaternion.identity, _root);
                var view = go.GetComponentInChildren<UnitView>(true);
                if (view != null) view.ApplyPreview(relic, PlayerTeam);
                _units.Add(go);
            }
        }

        // Сетка расстановки в левой (игроцкой) половине: до 4 в столбец, столбцы влево.
        private static Vector2 GridPosition(int index)
        {
            const int perColumn = 4;
            int col = index / perColumn;
            int row = index % perColumn;
            float x = -ArenaSize.x * 0.22f - col * 1.8f;
            float y = (row - (perColumn - 1) * 0.5f) * 2.2f;
            return new Vector2(x, y);
        }

        // Демо-отряд для dev-стенда без забега: первые реликвии с ViewPrefab (кроме дамми-кита relic.base).
        private PlayerSlot[] DemoRoster()
        {
            var list = new List<PlayerSlot>();
            IReadOnlyList<RelicData> all = _content != null ? _content.All<RelicData>() : null;
            for (int i = 0; all != null && i < all.Count && list.Count < 4; i++)
            {
                RelicData r = all[i];
                if (r == null || r.Id == "relic.base" || r.ViewPrefab == null) continue;
                list.Add(new PlayerSlot(r, null, Vector2.zero, null));
            }
            return list.ToArray();
        }

        // ── Ортокамера на стейдж → RT (прозрачно-серый фон). Инвариант рига: enabled только после targetTexture. ──
        private void BuildCamera(int width, int height)
        {
            _rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "RosterStageRT" };
            _rt.Create();

            var camGo = new GameObject("RosterStageCamera");
            camGo.transform.SetParent(_root, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled         = false;                      // не рендерить, пока нет targetTexture
            cam.depth           = -100f;
            cam.orthographic    = true;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = BgColor;
            cam.nearClipPlane   = 0.01f;
            cam.farClipPlane    = 100f;
            cam.targetTexture   = _rt;
            _camera = cam;

            // Габариты/кламп камеры (Ф3b.2). Зона пана = арена с небольшим полем; полный отзум = вся арена.
            Vector2 origin = _root.position;
            _aspect   = (float)width / height;
            _zoneHalf = new Vector2(ArenaSize.x * 0.5f * 1.05f, ArenaSize.y * 0.5f * 1.05f);
            _maxSize  = Mathf.Max(ArenaSize.y * 0.5f, ArenaSize.x * 0.5f / Mathf.Max(0.01f, _aspect)) * 1.06f;

            // Старт: заполняем по высоте, наводимся на зону игрока (там отряд). Пан/зум откроют остальное.
            _size   = Mathf.Min(_maxSize, ArenaSize.y * 0.5f * 1.06f);
            _center = new Vector2(origin.x - ArenaSize.x * 0.24f, origin.y);

            ApplyCamera();
            cam.enabled = true;                              // RT назначена — теперь можно рендерить
        }

        public void PanByFraction(Vector2 fractionDelta)
        {
            if (_camera == null) return;
            // Схват мира: тянем вправо → мир вправо → камера влево. UITK y вниз (+) → мир вверх (+).
            _center.x -= fractionDelta.x * (2f * _size * _aspect);
            _center.y += fractionDelta.y * (2f * _size);
            ApplyCamera();
        }

        public void ZoomBy(float steps)
        {
            if (_camera == null) return;
            _size = Mathf.Clamp(_size - steps * ZoomStep, MinSize, _maxSize);
            ApplyCamera();
        }

        // Применить зум+центр к камере с клампом: видимая область не выходит за зону арены; когда область
        // шире/выше зоны (полный отзум) — центрируем по соответствующей оси.
        private void ApplyCamera()
        {
            _camera.orthographicSize = _size;
            Vector2 zc = new Vector2(_root.position.x, _root.position.y);
            float slackX = _zoneHalf.x - _size * _aspect;
            float slackY = _zoneHalf.y - _size;
            float x = slackX <= 0f ? zc.x : Mathf.Clamp(_center.x, zc.x - slackX, zc.x + slackX);
            float y = slackY <= 0f ? zc.y : Mathf.Clamp(_center.y, zc.y - slackY, zc.y + slackY);
            _camera.transform.position = new Vector3(x, y, -10f);
        }

        private static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
