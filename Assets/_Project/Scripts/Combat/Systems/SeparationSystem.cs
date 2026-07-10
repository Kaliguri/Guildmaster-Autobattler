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
    /// Юниты в полёте (§9.9, <see cref="RuntimeUnit.DisplacedTicksRemaining"/>) — неподвижные «толкатели»:
    /// занимают место и толкают других, но сами не двигаются (импакт полёта — Фаза 2). Broad-phase через
    /// <see cref="SpatialHash"/>; итог клампится в границы арены. Место в тике: ПОСЛЕ движения/смещения,
    /// ДО ребилда хэша.
    /// </para>
    /// </summary>
    public sealed class SeparationSystem
    {
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
                if (u.IsDead) continue;
                float r = BodyRadius(u);
                if (r > maxRadius) maxRadius = r;
            }
            if (maxRadius <= 0f) return;

            for (int iter = 0; iter < SimConstants.SeparationIterations; iter++)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    RuntimeUnit a = units[i];
                    if (a.IsDead) continue;

                    float ra = BodyRadius(a);
                    hash.QueryRadius(a.Position, ra + maxRadius, _neighbors);

                    for (int n = 0; n < _neighbors.Count; n++)
                    {
                        RuntimeUnit b = _neighbors[n];

                        // Каждую неупорядоченную пару обрабатываем ровно один раз (b.Id > a.Id); себя пропускаем.
                        if (b.Id <= a.Id || b.IsDead) continue;

                        float rb = BodyRadius(b);
                        float minDist = ra + rb;

                        Vector2 delta = a.Position - b.Position;
                        float distSq = delta.sqrMagnitude;
                        if (distSq >= minDist * minDist) continue; // не пересекаются

                        bool aMovable = a.DisplacedTicksRemaining <= 0;
                        bool bMovable = b.DisplacedTicksRemaining <= 0;
                        if (!aMovable && !bMovable) continue; // оба в полёте — не двигаем ни одного

                        float dist = Mathf.Sqrt(distSq);
                        Vector2 dir = dist > 1e-4f ? delta / dist : DegenerateDir(a, b);
                        Vector2 push = dir * ((minDist - dist) * SimConstants.SeparationStrength);

                        if (aMovable && bMovable)
                        {
                            a.Position = bounds.Clamp(a.Position + push * 0.5f);
                            b.Position = bounds.Clamp(b.Position - push * 0.5f);
                        }
                        else if (aMovable)
                        {
                            a.Position = bounds.Clamp(a.Position + push); // неподвижный b забирает всё проникновение
                        }
                        else
                        {
                            b.Position = bounds.Clamp(b.Position - push); // неподвижен a
                        }
                    }
                }
            }
        }

        private static float BodyRadius(RuntimeUnit u)
        {
            float size = u.Stats.Get(StatType.Size);
            return Mathf.Max(0.01f, size) * SimConstants.BodyRadiusPerSize;
        }

        // Тела точь-в-точь: детерминированно раздвигаем по X — младший Id влево, старший вправо.
        private static Vector2 DegenerateDir(RuntimeUnit a, RuntimeUnit b) =>
            new Vector2(a.Id < b.Id ? -1f : 1f, 0f);
    }
}
