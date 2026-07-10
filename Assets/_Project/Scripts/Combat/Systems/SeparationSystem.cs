using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Разделение тел юнитов (вики «10» §5.1): круговые тела радиусом <c>Size × BodyRadiusPerSize</c>,
    /// мягкое позиционное расталкивание перекрытий. Это ЛОКАЛЬНОЕ избегание, а НЕ поиск пути —
    /// статические препятствия (геометрия арены / зоны способностей) будут отдельным навигационным
    /// слоем позже; персонажи друг для друга решаются здесь, расталкиванием, не перепрокладкой маршрута.
    /// <para>
    /// Детерминировано: каждая пара обрабатывается один раз (по <see cref="RuntimeUnit.Id"/>), порядок
    /// итерации фиксирован, без RNG; направление в вырожденном случае (позиции совпали) — по Id.
    /// Юниты в полёте (§9.9, <see cref="RuntimeUnit.DisplacedTicksRemaining"/>) ИСКЛЮЧЕНЫ из сепарации
    /// целиком — ими владеет <c>DisplacementSystem</c>, а удар летящего тела по цели/толпе делает логика
    /// броска (cannonball, урон + цепное отбрасывание), НЕ расталкивание (иначе оно спихивало бы цель с
    /// линии полёта до нанесения урона). Broad-phase через <see cref="SpatialHash"/>; итог клампится в
    /// границы арены. Место в тике: ПОСЛЕ движения/смещения, ДО ребилда хэша.
    /// </para>
    /// </summary>
    public sealed class SeparationSystem
    {
        // Тюнеры. Публичные, чтобы dev крутил вживую (gm_sep_*) без рекомпиляции. Стартовые значения
        // засевает CombatSimulation из снапшота SimTuning (SimTuningConfig); здесь — код-дефолты для
        // headless-конструирования без снапшота.
        public float BodyRadiusPerSize = SimTuning.Default.BodyRadiusPerSize;
        public float Strength          = SimTuning.Default.SeparationStrength;
        public int   Iterations        = SimTuning.Default.SeparationIterations;
        // Свои расталкиваются мягче (просачиваются к фронту), враги — на полную (линия держится).
        public float SameTeamScale     = SimTuning.Default.SeparationSameTeamScale;

        // Переиспользуемый буфер соседей — без аллокаций на горячем пути.
        private readonly List<RuntimeUnit> _neighbors = new List<RuntimeUnit>();

        /// <summary>Раздвинуть перекрывающиеся тела живых юнитов на один тик.</summary>
        public void Tick(List<RuntimeUnit> units, SpatialHash hash, in ArenaBounds bounds)
        {
            if (units == null || units.Count < 2 || hash == null) return;

            // Максимальный радиус тела — чтобы broad-phase запрос гарантированно накрыл любого соседа,
            // даже более крупного (Size варьируется).
            float maxRadius = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead || u.DisplacedTicksRemaining > 0) continue;
                float r = BodyRadius(u);
                if (r > maxRadius) maxRadius = r;
            }
            if (maxRadius <= 0f) return;

            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    RuntimeUnit a = units[i];
                    if (a.IsDead || a.DisplacedTicksRemaining > 0) continue; // в полёте — вне сепарации (владеет DisplacementSystem)

                    float ra = BodyRadius(a);
                    hash.QueryRadius(a.Position, ra + maxRadius, _neighbors);

                    for (int n = 0; n < _neighbors.Count; n++)
                    {
                        RuntimeUnit b = _neighbors[n];

                        // Пару обрабатываем ровно один раз (b.Id > a.Id); себя, мёртвых и летящих пропускаем.
                        if (b.Id <= a.Id || b.IsDead || b.DisplacedTicksRemaining > 0) continue;

                        float minDist = ra + BodyRadius(b);
                        Vector2 delta = a.Position - b.Position;
                        float distSq = delta.sqrMagnitude;
                        if (distSq >= minDist * minDist) continue; // не пересекаются

                        float dist = Mathf.Sqrt(distSq);
                        Vector2 dir = dist > 1e-4f ? delta / dist : DegenerateDir(a, b);
                        // Свои — мягче (просачиваются сквозь свои ряды к фронту), враги — на полную (линия держится).
                        float pairStrength = a.Team == b.Team ? Strength * SameTeamScale : Strength;
                        Vector2 halfPush = dir * ((minDist - dist) * pairStrength * 0.5f); // по половине каждому

                        a.Position = bounds.Clamp(a.Position + halfPush);
                        b.Position = bounds.Clamp(b.Position - halfPush);
                    }
                }
            }
        }

        private float BodyRadius(RuntimeUnit u)
        {
            float size = u.Stats.Get(StatType.Size);
            return Mathf.Max(0.01f, size) * BodyRadiusPerSize;
        }

        // Тела точь-в-точь: детерминированно раздвигаем по X — младший Id влево, старший вправо.
        private static Vector2 DegenerateDir(RuntimeUnit a, RuntimeUnit b) =>
            new Vector2(a.Id < b.Id ? -1f : 1f, 0f);
    }
}
