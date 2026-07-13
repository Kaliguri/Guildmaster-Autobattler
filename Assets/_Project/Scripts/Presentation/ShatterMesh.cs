using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Строит (и кэширует) единый меш для разлёта спрайта на осколки (<see cref="DeathShatter"/>).
    /// Квад [-0.5..0.5] × [-0.5..0.5] режется джиттер-сеткой на ячейки РАЗНЫХ размеров, каждая — на два
    /// РАЗЪЕДИНЁННЫХ треугольника (свои 3 вершины, чтобы двигаться независимо). В каждую вершину запекается
    /// центроид её треугольника (uv2 — общий у трёх вершин, точка разлёта/вращения) и случайные параметры
    /// осколка (color: r=speed, g=spin, b=dirJitter). UV0 = позиция+0.5 → [0..1] под текстуру спрайта.
    /// Меш общий на всех юнитов (детерминированный сид) — zero-alloc в бою: строится один раз.
    /// </summary>
    public static class ShatterMesh
    {
        private const int Cols = 7;
        private const int Rows = 9;
        private const float LineJitter = 0.4f; // доля ячейки, на которую гуляют внутренние линии сетки
        private const int Seed = 1337;

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

            // Линии сетки по осям с джиттером внутренних узлов (края фиксированы на ±0.5) → ячейки разных размеров.
            float[] xs = BuildLines(Cols, rng);
            float[] ys = BuildLines(Rows, rng);

            var verts     = new List<Vector3>(Cols * Rows * 6);
            var uvs       = new List<Vector2>(Cols * Rows * 6);
            var centroids = new List<Vector2>(Cols * Rows * 6);
            var colors    = new List<Color>(Cols * Rows * 6);
            var tris      = new List<int>(Cols * Rows * 6);

            for (int cy = 0; cy < Rows; cy++)
            for (int cx = 0; cx < Cols; cx++)
            {
                Vector2 bl = new Vector2(xs[cx],     ys[cy]);
                Vector2 br = new Vector2(xs[cx + 1], ys[cy]);
                Vector2 tl = new Vector2(xs[cx],     ys[cy + 1]);
                Vector2 tr = new Vector2(xs[cx + 1], ys[cy + 1]);

                // Диагональ ячейки чередуем — треугольники выглядят менее «сеточно».
                bool flipDiag = ((cx + cy) & 1) == 0;
                if (flipDiag)
                {
                    AddTri(bl, br, tl, verts, uvs, centroids, colors, tris, rng);
                    AddTri(br, tr, tl, verts, uvs, centroids, colors, tris, rng);
                }
                else
                {
                    AddTri(bl, br, tr, verts, uvs, centroids, colors, tris, rng);
                    AddTri(bl, tr, tl, verts, uvs, centroids, colors, tris, rng);
                }
            }

            var mesh = new Mesh { name = "ShatterQuad" };
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, centroids);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Позиции линий по одной оси: N ячеек → N+1 узлов, края фиксированы, внутренние — с джиттером.
        private static float[] BuildLines(int cells, System.Random rng)
        {
            var lines = new float[cells + 1];
            float step = 1f / cells;
            for (int i = 0; i <= cells; i++)
            {
                float baseT = i * step; // 0..1
                if (i > 0 && i < cells)
                    baseT += ((float)rng.NextDouble() - 0.5f) * step * LineJitter;
                lines[i] = baseT - 0.5f; // в [-0.5..0.5]
            }
            return lines;
        }

        private static void AddTri(
            Vector2 a, Vector2 b, Vector2 c,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> centroids, List<Color> colors, List<int> tris,
            System.Random rng)
        {
            Vector2 centroid = (a + b + c) / 3f;
            // Случайные параметры осколка — ОДНИ на треугольник (у всех трёх вершин одинаковые).
            var rand = new Color(
                (float)rng.NextDouble(),  // r = speed
                (float)rng.NextDouble(),  // g = spin
                (float)rng.NextDouble(),  // b = dir jitter
                1f);

            int start = verts.Count;
            AddVertex(a, centroid, rand, verts, uvs, centroids, colors);
            AddVertex(b, centroid, rand, verts, uvs, centroids, colors);
            AddVertex(c, centroid, rand, verts, uvs, centroids, colors);
            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
        }

        private static void AddVertex(
            Vector2 pos, Vector2 centroid, Color rand,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> centroids, List<Color> colors)
        {
            verts.Add(new Vector3(pos.x, pos.y, 0f));
            uvs.Add(new Vector2(pos.x + 0.5f, pos.y + 0.5f)); // [-0.5..0.5] → [0..1]
            centroids.Add(centroid);
            colors.Add(rand);
        }
    }
}
