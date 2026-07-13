using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Строит меш «пиксельного» разлёта спрайта (<see cref="DeathShatter"/>): видимая область режется на
    /// БЛОЧНЫЕ куски-квады разных размеров, выровненные по пиксель-сетке исходного спрайта (границы блоков
    /// падают на границы исходных пикселей → чанки выглядят как настоящие куски пиксель-персонажа, а не
    /// вектор-слайсы). Каждый блок — жёсткий квад (4 вершины, все несут ОДИН центроид блока в uv2 и ОДИН
    /// набор случайных параметров в color), поэтому летит и вращается как единое целое. UV0 мапится на
    /// тесную область текстуры (<paramref name="uvRect"/>). Меш строится под смерть и уничтожается с эффектом.
    /// </summary>
    public static class ShatterMesh
    {
        private const float LineJitter = 0.6f; // насколько гуляют внутренние линии (в долях ячейки) → разные размеры
        private const int Seed = 1337;

        /// <summary>
        /// Собрать блочный меш размером <paramref name="size"/> (лок. ед., центр в 0), UV в области
        /// <paramref name="uvRect"/>. <paramref name="regionPixels"/> — размер области в ИСХОДНЫХ пикселях
        /// (для снапа границ), <paramref name="blockPixels"/> — целевой размер чанка в пикселях.
        /// </summary>
        public static Mesh Build(Vector2 size, Rect uvRect, Vector2 regionPixels, int blockPixels)
        {
            var rng = new System.Random(Seed);
            int bp = Mathf.Max(1, blockPixels);
            int cols = Mathf.Clamp(Mathf.RoundToInt(regionPixels.x / bp), 3, 16);
            int rows = Mathf.Clamp(Mathf.RoundToInt(regionPixels.y / bp), 3, 20);

            float[] fx = BuildLines(cols, regionPixels.x, rng); // доли [0..1] границ по X, снап на пиксели
            float[] fy = BuildLines(rows, regionPixels.y, rng);

            int cap = cols * rows * 4;
            var verts     = new List<Vector3>(cap);
            var uvs       = new List<Vector2>(cap);
            var centroids = new List<Vector2>(cap);
            var colors    = new List<Color>(cap);
            var tris      = new List<int>(cols * rows * 6);

            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
                AddBlock(fx[cx], fy[cy], fx[cx + 1], fy[cy + 1], size, uvRect, verts, uvs, centroids, colors, tris, rng);

            var mesh = new Mesh { name = "ShatterBlocks" };
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

        // Доли [0..1] границ ячеек, СНАПНУТЫЕ на пиксельную сетку области (строго возрастают, каждая ≥1 px шире).
        private static float[] BuildLines(int cells, float regionPx, System.Random rng)
        {
            int w = Mathf.Max(cells, Mathf.RoundToInt(regionPx)); // хотя бы по 1 пикселю на ячейку
            var frac = new float[cells + 1];
            frac[0] = 0f;
            frac[cells] = 1f;
            int prevPx = 0;
            for (int i = 1; i < cells; i++)
            {
                float t = (float)i / cells + ((float)rng.NextDouble() - 0.5f) * (1f / cells) * LineJitter;
                int px = Mathf.Clamp(Mathf.RoundToInt(t * w), prevPx + 1, w - (cells - i)); // строго растёт, место для остатка
                frac[i] = (float)px / w;
                prevPx = px;
            }
            return frac;
        }

        // Доля [0..1] по осям → лок. координата, центр в 0: (f-0.5)*size.
        private static Vector2 Corner(float fxi, float fyi, Vector2 size)
            => new Vector2((fxi - 0.5f) * size.x, (fyi - 0.5f) * size.y);

        // Жёсткий блок-квад: 4 вершины несут ОДИН центроид и ОДИН рандом → блок летит/крутится как целое.
        private static void AddBlock(
            float fx0, float fy0, float fx1, float fy1, Vector2 size, Rect uvRect,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> centroids, List<Color> colors, List<int> tris,
            System.Random rng)
        {
            Vector2 bl = Corner(fx0, fy0, size);
            Vector2 br = Corner(fx1, fy0, size);
            Vector2 tr = Corner(fx1, fy1, size);
            Vector2 tl = Corner(fx0, fy1, size);
            Vector2 centroid = (bl + tr) * 0.5f;

            var rand = new Color(
                (float)rng.NextDouble(),  // r = speed
                (float)rng.NextDouble(),  // g = spin
                (float)rng.NextDouble(),  // b = dir jitter
                1f);

            int start = verts.Count;
            AddVertex(bl, fx0, fy0, centroid, rand, uvRect, verts, uvs, centroids, colors);
            AddVertex(br, fx1, fy0, centroid, rand, uvRect, verts, uvs, centroids, colors);
            AddVertex(tr, fx1, fy1, centroid, rand, uvRect, verts, uvs, centroids, colors);
            AddVertex(tl, fx0, fy1, centroid, rand, uvRect, verts, uvs, centroids, colors);
            tris.Add(start);     tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start);     tris.Add(start + 2); tris.Add(start + 3);
        }

        private static void AddVertex(
            Vector2 pos, float fu, float fv, Vector2 centroid, Color rand, Rect uvRect,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> centroids, List<Color> colors)
        {
            verts.Add(new Vector3(pos.x, pos.y, 0f));
            uvs.Add(new Vector2(uvRect.xMin + fu * uvRect.width, uvRect.yMin + fv * uvRect.height));
            centroids.Add(centroid);
            colors.Add(rand);
        }
    }
}
