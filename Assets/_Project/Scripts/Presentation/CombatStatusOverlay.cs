using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Shapes;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Dev-подсветка статусов юнитов кольцами Shapes: метка/стан/щит/заморозка/усиление. Каждый кадр
    /// читает состояние симуляции и рисует вокруг живых юнитов концентрические кольца (immediate-mode
    /// с пулом Disc-Ring). Чистая презентация — сим не трогает. Создаётся в рантайме
    /// <see cref="CombatPresenter"/> (никаких правок префабов/сцены); тогглится <see cref="IsEnabled"/>
    /// (команда <c>gm_toggle_status</c>). Не финальный VFX — инструмент наглядности среза.
    /// </summary>
    public sealed class CombatStatusOverlay : MonoBehaviour
    {
        private static readonly Color MarkColor    = new Color(1f,   0.25f, 0.2f,  0.9f); // метка — красный
        private static readonly Color StunColor    = new Color(1f,   0.9f,  0.2f,  0.9f); // стан — жёлтый
        private static readonly Color ShieldColor  = new Color(0.4f, 0.7f,  1f,    0.9f); // щит — синий
        private static readonly Color FrozenColor  = new Color(0.5f, 0.9f,  1f,    0.9f); // заморозка — голубой
        private static readonly Color EmpowerColor = new Color(1f,   0.4f,  1f,    0.9f); // усиление — маджента

        private const float BaseRadius = 0.55f;
        private const float RingStep   = 0.13f;
        private const float Thickness  = 0.06f;

        // Верхний слой сортировки для dev-оверлеев (иначе спрайт арены перекрывает Shapes — они на Default).
        // Ленивый резолв: NameToID нельзя из инициализатора поля MonoBehaviour — кэшируем при первом обращении.
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

        private CombatSimulation _simulation;
        private bool _enabled = true;

        private readonly List<Disc> _pool = new List<Disc>();
        private int _used;

        public bool IsEnabled { get => _enabled; set => _enabled = value; }

        /// <summary>Инициализация рантайм-создателем (CombatPresenter): подать симуляцию для чтения состояния.</summary>
        public void Initialize(CombatSimulation simulation) => _simulation = simulation;

        private void LateUpdate()
        {
            _used = 0;

            if (_enabled && _simulation != null)
            {
                IReadOnlyList<RuntimeUnit> units = _simulation.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    RuntimeUnit u = units[i];
                    if (u.IsDead) continue;
                    DrawStatusRings(u);
                }
            }

            // Спрятать неиспользованные кольца пула.
            for (int i = _used; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        private void DrawStatusRings(RuntimeUnit u)
        {
            float baseR = u.Stats.Get(StatType.Size) * 0.5f + BaseRadius;
            int ring = 0;

            bool marked  = (u.EffectTagMask & EffectTag.Marked) != 0;
            bool frozen  = (u.EffectTagMask & EffectTag.Frozen) != 0;
            bool stunned = !u.CanAct || u.DisplacedTicksRemaining > 0;
            bool shield  = u.CurrentShield > 0f;
            bool empower = u.EmpowerDamageMult > 0f;

            if (marked)  DrawRing(u.Position, baseR + RingStep * ring++, MarkColor);
            if (stunned) DrawRing(u.Position, baseR + RingStep * ring++, StunColor);
            if (shield)  DrawRing(u.Position, baseR + RingStep * ring++, ShieldColor);
            if (frozen)  DrawRing(u.Position, baseR + RingStep * ring++, FrozenColor);
            if (empower) DrawRing(u.Position, baseR + RingStep * ring++, EmpowerColor);
        }

        private void DrawRing(Vector2 center, float radius, Color color)
        {
            Disc disc = Rent();
            disc.transform.position = new Vector3(center.x, center.y, -1f); // поверх спрайтов
            disc.Radius    = radius;
            disc.Thickness = Thickness;
            disc.Color     = color;
            if (!disc.gameObject.activeSelf) disc.gameObject.SetActive(true);
            _used++;
        }

        private Disc Rent()
        {
            if (_used < _pool.Count) return _pool[_used];

            var go = new GameObject("StatusRing");
            go.transform.SetParent(transform, worldPositionStays: false);
            var disc = go.AddComponent<Disc>();
            disc.Geometry       = DiscGeometry.Flat2D;
            disc.Type           = DiscType.Ring;
            disc.ThicknessSpace = ThicknessSpace.Meters;
            disc.SortingLayerID = OverlayLayerId; // поверх арены/юнитов
            _pool.Add(disc);
            return disc;
        }
    }
}
