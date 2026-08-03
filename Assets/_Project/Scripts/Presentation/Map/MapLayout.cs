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
    /// <para>После разброса раскладка ПРОВЕРЯЕТСЯ правилами: узлы разводятся, пока каждый не отстоит от
    /// прочих на <see cref="MinDistance"/>. Без этого узлы соседних этажей налезают друг на друга — разброс
    /// сам по себе такого не гарантирует.</para>
    /// </summary>
    [Serializable]
    public struct MapLayout
    {
        [Tooltip("Расстояние между соседними этажами (мировые единицы).")]
        public float StepX;

        [Tooltip("Расстояние между соседними узлами внутри этажа.")]
        public float StepY;

        [Tooltip("Разброс поперёк пути, в долях шага. По умолчанию 0: узлы этажа стоят ровным столбиком.")]
        public float JitterY;

        [Tooltip("Разброс вдоль пути, в долях шага. По умолчанию 0 — этаж обязан читаться вертикальным " +
                 "столбиком, узел под узлом (требование Макса по play-QA).")]
        public float JitterX;

        [Tooltip("Минимальное расстояние между любыми двумя узлами (мировые единицы). Правило проверки " +
                 "раскладки: то, что ближе — разводится.")]
        public float MinDistance;

        [Tooltip("Сколько проходов расталкивания делать. Обычно хватает 8-12.")]
        public int RelaxIterations;

        /// <summary>
        /// Дефолты (Макс, 2026-07-20, второй раунд): РОВНАЯ сетка и растянутые шаги. Разброс убран целиком —
        /// он мешал этажу читаться столбиком, а живость рисунка должна идти от формы графа, а не от шума
        /// поверх неё. Карта задумана как большая КАРТА (выбор биома, а не только типа узла), поэтому
        /// расстояния увеличены — узлам нужен воздух.
        /// </summary>
        public static MapLayout Default => new MapLayout
        {
            StepX           = 6.5f,
            StepY           = 4.2f,
            JitterY         = 0f,
            JitterX         = 0f,
            MinDistance     = 2.2f,
            RelaxIterations = 10,
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

            var pos = new Vector2[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                MapNodeVisual n = nodes[i];
                int width = widthOf[n.Floor];

                float x = n.Floor * StepX;
                float y = (n.Row - (width - 1) * 0.5f) * StepY;

                // Два независимых хеша на узел — иначе смещения по осям коррелируют и узлы встают по диагонали.
                x += Signed(Hash(n.Id, seed, 0)) * JitterX * StepX;
                y += Signed(Hash(n.Id, seed, 1)) * JitterY * StepY;

                pos[i] = new Vector2(x, y);
            }

            Relax(nodes, pos);

            for (int i = 0; i < nodes.Count; i++) result[nodes[i].Id] = pos[i];
            return result;
        }

        /// <summary>
        /// Расталкивает узлы, оказавшиеся ближе <see cref="MinDistance"/>. Разводим ПРЕИМУЩЕСТВЕННО по Y:
        /// движение по X смешивало бы этажи между собой, а столбик должен оставаться читаемым.
        /// </summary>
        private void Relax(IReadOnlyList<MapNodeVisual> nodes, Vector2[] pos)
        {
            if (MinDistance <= 0f || RelaxIterations <= 0) return;

            float minSqr = MinDistance * MinDistance;
            for (int iter = 0; iter < RelaxIterations; iter++)
            {
                bool moved = false;
                for (int i = 0; i < pos.Length; i++)
                {
                    for (int j = i + 1; j < pos.Length; j++)
                    {
                        Vector2 delta = pos[j] - pos[i];
                        float sqr = delta.sqrMagnitude;
                        if (sqr >= minSqr) continue;

                        // Совпали точь-в-точь — разводим по детерминированному признаку, а не случайно.
                        if (sqr < 0.0001f)
                        {
                            delta = new Vector2(0f, nodes[i].Row <= nodes[j].Row ? -1f : 1f);
                            sqr = 1f;
                        }

                        float dist = Mathf.Sqrt(sqr);
                        float push = (MinDistance - dist) * 0.5f;
                        Vector2 dir = delta / dist;
                        // Гасим горизонтальную составляющую: пусть узлы расходятся вверх-вниз.
                        dir = new Vector2(dir.x * 0.25f, dir.y).normalized;

                        pos[i] -= dir * push;
                        pos[j] += dir * push;
                        moved = true;
                    }
                }
                if (!moved) break; // раскладка уже удовлетворяет правилу — дальше гонять незачем
            }
        }

        // Хеш — общий (Core.Random.DeterministicHash): своя копия формулы разъехалась бы с лавкой и
        // дорожками на первой же правке, а раскладка обязана совпадать у всех и после перезапуска.
        private static uint Hash(string id, long seed, int salt)
            => Guildmaster.Core.Random.DeterministicHash.Of32(id, seed, salt);

        // uint → [-1, 1)
        private static float Signed(uint h) => (h / (float)uint.MaxValue) * 2f - 1f;
    }
}
