using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Раскладка карты акта: топология (этаж + ряд) → координаты. Чистая функция без состояния и без
    /// Unity-объектов — тестируема и не зависит от того, кто и когда рисует.
    /// <para>Разброс узлов НЕ хранится в данных карты: он выводится хешем из id узла и сида забега.
    /// Поэтому он стабилен между открытиями карты и переживает загрузку сейва, но домен о нём не знает.</para>
    /// </summary>
    [Serializable]
    public struct MapLayout
    {
        [Tooltip("Расстояние между соседними этажами (мировые единицы).")]
        public float StepX;

        [Tooltip("Расстояние между соседними узлами внутри этажа.")]
        public float StepY;

        [Tooltip("Дрейф ЭТАЖА целиком поперёк пути, в долях шага. Основной источник «живости»: этаж уезжает " +
                 "вверх/вниз вместе со всеми своими узлами, поэтому карта волнится, но узлы не сближаются.")]
        public float FloorDriftY;

        [Tooltip("Разброс отдельного узла внутри этажа, в долях шага. Держим МАЛЫМ: соседи по этажу могут " +
                 "поехать навстречу друг другу, и на больших значениях они слипаются, а рёбра начинают " +
                 "пересекаться (монотонная лестница гарантирует непересекаемость только на ровной сетке).")]
        public float JitterY;

        [Tooltip("Разброс вдоль пути, в долях шага. Намеренно слабее вертикального: колонка должна " +
                 "читаться как вертикальный ряд, чтобы было видно, сколько вариантов на этаже.")]
        public float JitterX;

        /// <summary>Дефолты, одобренные Максом 2026-07-20: просторно, сильный разброс по Y, слабый по X.</summary>
        public static MapLayout Default => new MapLayout
        {
            StepX       = 5.0f,
            StepY       = 3.6f,
            FloorDriftY = 0.45f,
            JitterY     = 0.16f,
            JitterX     = 0.12f,
        };

        /// <summary>Позиции узлов относительно начала карты. Ряд центрируется по фактической ширине этажа.</summary>
        public Dictionary<string, Vector2> Resolve(IReadOnlyList<MapNodeVisual> nodes, long seed)
        {
            var result = new Dictionary<string, Vector2>(nodes.Count);
            if (nodes.Count == 0) return result;

            // Ширина этажа считается по факту: генератор её не сообщает, а центрировать ряд надо.
            var widthOf = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int floor = nodes[i].Floor;
                widthOf.TryGetValue(floor, out int w);
                if (nodes[i].Row + 1 > w) widthOf[floor] = nodes[i].Row + 1;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                MapNodeVisual n = nodes[i];
                int width = widthOf[n.Floor];

                float x = n.Floor * StepX;
                float y = (n.Row - (width - 1) * 0.5f) * StepY;

                // Дрейф всего этажа: хеш от НОМЕРА ЭТАЖА, а не от узла — вся колонка едет как целое,
                // поэтому карта волнится, а порядок и зазоры внутри этажа сохраняются.
                y += Signed(Hash(n.Floor.ToString(), seed, 2)) * FloorDriftY * StepY;

                // Два независимых хеша на узел — иначе смещения по осям коррелируют и узлы встают по диагонали.
                x += Signed(Hash(n.Id, seed, 0)) * JitterX * StepX;
                y += Signed(Hash(n.Id, seed, 1)) * JitterY * StepY;

                result[n.Id] = new Vector2(x, y);
            }
            return result;
        }

        // FNV-1a: свой хеш, а не string.GetHashCode — тот не гарантирован стабильным между запусками и
        // рантаймами, и карта разъезжалась бы после перезапуска игры.
        private static uint Hash(string id, long seed, int salt)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
                h ^= (uint)seed;         h *= 16777619u;
                h ^= (uint)(seed >> 32); h *= 16777619u;
                h ^= (uint)salt;         h *= 16777619u;
                return h;
            }
        }

        // uint → [-1, 1)
        private static float Signed(uint h) => (h / (float)uint.MaxValue) * 2f - 1f;
    }
}
