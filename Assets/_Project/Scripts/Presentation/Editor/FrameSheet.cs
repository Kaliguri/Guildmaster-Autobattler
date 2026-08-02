using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Editor
{
    /// <summary>
    /// Склейка последовательности кадров в одну картинку: контактный лист (сеткой) или луковая кожура
    /// (наложением в одну ячейку). Источник кадров классу безразличен — он получает готовые пиксели.
    /// </summary>
    /// <remarks>
    /// <para>Зачем существует. Агент не воспроизводит видео: читаются картинки, а MP4 и GIF для него в
    /// лучшем случае один кадр. Раскадровка — не эрзац записи, а другой прибор: она отвечает не
    /// «красиво ли», а «сыграло, не сыграло или сыграло не в том кадре». Запись остаётся человеку.</para>
    /// <para>Живёт у показа, а не в лаборатории анимаций, где была написана: потребителей стало двое —
    /// клип на риге и живой вид игры, — и второй к анимациям отношения не имеет. Владельцем в таком
    /// случае должен быть общий предок обоих, иначе владелец получается случайный.</para>
    /// <para><b>Подписи тиков здесь НЕ рисуются.</b> Нарисовать текст в <see cref="Texture2D"/> без
    /// шрифта — значит городить свой мини-растр цифр, и он же станет первым, что сломается при смене
    /// размера ячейки. Соответствие «кадр → тик» отдаётся числом в ответе инструмента: порядок чтения
    /// листа известен (слева направо, сверху вниз), а число не мылится и не обрезается.</para>
    /// </remarks>
    public static class FrameSheet
    {
        /// <summary>Как склеивать и чем размечать. Значения по умолчанию — те же, что были в лаборатории анимаций.</summary>
        public sealed class Options
        {
            /// <summary>Колонок в контактном листе. Меньше единицы — будет одна.</summary>
            public int Columns = 6;

            /// <summary>Заливка фона: и подложка листа, и то, относительно чего считается силуэт в луковой кожуре.</summary>
            public Color Background = new Color(0.10f, 0.10f, 0.12f, 1f);

            /// <summary>Разделитель между ячейками.</summary>
            public Color Divider = new Color(0.25f, 0.25f, 0.30f, 1f);

            /// <summary>Полоса поверх ячейки, на которую пришлось событие.</summary>
            public Color EventMarker = new Color(0.85f, 0.65f, 0.25f, 1f);
        }

        /// <summary>
        /// Кадры сеткой, читается как раскадровка — слева направо, сверху вниз.
        /// </summary>
        /// <param name="frames">Кадры, каждый размером <paramref name="cell"/> × <paramref name="cell"/>.</param>
        /// <param name="cell">Сторона ячейки в пикселях.</param>
        /// <param name="markedFrames">Индексы кадров, на которые пришлось событие; <c>null</c> — не размечать.</param>
        public static Texture2D ComposeContactSheet(List<Color[]> frames, int cell, Options options,
                                                    int[] markedFrames = null)
        {
            options ??= new Options();
            int columns = Mathf.Clamp(options.Columns, 1, Mathf.Max(1, frames.Count));
            int rows = Mathf.CeilToInt(frames.Count / (float)columns);

            var sheet = NewSheet(columns * cell, rows * cell, options.Background);

            for (int i = 0; i < frames.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                // Texture2D origin is bottom-left; rows should read top-down like a storyboard.
                int x = column * cell;
                int y = (rows - 1 - row) * cell;
                sheet.SetPixels(x, y, cell, cell, frames[i]);

                DrawVerticalLine(sheet, x, y, cell, options.Divider);
                if (markedFrames != null && System.Array.IndexOf(markedFrames, i) >= 0)
                    DrawHorizontalLine(sheet, x, y + cell - 3, cell, 3, options.EventMarker);
            }

            sheet.Apply();
            return sheet;
        }

        /// <summary>
        /// Кадры наложением в одну ячейку: старые позы бледные, последняя в полную силу. Читается
        /// движение — дуга, разгон, перелёт, — которое в сетке рассыпано по клеткам.
        /// </summary>
        public static Texture2D ComposeOnionSkin(List<Color[]> frames, int cell, Options options)
        {
            options ??= new Options();
            var sheet = NewSheet(cell, cell, options.Background);
            var canvas = sheet.GetPixels();
            var background = options.Background;

            for (int f = 0; f < frames.Count; f++)
            {
                // Older poses stay faint, the last one lands at full strength.
                float weight = Mathf.Lerp(0.18f, 1f, frames.Count == 1 ? 1f : f / (float)(frames.Count - 1));
                var frame = frames[f];
                for (int p = 0; p < canvas.Length; p++)
                {
                    var c = frame[p];
                    // The preview clears to Background, so anything that differs from it is the silhouette.
                    float ink = Mathf.Max(Mathf.Abs(c.r - background.r), Mathf.Max(Mathf.Abs(c.g - background.g), Mathf.Abs(c.b - background.b)));
                    if (ink < 0.02f) continue;
                    canvas[p] = Color.Lerp(canvas[p], c, weight);
                }
            }

            sheet.SetPixels(canvas);
            sheet.Apply();
            return sheet;
        }

        /// <summary>Снять пиксели с текстуры рендера. Возвращённый массив — один кадр для склейки.</summary>
        public static Color[] ReadBack(RenderTexture rt, int width, int height)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var pixels = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>Пустой холст, залитый цветом. Point-фильтрация: лист смотрят попиксельно.</summary>
        public static Texture2D NewSheet(int width, int height, Color fill)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            tex.SetPixels(pixels);
            return tex;
        }

        private static void DrawVerticalLine(Texture2D tex, int x, int y, int height, Color color)
        {
            if (x <= 0) return;
            for (int i = 0; i < height; i++) tex.SetPixel(x, y + i, color);
        }

        private static void DrawHorizontalLine(Texture2D tex, int x, int y, int width, int thickness, Color color)
        {
            for (int t = 0; t < thickness; t++)
                for (int i = 0; i < width; i++)
                    tex.SetPixel(x + i, y + t, color);
        }
    }
}
