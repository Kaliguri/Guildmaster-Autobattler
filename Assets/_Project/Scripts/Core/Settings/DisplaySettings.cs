using System;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Режим окна — три варианта, которые игрок реально выбирает. Платформенный
    /// <c>FullScreenMode.MaximizedWindow</c> (macOS) наружу не выводим: мы на Windows, а лишний пункт в
    /// списке пришлось бы объяснять.
    /// </summary>
    public enum WindowMode
    {
        /// <summary>Эксклюзивный полноэкранный. <b>Единственный режим, где можно менять частоту обновления.</b></summary>
        ExclusiveFullscreen,

        /// <summary>Полноэкранное окно без рамок. Дружелюбно к alt-tab, поэтому режим по умолчанию.</summary>
        BorderlessWindow,

        /// <summary>Обычное окно с рамкой.</summary>
        Windowed,
    }

    /// <summary>
    /// Машинно-локальные настройки дисплея (ТЗ [[save-system]] §3). Лежат в <c>Local/machine</c> и
    /// <b>не синхронизируются</b> Steam Cloud: они описывают компьютер, а не игрока.
    /// <para>Поля <b>nullable намеренно</b>: «не задано» означает «взять с монитора» — нативное разрешение
    /// и наибольшую частоту. Записать сюда вычисленные значения при первом запуске было бы ошибкой:
    /// игрок, сменивший монитор, остался бы с разрешением старого.</para>
    /// </summary>
    [Serializable]
    [SaveSchema(1)]
    public sealed class DisplaySettings
    {
        public int? Width;
        public int? Height;

        public WindowMode? Mode;

        /// <summary>
        /// Частота обновления рациональным числом, как её отдаёт Unity: 60 Гц = 60/1, а 59.94 Гц —
        /// это 60000/1001, и в double такое не уложить без потери точности.
        /// </summary>
        public uint? RefreshNumerator;

        public uint? RefreshDenominator;

        /// <summary>
        /// Синхронизация кадров с развёрткой. «Не задано» читается как <b>включена</b> — см.
        /// <see cref="FramePacing"/>, там же причина, почему это не default(bool).
        /// </summary>
        public bool? VSync;

        /// <summary>
        /// Потолок кадров в секунду, <see cref="FramePacing.Unlimited"/> — без потолка. Смысл имеет
        /// только при выключенной <see cref="VSync"/>: движок игнорирует потолок, пока синхронизация
        /// включена.
        /// </summary>
        public int? FrameRateCap;
    }
}
