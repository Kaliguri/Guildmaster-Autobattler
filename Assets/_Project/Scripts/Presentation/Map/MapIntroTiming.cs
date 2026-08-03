using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Когда какая часть карты прорастает на интро: фронт выходит из стартового узла и идёт ПО ДОРОЖКАМ,
    /// сам ветвясь на развилках. Чистая математика над графом — Unity здесь нет, кроме <c>Mathf</c>.
    /// <para>Каждое ребро растёт со СВОЕЙ скоростью (решение Макса: рост должен быть хаотичным, а не
    /// циркульным). Скорость выводится из пары id узлов, поэтому один и тот же акт прорастает одинаково
    /// при каждом показе — «хаос» здесь про рисунок, а не про случайность от запуска к запуску.</para>
    /// <para>Ищется КРАТЧАЙШЕЕ ВРЕМЯ, а не длина: у рёбер разная скорость, и длинная быстрая ветка может
    /// обогнать короткую медленную. Обход ненаправленный — дорожка прорастает и «назад», если фронт
    /// пришёл к ней с дальнего конца.</para>
    /// </summary>
    public static class MapIntroTiming
    {
        /// <summary>Ребро карты в терминах роста: кто с кем и какой длины дуга.</summary>
        public readonly struct Edge
        {
            /// <summary>Индекс узла-начала в списке узлов.</summary>
            public readonly int From;

            /// <summary>Индекс узла-конца.</summary>
            public readonly int To;

            /// <summary>Длина дуги в мировых единицах — по ней и считается время прохода.</summary>
            public readonly float Length;

            public Edge(int from, int to, float length)
            {
                From = from; To = to; Length = length;
            }
        }

        /// <summary>
        /// Раскладывает граф по времени.
        /// </summary>
        /// <param name="edges">Рёбра (индексами узлов). Длина дуги — фактическая, не расстояние по прямой.</param>
        /// <param name="edgeSpeeds">Скорость каждого ребра, ед./сек. Заполняется здесь же по <paramref name="scatter"/>.</param>
        /// <param name="edgeKeys">Пара id для хеша скорости — тот же порядок, что у <paramref name="edges"/>.</param>
        /// <param name="nodeCount">Сколько всего узлов.</param>
        /// <param name="start">Индекс узла, из которого растём. Вне диапазона — растим из нулевого.</param>
        /// <param name="baseSpeed">Базовая скорость роста, ед./сек.</param>
        /// <param name="scatter">Разброс скоростей, 0..0.9. Ноль — все ветки идут ровно.</param>
        /// <param name="nodeTimes">Результат: когда фронт доходит до узла (секунды от начала роста).</param>
        public static void Resolve(IReadOnlyList<Edge> edges,
                                   IReadOnlyList<(string From, string To)> edgeKeys,
                                   int nodeCount,
                                   int start,
                                   float baseSpeed,
                                   float scatter,
                                   float[] edgeSpeeds,
                                   float[] nodeTimes)
        {
            if (nodeTimes == null || edgeSpeeds == null) return;

            float speed = Mathf.Max(0.01f, baseSpeed);
            scatter = Mathf.Clamp(scatter, 0f, 0.9f);

            for (int i = 0; i < edges.Count && i < edgeSpeeds.Length; i++)
            {
                float k = 1f;
                if (scatter > 0f && edgeKeys != null && i < edgeKeys.Count)
                {
                    // Хеш пары id → [-1..1] → множитель скорости. Тот же приём, что у стороны изгиба пути:
                    // рисунок карты обязан быть одним и тем же при каждом её показе.
                    uint h = Core.Random.DeterministicHash.Of32(edgeKeys[i].From, edgeKeys[i].To);
                    float u = (h & 0xFFFFFFu) / 16777216f;
                    k = 1f + (u * 2f - 1f) * scatter;
                }
                edgeSpeeds[i] = speed * Mathf.Max(0.05f, k);
            }

            for (int i = 0; i < nodeTimes.Length; i++) nodeTimes[i] = float.PositiveInfinity;
            if (nodeCount <= 0) return;

            int from = start >= 0 && start < nodeCount ? start : 0;
            nodeTimes[from] = 0f;

            // Дейкстра выборкой минимума перебором: узлов на акте десятки, куча здесь дала бы только код.
            var settled = new bool[nodeCount];
            for (int step = 0; step < nodeCount; step++)
            {
                int at = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < nodeCount; i++)
                    if (!settled[i] && nodeTimes[i] < best) { best = nodeTimes[i]; at = i; }

                if (at < 0) break;      // остаток графа со стартом не связан
                settled[at] = true;

                for (int e = 0; e < edges.Count; e++)
                {
                    Edge edge = edges[e];
                    int other = edge.From == at ? edge.To : (edge.To == at ? edge.From : -1);
                    if (other < 0 || other >= nodeCount) continue;

                    float arrive = best + edge.Length / Mathf.Max(0.05f, edgeSpeeds[e]);
                    if (arrive < nodeTimes[other]) nodeTimes[other] = arrive;
                }
            }

            // Недостижимое (оборванная связь в генерации, узел без рёбер) не должно пропасть с карты
            // насовсем: такие узлы появляются последними, вместе с концом роста. Спрятать их навсегда —
            // куда худший дефект, чем показать не в такт.
            float last = 0f;
            for (int i = 0; i < nodeCount; i++)
                if (!float.IsInfinity(nodeTimes[i]) && nodeTimes[i] > last) last = nodeTimes[i];

            for (int i = 0; i < nodeCount; i++)
                if (float.IsInfinity(nodeTimes[i])) nodeTimes[i] = last;
        }
    }
}
