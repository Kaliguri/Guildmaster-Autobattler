using Guildmaster.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Контракт компенсации зума для мирового UI (решение <c>2026-07-31/69</c>): надголовные бары и боевые
    /// цифры держат ПОСТОЯННЫЙ экранный размер, зум камеры на них не влияет. Инвариант живёт между тремя
    /// файлами — <c>ConstantScreenScale</c> (формула + бары), <c>FloatingText</c> (второй потребитель той же
    /// формулы) и ортографическая боевая камера, — поэтому держит его тест, а не комментарий: второй
    /// потребитель, посчитавший множитель по-своему, разъехался бы с первым молча.
    /// </summary>
    public sealed class ConstantScreenScaleTests
    {
        private static Camera MakeOrtho(float size)
        {
            var cam = new GameObject("TestCam").AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = size;
            return cam;
        }

        private static void Kill(Camera cam)
        {
            if (cam != null) Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void Экранный_размер_не_зависит_от_зума()
        {
            // Экранный размер объекта у ортокамеры ∝ worldScale / orthographicSize. Компенсация обязана
            // держать это произведение постоянным на всём диапазоне зума — иначе бар «дышит» с камерой.
            const float reference = 5f;
            Camera cam = MakeOrtho(reference);
            try
            {
                float baseline = ConstantScreenScale.ZoomFactor(cam, reference) / reference;

                foreach (float size in new[] { 3f, 4.2f, 5f, 8f, 14f })
                {
                    cam.orthographicSize = size;
                    float onScreen = ConstantScreenScale.ZoomFactor(cam, reference) / size;
                    Assert.That(onScreen, Is.EqualTo(baseline).Within(1e-5f),
                                $"на зуме {size} экранный размер уехал: компенсация не держит");
                }
            }
            finally { Kill(cam); }
        }

        [Test]
        public void На_опорном_зуме_множитель_единица()
        {
            // Опорный размер — это калибровка: при нём объект виден в свой АВТОРСКИЙ масштаб из префаба.
            Camera cam = MakeOrtho(6.5f);
            try { Assert.That(ConstantScreenScale.ZoomFactor(cam, 6.5f), Is.EqualTo(1f).Within(1e-6f)); }
            finally { Kill(cam); }
        }

        [Test]
        public void Без_камеры_и_на_перспективе_компенсации_нет()
        {
            // Формула выведена для орто; на перспективе и без камеры молча вернуть «как есть» безопаснее,
            // чем схлопнуть UI в ноль. Ноль/минус в опорном размере — та же дорога.
            Assert.That(ConstantScreenScale.ZoomFactor(null, 5f), Is.EqualTo(1f));

            Camera cam = MakeOrtho(9f);
            try
            {
                cam.orthographic = false;
                Assert.That(ConstantScreenScale.ZoomFactor(cam, 5f), Is.EqualTo(1f), "перспектива посчиталась по орто-формуле");

                cam.orthographic = true;
                Assert.That(ConstantScreenScale.ZoomFactor(cam, 0f),  Is.EqualTo(1f), "нулевой опорный размер дал деление на ноль");
                Assert.That(ConstantScreenScale.ZoomFactor(cam, -1f), Is.EqualTo(1f));
            }
            finally { Kill(cam); }
        }
    }
}
