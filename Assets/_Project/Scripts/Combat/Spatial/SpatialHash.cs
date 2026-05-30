using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Равномерная сетка для пространственных запросов без аллокаций на горячем пути.
    /// Используется для <c>QueryRadius</c> в <see cref="TargetingSystem"/> и AOE-эффектах (Фаза 2).
    /// <para>Не влияет на геометрию поля — оно по-прежнему непрерывное (вики «10» §5.3).</para>
    /// </summary>
    public sealed class SpatialHash
    {
        private readonly float _cellSize;
        private readonly float _invCellSize;
        private readonly Dictionary<long, List<RuntimeUnit>> _cells =
            new Dictionary<long, List<RuntimeUnit>>();

        // Пул освободившихся списков: переиспользуем аллокации между ребилдами,
        // не давая словарю копить пустые ячейки от посещённых, но покинутых клеток.
        private readonly Stack<List<RuntimeUnit>> _listPool =
            new Stack<List<RuntimeUnit>>();

        public float CellSize => _cellSize;

        public SpatialHash(float cellSize)
        {
            _cellSize    = cellSize;
            _invCellSize = 1f / cellSize;
        }

        /// <summary>Добавить юнита в хэш.</summary>
        public void Add(RuntimeUnit unit)
        {
            long key = CellKey(unit.Position);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = RentList();
                _cells[key] = list;
            }
            list.Add(unit);
        }

        /// <summary>Удалить юнита из хэша (например, после смерти).</summary>
        public void Remove(RuntimeUnit unit)
        {
            long key = CellKey(unit.Position);
            if (_cells.TryGetValue(key, out var list))
            {
                list.Remove(unit);
                if (list.Count == 0)
                {
                    _cells.Remove(key);
                    _listPool.Push(list);
                }
            }
        }

        /// <summary>
        /// Перестроить хэш по текущим позициям всех активных юнитов.
        /// Вызывается после фазы движения каждого тика.
        /// </summary>
        public void Rebuild(List<RuntimeUnit> units)
        {
            // Возвращаем все списки в пул и чистим словарь, чтобы он содержал
            // только реально занятые ячейки этого тика, а не все исторически посещённые.
            foreach (var kvp in _cells)
            {
                kvp.Value.Clear();
                _listPool.Push(kvp.Value);
            }
            _cells.Clear();

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;

                long key = CellKey(unit.Position);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = RentList();
                    _cells[key] = list;
                }
                list.Add(unit);
            }
        }

        /// <summary>
        /// Заполнить <paramref name="results"/> юнитами в радиусе <paramref name="radius"/>
        /// от <paramref name="center"/>. Мёртвые юниты пропускаются.
        /// </summary>
        public void QueryRadius(Vector2 center, float radius, List<RuntimeUnit> results)
        {
            results.Clear();
            int minCx = WorldToCell(center.x - radius);
            int maxCx = WorldToCell(center.x + radius);
            int minCy = WorldToCell(center.y - radius);
            int maxCy = WorldToCell(center.y + radius);

            float radiusSq = radius * radius;

            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cy = minCy; cy <= maxCy; cy++)
                {
                    long key = PackKey(cx, cy);
                    if (!_cells.TryGetValue(key, out var list)) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        RuntimeUnit unit = list[i];
                        if (unit.IsDead) continue;
                        if ((unit.Position - center).sqrMagnitude <= radiusSq)
                            results.Add(unit);
                    }
                }
            }
        }

        private List<RuntimeUnit> RentList() =>
            _listPool.Count > 0 ? _listPool.Pop() : new List<RuntimeUnit>(4);

        private int WorldToCell(float worldCoord) =>
            Mathf.FloorToInt(worldCoord * _invCellSize);

        private long CellKey(Vector2 pos) =>
            PackKey(WorldToCell(pos.x), WorldToCell(pos.y));

        private static long PackKey(int cx, int cy) =>
            ((long)(uint)cx << 32) | (uint)cy;
    }
}
