using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Shapes;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Dev-оверлей зон удара (вики «13» шаг 4): при линейной авто-атаке и круговой активке рисует
    /// форму через Shapes-компоненты на короткую вспышку (~0.25с) с затуханием — инструмент оценки
    /// работы, НЕ финальный VFX. Подписан на <see cref="CombatSimulation.OnAreaHit"/>; тогглится
    /// через <see cref="IsEnabled"/>. Компонентные ShapeRenderer'ы рисуются как обычные меши
    /// (без URP render feature) — видны и в Scene-, и в Game-view.
    /// </summary>
    public sealed class CombatAreaFlash : MonoBehaviour
    {
        [SerializeField] private bool  _enabled       = true;
        [SerializeField] private float _flashDuration = 0.25f;
        [SerializeField] private Color _teamAColor    = new Color(0.2f, 0.6f, 1f, 0.5f);
        [SerializeField] private Color _teamBColor    = new Color(1f, 0.35f, 0.2f, 0.5f);

        // Зоны рисуются по ПОКАЗАННОМУ кадру: по симу вспышка полыхала бы за окно опережения до удара.
        // Приходит и уходит вместе с боем: объект живёт в персист-сцене и переживает бои, а диспетчер —
        // нет. Вне боя оверлею просто нечего показывать, зон удара не бывает.
        private Combat.Tape.BattleTapeDispatcher _dispatcher;
        private readonly List<Flash>  _active     = new List<Flash>();
        private readonly Stack<Flash> _circlePool = new Stack<Flash>();
        private readonly Stack<Flash> _linePool   = new Stack<Flash>();

        /// <summary>Бой начался: подписаться на его показанные зоны удара.</summary>
        public void BindBattle(Combat.Tape.BattleTapeDispatcher dispatcher)
        {
            UnbindBattle();
            _dispatcher = dispatcher;
            if (_dispatcher != null) _dispatcher.AreaHit += OnAreaHit;
        }

        /// <summary>Бой ушёл: отписаться и погасить всё, что ещё догорало.</summary>
        public void UnbindBattle()
        {
            if (_dispatcher != null) _dispatcher.AreaHit -= OnAreaHit;
            _dispatcher = null;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Hide();
                Return(_active[i]);
            }
            _active.Clear();
        }

        private void OnDestroy() => UnbindBattle();

        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        private void OnAreaHit(AreaHit hit)
        {
            if (!_enabled) return;

            Flash flash = Rent(hit.Shape);
            flash.Configure(in hit, hit.Team == 0 ? _teamAColor : _teamBColor);
            flash.StartTime = Time.time;
            _active.Add(flash);
        }

        private void Update()
        {
            float dur = Mathf.Max(_flashDuration, 1e-3f);
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Flash flash = _active[i];
                float t = (Time.time - flash.StartTime) / dur;
                if (t >= 1f)
                {
                    flash.Hide();
                    Return(flash);
                    _active.RemoveAt(i);
                    continue;
                }
                flash.SetFade(1f - t); // линейное затухание
            }
        }

        private Flash Rent(AreaShape shape)
        {
            Stack<Flash> pool = shape == AreaShape.Circle ? _circlePool : _linePool;
            return pool.Count > 0 ? pool.Pop() : new Flash(transform, shape);
        }

        private void Return(Flash flash)
        {
            (flash.Shape == AreaShape.Circle ? _circlePool : _linePool).Push(flash);
        }

        /// <summary>
        /// Один пулируемый объект-вспышка. Shapes допускает лишь ОДИН ShapeRenderer на GameObject,
        /// поэтому форма фиксируется при создании (Disc ИЛИ Line) и пулы раздельные по форме.
        /// </summary>
        // Dev-оверлеи рисуем на выделенном верхнем слое сортировки, иначе спрайт арены (слой выше Default)
        // перекрывает Shapes — они по умолчанию на Default (самый нижний). Резолвим по имени лениво: NameToID
        // нельзя звать из инициализатора поля MonoBehaviour, поэтому кэшируем при первом обращении (рантайм).
        private static bool _layerResolved;
        private static int  _overlayLayerId;
        private static int OverlayLayerId
        {
            get
            {
                if (!_layerResolved) { _overlayLayerId = SortingLayer.NameToID("DevOverlay"); _layerResolved = true; }
                return _overlayLayerId;
            }
        }

        private sealed class Flash
        {
            public readonly AreaShape Shape;
            public float StartTime;

            private readonly GameObject _go;
            private readonly Disc       _disc;
            private readonly Line       _line;
            private Color               _baseColor;

            public Flash(Transform parent, AreaShape shape)
            {
                Shape = shape;
                _go = new GameObject("AreaFlash_" + shape);
                _go.transform.SetParent(parent, worldPositionStays: false);

                if (shape == AreaShape.Circle)
                {
                    _disc = _go.AddComponent<Disc>();
                    _disc.Geometry = DiscGeometry.Flat2D;
                    _disc.Type     = DiscType.Disc;
                }
                else
                {
                    _line = _go.AddComponent<Line>();
                    _line.Geometry       = LineGeometry.Flat2D;
                    _line.EndCaps        = LineEndCap.Square;
                    _line.ThicknessSpace = ThicknessSpace.Meters;
                }

                // Поверх арены/юнитов: выделенный верхний слой сортировки (см. OverlayLayerId).
                ShapeRenderer r = shape == AreaShape.Circle ? (ShapeRenderer)_disc : _line;
                r.SortingLayerID = OverlayLayerId;

                Hide();
            }

            public void Configure(in AreaHit hit, Color color)
            {
                _baseColor = color;
                _go.SetActive(true);

                if (Shape == AreaShape.Circle)
                {
                    _go.transform.position = new Vector3(hit.Origin.x, hit.Origin.y, -1f); // поверх спрайтов
                    _disc.Radius = hit.Radius;
                    _disc.Color  = color;
                }
                else
                {
                    _go.transform.position = new Vector3(0f, 0f, -1f);
                    Vector3 a = new Vector3(hit.Origin.x, hit.Origin.y, 0f);
                    Vector3 b = a + new Vector3(hit.Direction.x, hit.Direction.y, 0f) * hit.Length;
                    _line.Start     = a;
                    _line.End       = b;
                    _line.Thickness = hit.Width;
                    _line.Color     = color;
                }
            }

            public void SetFade(float k)
            {
                Color c = _baseColor;
                c.a *= k;
                if (Shape == AreaShape.Circle) _disc.Color = c;
                else                           _line.Color = c;
            }

            public void Hide() => _go.SetActive(false);
        }
    }
}
