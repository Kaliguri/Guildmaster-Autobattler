using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Строит меш для разлёта спрайта на осколки (<see cref="DeathShatter"/>) под КОНКРЕТНЫЙ видимый размер
    /// спрайта. Прямоугольник <paramref name="size"/> (в локальных ед. спрайта, центр в 0) режется джиттер-сеткой
    /// на ячейки РАЗНЫХ размеров, каждая — на два РАЗЪЕДИНЁННЫХ треугольника (свои 3 вершины, чтобы двигаться
    /// независимо). В вершину запекается центроид её треугольника (uv2 — общий у трёх, точка разлёта/вращения)
    /// и случайные параметры осколка (color: r=speed, g=spin, b=dirJitter). UV0 мапится на <paramref name="uvRect"/>
    /// (тесная область текстуры под спрайтом) — осколки несут именно пиксели персонажа, без размазывания.
    /// Меш строится ПОД смерть (маленький, ~126 треугольников) и уничтожается вместе с эффектом.
    /// </summary>
    public static class ShatterMesh
    {
        private const int Cols = 7;
        private const int Rows = 9;
        private const float LineJitter = 0.4f; // доля ячейки, на которую гуляют внутренние линии сетки
        private const int Seed = 1337;

        /// <summary>Собрать меш размером <paramref name="size"/> (лок. ед., центр в 0), UV в области <paramref name="uvRect"/>.</summary>
        public static Mesh Build(Vector2 size, Rect uvRect)
        {
            var rng = new System.Random(Seed);

            // Доли линий сетки по осям [0..1] с джиттером внутренних узлов → ячейки разных размеров.
            float[] fx = BuildLines(Cols, rng);
            float[] fy = BuildLines(Rows, rng);

            int cap = Cols * Rows * 6;
            var verts     = new List<Vector3>(cap);
            var uvs       = new List<Vector2>(cap);
            var centroids = new List<Vector2>(cap);
            var colors    = new List<Color>(cap);
            var tris      = new List<int>(cap);

            for (int cy = 0; cy < Rows; cy++)
            for (int cx = 0; cx < Cols; cx++)
            {
                Vector2 bl = Corner(fx[cx],     fy[cy],     size);
                Vector2 br = Corner(fx[cx + 1], fy[cy],     size);
                Vector2 tl = Corner(fx[cx],     fy[cy + 1], size);
                Vector2 tr = Corner(fx[cx + 1], fy[cy + 1], size);

                bool flipDiag = ((cx + cy) & 1) == 0; // чередуем диагональ — меньше «сеточности»
                if (flipDiag)
                {
                    AddTri(bl, br, tl, size, uvRect, verts, uvs, centroids, colors, tris, rng);
                    AddTri(br, tr, tl, size, uvRect, verts, uvs, centroids, colors, tris, rng);
                }
                else
                {
                    AddTri(bl, br, tr, size, uvRect, verts, uvs, centroids, colors, tris, rng);
                    AddTri(bl, tr, tl, size, uvRect, verts, uvs, centroids, colors, tris, rng);
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

        // Доля [0..1] сетки → лок. координата, центр в 0: (f-0.5)*size.
        private static Vector2 Corner(float fxi, float fyi, Vector2 size)
            => new Vector2((fxi - 0.5f) * size.x, (fyi - 0.5f) * size.y);

        // Позиции линий по одной оси: N ячеек → N+1 узлов (доли [0..1]), края фиксированы, внутренние — джиттер.
        private static float[] BuildLines(int cells, System.Random rng)
        {
            var lines = new float[cells + 1];
            float step = 1f / cells;
            for (int i = 0; i <= cells; i++)
            {
                float t = i * step;
                if (i > 0 && i < cells) t += ((float)rng.NextDouble() - 0.5f) * step * LineJitter;
                lines[i] = t;
            }
            return lines;
        }

        private static void AddTri(
            Vector2 a, Vector2 b, Vector2 c, Vector2 size, Rect uvRect,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> centroids, List<Color> colors, List<int> tris,
            System.Random rng)
        {
            Vector2 centroid = (a + b + c) / 3f;
            var rand = new Color(
                (float)rng.NextDouble(),  // r = speed
                (float)rng.NextDouble(),  // g = spin
                (float)rng.NextDouble(),  // b = dir jitter
                1f);

            int start = verts.Count;
            AddVertex(a, centroid, rand, size, uvRect, verts, uvs, centroids, colors);
            AddVertex(b, centroid, rand, size, uvRect, verts, uvs, centroids, colors);
            AddVertex(c, centroid, rand, size, uvRect, verts, uvs, centroids, colors);
            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
        }

        private static void AddVertex(
            Vector2 pos, Vector2 centroid, Color rand, Vector2 size, Rect uvRect,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> centroids, List<Color> colors)
        {
            verts.Add(new Vector3(pos.x, pos.y, 0f));
            // Доля позиции внутри прямоугольника [0..1] → UV в тесной области текстуры.
            float u = size.x != 0f ? pos.x / size.x + 0.5f : 0.5f;
            float v = size.y != 0f ? pos.y / size.y + 0.5f : 0.5f;
            uvs.Add(new Vector2(uvRect.xMin + u * uvRect.width, uvRect.yMin + v * uvRect.height));
            centroids.Add(centroid);
            colors.Add(rand);
        }
    }
}
