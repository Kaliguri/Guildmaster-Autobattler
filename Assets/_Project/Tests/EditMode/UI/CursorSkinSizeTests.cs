using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Скины курсора одного размера и с остриём в углу холста.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт между файлами, поэтому он тест, а не комментарий.</b> Скины собираются
    /// скриптом (<c>scripts/cursors-build.py</c>), но положить в папку картинку мимо скрипта может кто
    /// угодно, и разъехавшийся набор выглядит не поломкой, а «этот курсор какой-то жирный» — то есть
    /// живёт в игре месяцами.
    /// <para>Исходники Kenney расходятся почти вдвое при одинаковом холсте 32x32 (<c>pointer_a</c> —
    /// фигура 16x20, <c>pointer_toon_a</c> — 29x28), так что равнять по холсту нельзя: равняем по
    /// диагонали фигуры.</para>
    /// </remarks>
    public sealed class CursorSkinSizeTests
    {
        private const string Folder = "Assets/_Project/Art/UI/Cursors";

        /// <summary>Допуск на расхождение диагоналей. Процент — это доли пикселя на экране.</summary>
        private const float ToleranceRatio = 0.02f;

        /// <summary>Насколько далеко от угла холста разрешено лежать острию, долями стороны.</summary>
        private const float TipMargin = 0.06f;

        private static IEnumerable<string> SkinFiles()
        {
            if (!Directory.Exists(Folder)) yield break;
            foreach (string path in Directory.GetFiles(Folder, "cursor_*.png")) yield return path;
        }

        [Test]
        public void EverySkin_HasTheSameFigureDiagonal()
        {
            var diagonals = new Dictionary<string, float>();

            foreach (string path in SkinFiles())
            {
                Texture2D texture = Load(path);
                RectInt box = OpaqueBounds(texture);

                Assert.Greater(box.width, 0, $"{Path.GetFileName(path)}: картинка пустая");
                diagonals[Path.GetFileName(path)] = Mathf.Sqrt(box.width * box.width + box.height * box.height);

                Object.DestroyImmediate(texture);
            }

            if (diagonals.Count < 2) Assert.Pass("меньше двух скинов — равнять нечего");

            float min = float.MaxValue, max = float.MinValue;
            foreach (float value in diagonals.Values)
            {
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            float drift = (max - min) / max;
            Assert.LessOrEqual(drift, ToleranceRatio,
                $"скины разъехались по величине на {drift:P1}: {string.Join(", ", diagonals)}. " +
                "Пересобери набор: python scripts/cursors-build.py");
        }

        [Test]
        public void EverySkin_KeepsItsTipInTheCorner()
        {
            foreach (string path in SkinFiles())
            {
                Texture2D texture = Load(path);
                RectInt box = OpaqueBounds(texture);

                float offsetX = box.xMin / (float)texture.width;
                float offsetY = box.yMin / (float)texture.height;

                Assert.LessOrEqual(offsetX, TipMargin,
                    $"{Path.GetFileName(path)}: фигура отступает от левого края холста — " +
                    "остриё должно лежать в углу, иначе курсор указывает мимо");
                Assert.LessOrEqual(offsetY, TipMargin,
                    $"{Path.GetFileName(path)}: фигура отступает от верхнего края холста");

                Object.DestroyImmediate(texture);
            }
        }

        private static Texture2D Load(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            // Читаем файл с диска, а не ассет: у импортированной текстуры выключен Read/Write, и это
            // правильно — курсору он не нужен, а тесту хватает своей копии.
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        /// <summary>Границы непрозрачной части. Координаты — от левого ВЕРХНЕГО угла, как у холста.</summary>
        private static RectInt OpaqueBounds(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width, height = texture.height;

            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a <= 40) continue;

                    // GetPixels32 идёт снизу вверх, а холст мы меряем сверху — переворачиваем здесь,
                    // чтобы дальше все координаты были в одной системе.
                    int top = height - 1 - y;
                    if (x < minX)   minX = x;
                    if (x > maxX)   maxX = x;
                    if (top < minY) minY = top;
                    if (top > maxY) maxY = top;
                }
            }

            return maxX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}
