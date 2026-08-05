using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Настройки дисплея за швом: разрешение, режим окна, частота обновления. Игровой код не дёргает
    /// <see cref="Screen"/> напрямую — ровно как звук ходит через <c>IAudioService</c>.
    /// <para><b>Владелец факта — наш файл</b> <c>Local/machine</c>, а не реестр Windows. Unity сохраняет
    /// разрешение сам (<c>Screenmanager Resolution Width/Height</c>) и применяет его ещё до первой сцены;
    /// поэтому мы применяем своё поверх на старте сессии, и реестр становится следствием, а не вторым
    /// владельцем.</para>
    /// </summary>
    public interface IDisplayService
    {
        int        Width  { get; }
        int        Height { get; }
        WindowMode Mode   { get; }

        /// <summary>Текущая частота обновления. Осмысленна только в <see cref="WindowMode.ExclusiveFullscreen"/>.</summary>
        RefreshRate RefreshRate { get; }

        /// <summary>
        /// Можно ли сейчас выбирать частоту обновления. <b>false</b> во всех режимах, кроме эксклюзивного
        /// полноэкранного — там её задаёт композитор рабочего стола, и выбор в UI надо гасить, а не
        /// показывать неработающим.
        /// </summary>
        bool RefreshRateSelectable { get; }

        /// <summary>Разрешения, которые поддерживает монитор.</summary>
        IReadOnlyList<Resolution> AvailableResolutions { get; }

        /// <summary>Частоты, доступные для указанного разрешения (без дублей).</summary>
        IReadOnlyList<RefreshRate> RefreshRatesFor(int width, int height);

        /// <summary>
        /// Синхронизация и потолок кадров в применённом виде. <b>Владелец — этот сервис, а не уровень
        /// качества:</b> в <c>QualitySettings</c> у каждого уровня своё <c>vSyncCount</c>, и появись у нас
        /// переключатель качества, игрок менял бы синхронизацию, выбирая «покрасивее».
        /// </summary>
        FramePacing Pacing { get; }

        void SetResolution(int width, int height);
        void SetMode(WindowMode mode);
        void SetRefreshRate(RefreshRate rate);

        /// <summary>Включить или выключить синхронизацию с развёрткой.</summary>
        void SetVSync(bool enabled);

        /// <summary>
        /// Задать потолок кадров, <see cref="FramePacing.Unlimited"/> — снять. Применится только при
        /// выключенной синхронизации; ниже <see cref="FramePacing.MinCap"/> не опускается.
        /// </summary>
        void SetFrameRateCap(int framesPerSecond);

        /// <summary>Вернуть «как у монитора»: нативное разрешение, наибольшая частота, окно без рамок.</summary>
        void ResetToNative();

        /// <summary>Записать выбор на диск. Применение происходит сразу, персист — по этому вызову.</summary>
        void Save();

        event Action Changed;
    }
}
