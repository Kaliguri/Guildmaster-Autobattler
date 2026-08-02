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
    /// читает состояние боя и рисует вокруг живых юнитов концентрические кольца (immediate-mode
    /// с пулом Disc-Ring). Чистая презентация — сим не трогает. Создаётся в рантайме
    /// <see cref="CombatPresenter"/> (никаких правок префабов/сцены); тогглится <see cref="IsEnabled"/>
    /// (команда <c>gm_toggle_status</c>). Не финальный VFX — инструмент наглядности среза.
    /// <para><b>Источник состояния — по умолчанию ПОКАЗАННЫЙ кадр</b> (<see cref="DevOverlayMode"/>):
    /// кольца обязаны быть на тех юнитах, которых видит игрок. Живой сим впереди на окно опережения, и
    /// в его координатах кольца висели бы в пустоте. Режим переключаем (<c>gm_overlay_source</c>) и
    /// подписан на экране — иначе разъезд читается как баг, а не как выбранный режим.</para>
    /// </summary>
    public sealed class CombatStatusOverlay : MonoBehaviour
    {
        private static readonly Color MarkColor    = new Color(1f,   0.25f, 0.2f,  0.9f); // метка — красный
        private static readonly Color StunColor    = new Color(1f,   0.9f,  0.2f,  0.9f); // стан — жёлтый
        private static readonly Color ShieldColor  = new Color(0.4f, 0.7f,  1f,    0.9f); // щит — синий
        private static readonly Color FrozenColor  = new Color(0.5f, 0.9f,  1f,    0.9f); // заморозка — голубой
        private static readonly Color EmpowerColor = new Color(1f,   0.4f,  1f,    0.9f); // усиление — маджента

        // Базовый радиус кольца = половина видимой ширины тела при Size 1 + небольшой зазор
        // (тело ~0.6 юнита в ширину при росте ~1.7). Крутится под финальный размер спрайта на глаз.
        private const float BaseRadius = 0.35f;
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

        private CombatSimulation             _simulation;
        private Combat.Tape.BattleTapePlayback _playback;
        private DevOverlayMode               _mode;
        private bool _enabled = true;

        private readonly List<Disc> _pool = new List<Disc>();
        private int _used;

        public bool IsEnabled { get => _enabled; set => _enabled = value; }

        /// <summary>
        /// Инициализация рантайм-создателем (<see cref="CombatPresenter"/>): подать оба источника
        /// состояния и владельца режима. Сим нужен даже в режиме показа — на него переключаются руками.
        /// </summary>
        public void Initialize(
            CombatSimulation simulation, Combat.Tape.BattleTapePlayback playback, DevOverlayMode mode)
        {
            _simulation = simulation;
            _playback   = playback;
            _mode       = mode;
        }

        private void LateUpdate()
        {
            _used = 0;

            if (_enabled) DrawAllUnits();

            // Спрятать неиспользованные кольца пула.
            for (int i = _used; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        // Экранная подпись режима: без неё разъезд «оверлей не там, где бой» читается как баг.
        // Печатается, только когда оверлей включён — молчаливый инструмент не нуждается в подписи.
        private void OnGUI()
        {
            if (!_enabled || _mode == null) return;

            GUI.color = Color.white;
            // Ниже верхней панели забега: в самом верху подпись ложилась прямо на её заголовок и мешала
            // читать оба (замечание Макса 02.08.2026). Панель занимает первые ~64 px при Full HD.
            GUI.Label(new Rect(12f, TopBarHeight + 8f, 640f, 22f), _mode.Describe());
        }

        /// <summary>Высота верхней панели забега, под которой начинается место для dev-подписей.</summary>
        private const float TopBarHeight = 64f;

        private void DrawAllUnits()
        {
            // Режим показа: кадр ленты — ровно то, что на экране. Кадра может не быть (бой не начат) —
            // тогда рисовать нечего, и это не ошибка.
            if (_mode == null || !_mode.ReadsSimulation)
            {
                if (_playback == null) return;
                if (!_playback.TryGetFrame(out IReadOnlyList<Combat.Tape.UnitSnapshot> frame)) return;

                for (int i = 0; i < frame.Count; i++)
                {
                    Combat.Tape.UnitSnapshot s = frame[i];
                    if (s.IsDead) continue;
                    DrawStatusRings(
                        s.Position, s.Size, s.EffectTagMask,
                        stunned: !s.CanAct || s.IsDisplaced, shield: s.CurrentShield > 0f,
                        empower: s.IsEmpowered);
                }
                return;
            }

            // Режим сима: правда модели, на окно опережения впереди картинки. Осознанный выбор — отладка
            // самой ленты; подпись сверху объясняет, почему кольца разъехались с боем.
            if (_simulation == null) return;

            IReadOnlyList<RuntimeUnit> units = _simulation.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead) continue;
                DrawStatusRings(
                    u.Position, u.Stats.Get(StatType.Size), u.EffectTagMask,
                    stunned: !u.CanAct || u.DisplacedTicksRemaining > 0, shield: u.CurrentShield > 0f,
                    empower: u.EmpowerDamageMult > 0f);
            }
        }

        // Один рисовальщик на оба источника: набор колец и их порядок не должны зависеть от режима,
        // иначе переключатель врал бы о состоянии, а не о моменте.
        private void DrawStatusRings(
            Vector2 position, float size, EffectTag tags, bool stunned, bool shield, bool empower)
        {
            float baseR = size * 0.5f + BaseRadius;
            int ring = 0;

            bool marked = (tags & EffectTag.Marked) != 0;
            bool frozen = (tags & EffectTag.Frozen) != 0;

            if (marked)  DrawRing(position, baseR + RingStep * ring++, MarkColor);
            if (stunned) DrawRing(position, baseR + RingStep * ring++, StunColor);
            if (shield)  DrawRing(position, baseR + RingStep * ring++, ShieldColor);
            if (frozen)  DrawRing(position, baseR + RingStep * ring++, FrozenColor);
            if (empower) DrawRing(position, baseR + RingStep * ring++, EmpowerColor);
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
