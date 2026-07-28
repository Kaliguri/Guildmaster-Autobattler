#if UNITY_EDITOR
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Pixel drawing over a rendered frame, in world coordinates.
    ///
    /// Lives on its own because two tools draw over the same kind of picture — <see cref="RigProbe"/>
    /// marks the geometry of a single pose, <see cref="RigSweep"/> traces where a weapon travelled over a
    /// whole clip — and a second copy of the same plotting code is exactly the kind of duplicate that
    /// drifts: fix an off-by-one in one file and the other picture keeps lying.
    /// </summary>
    internal sealed class RigCanvas
    {
        readonly Texture2D _tex;
        readonly Camera _cam;
        readonly int _width;
        readonly int _height;

        public RigCanvas(Texture2D tex, Camera cam, int size) : this(tex, cam, size, size) { }

        public RigCanvas(Texture2D tex, Camera cam, int width, int height)
        {
            _tex = tex;
            _cam = cam;
            _width = width;
            _height = height;
        }

        /// <summary>
        /// World point to pixel. The viewport is the CAMERA's square render, so a canvas taller than it
        /// (a picture with a caption strip under the render) has to be told both sizes — mapping y by the
        /// full height would slide every mark off the body.
        /// </summary>
        public Vector2 ToPixels(Vector3 world)
        {
            var viewport = _cam.WorldToViewportPoint(world);
            return new Vector2(viewport.x * _width, viewport.y * _width);
        }

        public void Dot(Vector3 world, int radius, Color color)
        {
            var p = ToPixels(world);
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    Plot((int)p.x + dx, (int)p.y + dy, color);
                }
        }

        public void Line(Vector3 worldA, Vector3 worldB, Color color, int thickness)
        {
            var a = ToPixels(worldA);
            var b = ToPixels(worldB);
            int steps = (int)Vector2.Distance(a, b) * 2 + 2;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                for (int dx = -thickness; dx <= thickness; dx++)
                    for (int dy = -thickness; dy <= thickness; dy++)
                        Plot((int)p.x + dx, (int)p.y + dy, color);
            }
        }

        /// <summary>
        /// A filled triangle, blended over what is already there. Fans of these are how a swing becomes a
        /// ZONE — the thing an attack actually occupies — instead of a bundle of lines the eye has to
        /// integrate on its own.
        /// </summary>
        public void FillTriangle(Vector3 worldA, Vector3 worldB, Vector3 worldC, Color color)
        {
            var a = ToPixels(worldA);
            var b = ToPixels(worldB);
            var c = ToPixels(worldC);

            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, _width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, _width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, _height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, _height - 1);

            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-4f) return;

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(b, c, p) / area;
                    float w1 = Edge(c, a, p) / area;
                    float w2 = Edge(a, b, p) / area;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    Blend(x, y, color);
                }
        }

        static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        /// <summary>Alpha-blend one pixel. Overlapping fan slices must not stack into an opaque blob.</summary>
        public void Blend(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            var under = _tex.GetPixel(x, y);
            _tex.SetPixel(x, y, Color.Lerp(under, new Color(color.r, color.g, color.b, 1f), color.a));
        }

        public void Cross(Vector3 world, int radius, Color color)
        {
            var p = ToPixels(world);
            for (int i = -radius; i <= radius; i++)
            {
                Plot((int)p.x + i, (int)p.y + i, color);
                Plot((int)p.x + i, (int)p.y - i, color);
            }
        }

        public void Plot(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            _tex.SetPixel(x, y, color);
        }
    }
}
#endif
