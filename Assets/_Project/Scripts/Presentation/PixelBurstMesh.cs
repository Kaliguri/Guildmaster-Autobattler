using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Общий меш для пиксельного «брызга» (<see cref="PixelBurst"/>): <see cref="Count"/> квадов-«пикселей».
    /// В каждую вершину запечено: угловое смещение пикселя (position.xy ∈ [-0.5..0.5]), угол разлёта частицы
    /// (uv2.x, общий у 4 вершин квада), рандомы скорости/жизни (color.r/g) и порог отсева по количеству
    /// (color.b). Один меш переиспользуется всеми бёрстами (детерминированный сид) — форму бёрста задают
    /// параметры материала (цвет/скорость/конус/кол-во), а не меш. Zero-alloc: строится один раз.
    /// </summary>
    public static class PixelBurstMesh
    {
        public const int Count = 32; // запечённый максимум частиц; фактическое кол-во режется _CountFrac

        private const int Seed = 20260713;
        private static Mesh _shared;

        public static Mesh GetShared()
        {
            if (_shared != null) return _shared;
            _shared = Build();
            return _shared;
        }

        private static Mesh Build()
        {
            var rng = new System.Random(Seed);
            var verts  = new List<Vector3>(Count * 4);
            var rnds   = new List<Vector2>(Count * 4);
            var colors = new List<Color>(Count * 4);
            var tris   = new List<int>(Count * 6);

            for (int i = 0; i < Count; i++)
            {
                // Угол равномерно по кругу + джиттер → брызг не клочкуется; порог = i/Count, чтобы отсев
                // по _CountFrac оставлял равномерно распределённые по кругу частицы.
                float angle01   = (i + (float)rng.NextDouble()) / Count;
                float threshold = (float)i / Count;
                var rand = new Color(
                    (float)rng.NextDouble(), // r = speed
                    (float)rng.NextDouble(), // g = life
                    threshold,               // b = count threshold
                    1f);

                int start = verts.Count;
                AddCorner(-0.5f, -0.5f, angle01, rand, verts, rnds, colors);
                AddCorner( 0.5f, -0.5f, angle01, rand, verts, rnds, colors);
                AddCorner( 0.5f,  0.5f, angle01, rand, verts, rnds, colors);
                AddCorner(-0.5f,  0.5f, angle01, rand, verts, rnds, colors);
                tris.Add(start);     tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start);     tris.Add(start + 2); tris.Add(start + 3);
            }

            var mesh = new Mesh { name = "PixelBurst" };
            mesh.SetVertices(verts);
            mesh.SetUVs(1, rnds);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f); // не кулить: частицы уходят далеко от центра
            return mesh;
        }

        private static void AddCorner(
            float cx, float cy, float angle01, Color rand,
            List<Vector3> verts, List<Vector2> rnds, List<Color> colors)
        {
            verts.Add(new Vector3(cx, cy, 0f));
            rnds.Add(new Vector2(angle01, 0f));
            colors.Add(rand);
        }
    }
}
