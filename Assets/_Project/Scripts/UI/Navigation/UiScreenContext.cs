using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Общий контекст, который навигатор передаёт экрану в <see cref="UiScreen.Build"/>: корень слоя экранов
    /// и локализатор. Специфичные зависимости (VM, UXML-шаблоны) экран-обёртка несёт сама — их даёт создатель
    /// экрана (напр. <c>MenuRouter</c>, у которого VM в конструкторе). Здесь — только то, что нужно ВСЕМ экранам.
    /// <para>Тонкий по замыслу: расширяется по мере надобности (Ф2+), а не наполняется впрок.</para>
    /// </summary>
    public sealed class UiScreenContext
    {
        /// <summary>Корень слоя экранов навигатора (куда добавляются <see cref="UiScreen.Root"/>).</summary>
        public VisualElement ScreensLayer { get; }

        /// <summary>Локализатор: ключ → строка (RU-фолбэк — на стороне вызывающего). Может быть null в тестах.</summary>
        public Func<string, string> Localize { get; }

        public UiScreenContext(VisualElement screensLayer, Func<string, string> localize = null)
        {
            ScreensLayer = screensLayer;
            Localize = localize;
        }
    }
}
