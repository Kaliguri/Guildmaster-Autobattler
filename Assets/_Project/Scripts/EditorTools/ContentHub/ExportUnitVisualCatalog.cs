#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Каталог idle-визуалов юнитов в масштабе гизмо <c>_recommendedHeight</c>/<c>_recommendedWidth</c>.
    /// Меню: Alebardium/Visuals/Export Unit Visual Catalog.
    /// </summary>
    public static class ExportUnitVisualCatalog
    {
        const string OutputDir = "Assets/_Project/Art/Dev";
        const string OutputFile = "UnitVisualCatalog.png";
        const int Cols = 4;
        const int Pad = 16;
        const int LabelH = 36;
        const float PixelsPerMeter = 120f; // 1.7m → ~204 px tall figure
        const int CellPad = 10;

        [MenuItem("Alebardium/Visuals/Export Unit Visual Catalog", priority = 502)]
        public static void Export()
        {
            EnsureFolder(OutputDir);

            var entries = CollectEntries();
            if (entries.Count == 0)
            {
                Debug.LogError("[UnitVisualCatalog] No units found.");
                return;
            }

            float maxH = entries.Max(e => e.RecH);
            // Cell width: RecW guide + room for wide art (weapons). Fixed multiplier keeps grid tidy.
            float maxW = Mathf.Max(entries.Max(e => e.RecW) * 2.2f, entries.Max(e => e.RecH) * 1.1f);
            int figureMaxH = Mathf.CeilToInt(maxH * PixelsPerMeter);
            int figureMaxW = Mathf.CeilToInt(maxW * PixelsPerMeter);
            int cellW = figureMaxW + CellPad * 2;
            int cellH = figureMaxH + LabelH + CellPad * 2;

            int rows = (entries.Count + Cols - 1) / Cols;
            int texW = Cols * cellW + Pad * 2;
            int texH = rows * cellH + Pad * 2 + 44;

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
            };
            Fill(tex, new Color(0.12f, 0.13f, 0.16f, 1f));
            StampText(tex, "UNIT VISUAL CATALOG - SUPER ALPHA  (scale = recommended gizmo)", Pad, texH - 30,
                new Color(0.95f, 0.92f, 0.8f, 1f));

            var unlocked = UnlockReadable(entries);
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    int col = i % Cols;
                    int row = i / Cols;
                    int cellX = Pad + col * cellW;
                    int cellY = texH - Pad - 44 - (row + 1) * cellH;

                    FillRect(tex, cellX + 2, cellY + 2, cellW - 4, cellH - 4, new Color(0.17f, 0.18f, 0.21f, 1f));

                    var e = entries[i];
                    int figH = Mathf.Max(1, Mathf.RoundToInt(e.RecH * PixelsPerMeter));
                    int figW = Mathf.Max(1, Mathf.RoundToInt(e.RecW * PixelsPerMeter));
                    int figX = cellX + (cellW - figW) / 2;
                    int figY = cellY + LabelH + CellPad;

                    // Green recommended frame (gizmo growth × width at feet)
                    var green = new Color(0.35f, 0.95f, 0.55f, 0.95f);
                    DrawRectOutline(tex, figX, figY, figW, figH, green);

                    // Sprite: opaque content scaled to RecH, bottom on feet line, centered in cell
                    if (e.Sprite != null)
                        BlitSpriteFit(tex, e.Sprite, cellX + CellPad, figY, cellW - CellPad * 2, figH, e.Tint);

                    StampText(tex, e.Name.ToUpperInvariant(), cellX + 8, cellY + 20, new Color(0.95f, 0.92f, 0.8f, 1f));
                    StampText(tex, $"{e.Kind}  H={e.RecH:0.##} W={e.RecW:0.##}", cellX + 8, cellY + 8,
                        new Color(0.6f, 0.72f, 0.65f, 1f));
                }
            }
            finally
            {
                RestoreReadable(unlocked);
            }

            tex.Apply();
            string outPath = $"{OutputDir}/{OutputFile}";
            File.WriteAllBytes(Path.GetFullPath(outPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(outPath);

            var catalogImp = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (catalogImp != null)
            {
                catalogImp.textureType = TextureImporterType.Default;
                catalogImp.filterMode = FilterMode.Bilinear;
                catalogImp.mipmapEnabled = false;
                catalogImp.SaveAndReimport();
            }

            Debug.Log($"[UnitVisualCatalog] Exported {entries.Count} units → {outPath}");
            EditorUtility.RevealInFinder(Path.GetFullPath(outPath));
        }

        static List<Entry> CollectEntries()
        {
            var list = new List<Entry>();
            // Палитра проекта — тот же снимок, что читают бой и карта: каталог обязан показывать РОВНО
            // игровой цвет тела, иначе он врёт именно там, где на него и смотрят.
            var palette = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(
                "Assets/_Project/ScriptableObjects/Configs/GuildmasterPalette.asset");
            foreach (var folder in new[]
                     {
                         "Assets/_Project/ScriptableObjects/Relics",
                         "Assets/_Project/ScriptableObjects/Enemies",
                     })
            {
                foreach (var guid in AssetDatabase.FindAssets("t:UnitData", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var unit = AssetDatabase.LoadAssetAtPath<UnitData>(path);
                    if (unit == null || unit.ViewPrefab == null) continue;

                    var uv = unit.ViewPrefab.GetComponent<Guildmaster.Presentation.UnitView>();
                    float recH = 1.7f, recW = 0.7f;
                    Sprite sprite = null;
                    if (uv != null)
                    {
                        var so = new SerializedObject(uv);
                        recH = so.FindProperty("_recommendedHeight").floatValue;
                        recW = so.FindProperty("_recommendedWidth").floatValue;
                        var body = unit.ViewPrefab.transform.Find("Visual Sprites/Body");
                        if (body != null)
                        {
                            var sr = body.GetComponent<SpriteRenderer>();
                            if (sr != null) sprite = sr.sprite;
                        }
                    }

                    if (sprite == null) continue;

                    list.Add(new Entry
                    {
                        Name = unit.name,
                        Kind = path.Contains("/Relics/") ? "RELIC" : "ENEMY",
                        Sprite = sprite,
                        Tint = UnitColorRoles.Body(palette, unit.VfxTone),
                        RecH = Mathf.Max(0.01f, recH),
                        RecW = Mathf.Max(0.01f, recW),
                    });
                }
            }

            return list.OrderBy(e => e.Kind).ThenBy(e => e.Name).ToList();
        }

        static HashSet<string> UnlockReadable(List<Entry> entries)
        {
            var unlocked = new HashSet<string>();
            foreach (var e in entries)
            {
                string path = AssetDatabase.GetAssetPath(e.Sprite);
                if (string.IsNullOrEmpty(path) || unlocked.Contains(path)) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && !imp.isReadable)
                {
                    imp.isReadable = true;
                    imp.SaveAndReimport();
                    unlocked.Add(path);
                }
            }

            return unlocked;
        }

        static void RestoreReadable(HashSet<string> unlocked)
        {
            foreach (var path in unlocked)
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                imp.isReadable = false;
                imp.SaveAndReimport();
            }
        }

        static void BlitSpriteFit(Texture2D dest, Sprite sp, int x, int y, int boxW, int boxH, Color tint)
        {
            string path = AssetDatabase.GetAssetPath(sp);
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (src == null) return;

            var r = sp.rect;
            int sw = (int)r.width;
            int sh = (int)r.height;
            var px = src.GetPixels((int)r.x, (int)r.y, sw, sh);

            // Opaque content bounds — scale THAT to recommended height (ignore sheet padding).
            int minX = sw, minY = sh, maxX = -1, maxY = -1;
            for (int yy = 0; yy < sh; yy++)
            for (int xx = 0; xx < sw; xx++)
            {
                if (px[yy * sw + xx].a <= 0.1f) continue;
                if (xx < minX) minX = xx;
                if (yy < minY) minY = yy;
                if (xx > maxX) maxX = xx;
                if (yy > maxY) maxY = yy;
            }

            if (maxY < minY) { minX = 0; minY = 0; maxX = sw - 1; maxY = sh - 1; }
            int cw = maxX - minX + 1;
            int ch = maxY - minY + 1;

            float scale = (float)boxH / ch;
            int dw = Mathf.Max(1, Mathf.RoundToInt(cw * scale));
            int dh = Mathf.Max(1, Mathf.RoundToInt(ch * scale));
            int ox = x + (boxW - dw) / 2;
            int oy = y; // feet / opaque bottom on bottom of gizmo

            for (int dy = 0; dy < dh; dy++)
            {
                int sy = minY + Mathf.Clamp((int)(dy / scale), 0, ch - 1);
                for (int dx = 0; dx < dw; dx++)
                {
                    int sx = minX + Mathf.Clamp((int)(dx / scale), 0, cw - 1);
                    var c = px[sy * sw + sx] * tint;
                    if (c.a < 0.05f) continue;
                    int pxX = ox + dx;
                    int pxY = oy + dy;
                    if ((uint)pxX >= (uint)dest.width || (uint)pxY >= (uint)dest.height) continue;
                    var dst = dest.GetPixel(pxX, pxY);
                    float a = c.a;
                    dest.SetPixel(pxX, pxY, new Color(
                        c.r * a + dst.r * (1 - a),
                        c.g * a + dst.g * (1 - a),
                        c.b * a + dst.b * (1 - a),
                        a + dst.a * (1 - a)));
                }
            }
        }

        static void DrawRectOutline(Texture2D tex, int x, int y, int w, int h, Color c)
        {
            for (int i = 0; i < w; i++)
            {
                Set(tex, x + i, y, c);
                Set(tex, x + i, y + h - 1, c);
            }

            for (int i = 0; i < h; i++)
            {
                Set(tex, x, y + i, c);
                Set(tex, x + w - 1, y + i, c);
            }
        }

        static void Set(Texture2D tex, int x, int y, Color c)
        {
            if ((uint)x >= (uint)tex.width || (uint)y >= (uint)tex.height) return;
            tex.SetPixel(x, y, c);
        }

        static readonly Dictionary<char, string[]> Font = BuildFont();

        static Dictionary<char, string[]> BuildFont()
        {
            var d = new Dictionary<char, string[]>();
            void G(char c, params string[] rows) => d[c] = rows;
            G(' ', ".....", ".....", ".....", ".....", ".....", ".....", ".....");
            G('-', ".....", ".....", ".....", "#####", ".....", ".....", ".....");
            G('=', ".....", ".....", "#####", ".....", "#####", ".....", ".....");
            G('.', ".....", ".....", ".....", ".....", ".....", ".#...", ".....");
            G(',', ".....", ".....", ".....", ".....", ".....", ".#...", "#....");
            G('(', "..##.", ".#...", "#....", "#....", "#....", ".#...", "..##.");
            G(')', ".##..", "...#.", "....#", "....#", "....#", "...#.", ".##..");
            G('/', "....#", "...#.", "..#..", ".#...", "#....", ".....", ".....");
            G('0', ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.");
            G('1', "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.");
            G('2', ".###.", "#...#", "....#", "..##.", ".#...", "#....", "#####");
            G('3', ".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###.");
            G('4', "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.");
            G('5', "#####", "#....", "####.", "....#", "....#", "#...#", ".###.");
            G('6', ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###.");
            G('7', "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...");
            G('8', ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.");
            G('9', ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###.");
            G('A', ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            G('B', "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.");
            G('C', ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.");
            G('D', "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.");
            G('E', "#####", "#....", "#....", "####.", "#....", "#....", "#####");
            G('F', "#####", "#....", "#....", "####.", "#....", "#....", "#....");
            G('G', ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###.");
            G('H', "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            G('I', ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###.");
            G('J', "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..");
            G('K', "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#");
            G('L', "#....", "#....", "#....", "#....", "#....", "#....", "#####");
            G('M', "#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#");
            G('N', "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#");
            G('O', ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            G('P', "####.", "#...#", "#...#", "####.", "#....", "#....", "#....");
            G('Q', ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
            G('R', "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#");
            G('S', ".####", "#....", "#....", ".###.", "....#", "....#", "####.");
            G('T', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
            G('U', "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            G('V', "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
            G('W', "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#");
            G('X', "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
            G('Y', "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
            G('Z', "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####");
            return d;
        }

        static void StampText(Texture2D dest, string text, int x, int y, Color color)
        {
            int cx = x;
            foreach (char ch in text.ToUpperInvariant())
            {
                if (!Font.TryGetValue(ch, out var rows))
                    Font.TryGetValue(' ', out rows);
                if (rows == null) { cx += 6; continue; }
                for (int row = 0; row < 7; row++)
                {
                    string line = rows[row];
                    for (int col = 0; col < 5; col++)
                    {
                        if (line[col] != '#') continue;
                        Set(dest, cx + col, y + (6 - row), color);
                    }
                }

                cx += 6;
            }
        }

        static void Fill(Texture2D tex, Color c)
        {
            tex.SetPixels(Enumerable.Repeat(c, tex.width * tex.height).ToArray());
        }

        static void FillRect(Texture2D tex, int x, int y, int w, int h, Color c)
        {
            for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Set(tex, xx, yy, c);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
                AssetDatabase.CreateFolder(parent, name);
        }

        sealed class Entry
        {
            public string Name;
            public string Kind;
            public Sprite Sprite;
            public Color Tint;
            public float RecH;
            public float RecW;
        }
    }
}
#endif
