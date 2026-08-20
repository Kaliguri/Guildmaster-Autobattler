using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Settings;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реализация <see cref="IDisplayService"/>. Персист — <see cref="ILocalSaveService"/>, ключ
    /// <c>machine</c>: файл лежит вне <c>Saves/</c> и в Steam Cloud не едет (ТЗ [[save-system]] §3).
    /// <para>Entry point: <see cref="Start"/> читает файл и применяет режим на старте сессии. Unity к
    /// этому моменту уже применил <b>своё</b> запомненное разрешение (реестр
    /// <c>Screenmanager Resolution Width/Height</c>) — он делает это до первой сцены. Мы применяем поверх,
    /// поэтому владельцем остаётся наш файл, а реестр становится его следствием. Видимого переключения при
    /// этом обычно нет: прошлый запуск записал в реестр ровно то, что мы применили.</para>
    /// <para><b>В редакторе режим не применяется.</b> <c>Screen.SetResolution</c> здесь переписывал бы
    /// Game view под разрешение монитора, ломая работу; настройки при этом читаются и отдаются как есть,
    /// чтобы UI можно было проверять в play mode. Пасовка кадров (<see cref="FramePacing"/>) — исключение:
    /// она применяется и в редакторе, см. <see cref="Apply"/>.</para>
    /// <para><b>Синхронизация кадров живёт здесь, а не в уровне качества.</b> В <c>QualitySettings</c>
    /// <c>vSyncCount</c> задан у каждого из шести уровней по-своему, и на активном (<c>Very Low</c>) он
    /// равен нулю. Оставить владельцем уровень значило бы, что игрок, выбравший «покрасивее», молча меняет
    /// и синхронизацию — один факт с двумя владельцами.</para>
    /// </summary>
    public sealed class DisplayService : IDisplayService, IStartable
    {
        private const string SaveKey = "machine";

        private readonly ILocalSaveService _save;

        private DisplaySettings _settings = new();
        private Resolution[]    _resolutions = Array.Empty<Resolution>();

        public DisplayService(ILocalSaveService save) => _save = save;

        public int         Width  { get; private set; }
        public int         Height { get; private set; }
        public WindowMode  Mode   { get; private set; } = WindowMode.BorderlessWindow;
        public RefreshRate RefreshRate { get; private set; }
        public FramePacing Pacing { get; private set; } = FramePacing.Resolve(null, null);

        public bool RefreshRateSelectable => Mode == WindowMode.ExclusiveFullscreen;

        public IReadOnlyList<Resolution> AvailableResolutions => _resolutions;

        public event Action Changed;

        void IStartable.Start()
        {
            _resolutions = Screen.resolutions;

            SaveLoadResult<DisplaySettings> loaded = _save.TryLoad<DisplaySettings>(SaveKey);
            if (loaded.IsBlocked)
                Debug.LogWarning($"[DisplayService] - настройки дисплея записаны версией " +
                                 $"{loaded.SavedGameVersion} ({loaded.Status}) — беру монитор как есть");

            _settings = loaded.IsOk ? loaded.Value : new DisplaySettings();

            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public IReadOnlyList<RefreshRate> RefreshRatesFor(int width, int height)
        {
            var rates = new List<RefreshRate>();
            foreach (Resolution r in _resolutions)
            {
                if (r.width != width || r.height != height) continue;
                if (!rates.Exists(existing => Same(existing, r.refreshRateRatio)))
                    rates.Add(r.refreshRateRatio);
            }
            return rates;
        }

        public void SetResolution(int width, int height)
        {
            _settings.Width  = width;
            _settings.Height = height;
            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public void SetMode(WindowMode mode)
        {
            _settings.Mode = mode;
            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public void SetRefreshRate(RefreshRate rate)
        {
            _settings.RefreshNumerator   = rate.numerator;
            _settings.RefreshDenominator = rate.denominator;
            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public void SetVSync(bool enabled)
        {
            _settings.VSync = enabled;
            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public void SetFrameRateCap(int framesPerSecond)
        {
            _settings.FrameRateCap = framesPerSecond;
            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public void ResetToNative()
        {
            _settings = new DisplaySettings();
            ResolveEffective();
            Apply();
            Changed?.Invoke();
        }

        public void Save() => _save.Save(SaveKey, _settings);

        /// <summary>
        /// Незаданное поле → взять с монитора. Именно поэтому вычисленные значения не пишутся в файл:
        /// «не задано» переживает смену монитора, а записанное однажды нативное разрешение — нет.
        /// </summary>
        private void ResolveEffective()
        {
            DisplayInfo display = Screen.mainWindowDisplayInfo;

            Width  = _settings.Width  ?? display.width;
            Height = _settings.Height ?? display.height;
            Mode   = _settings.Mode   ?? WindowMode.BorderlessWindow;

            // Эксклюзивный полноэкранный работает ТОЛЬКО с разрешениями из списка монитора: передать
            // неподдерживаемое — не ошибка, а тихая просадка производительности (док Screen.SetResolution).
            if (Mode == WindowMode.ExclusiveFullscreen && _resolutions.Length > 0 && !IsSupported(Width, Height))
            {
                Resolution closest = ClosestSupported(Width, Height);
                Debug.LogWarning($"[DisplayService] - {Width}x{Height} монитором не поддерживается, " +
                                 $"беру ближайшее {closest.width}x{closest.height}");
                Width  = closest.width;
                Height = closest.height;
            }

            RefreshRate = ResolveRefreshRate(display);
            Pacing      = FramePacing.Resolve(_settings.VSync, _settings.FrameRateCap);
        }

        private RefreshRate ResolveRefreshRate(DisplayInfo display)
        {
            if (_settings.RefreshNumerator.HasValue && _settings.RefreshDenominator.HasValue &&
                _settings.RefreshDenominator.Value != 0)
            {
                var requested = new RefreshRate
                {
                    numerator   = _settings.RefreshNumerator.Value,
                    denominator = _settings.RefreshDenominator.Value,
                };

                foreach (RefreshRate rate in RefreshRatesFor(Width, Height))
                    if (Same(rate, requested)) return requested;
            }

            // Не задано или монитор такого не умеет → наибольшая доступная для этого разрешения.
            RefreshRate best = default;
            foreach (RefreshRate rate in RefreshRatesFor(Width, Height))
                if (rate.value > best.value) best = rate;

            return best.value > 0 ? best : display.refreshRate;
        }

        private void Apply()
        {
            // Пасовка кадров применяется И в редакторе, в отличие от разрешения ниже: причина не трогать
            // редактор — перекроенный Game view, а к vSync она не относится. Наоборот, разработчик должен
            // видеть ту же плавность, что игрок, иначе микростаттер найдётся только в релизе.
            ApplyPacing();

            if (Application.isEditor) return; // иначе перекраивается Game view — см. комментарий класса

            // Значение вступает в силу в конце кадра: Screen.width сразу после вызова ещё старый,
            // поэтому источник правды для UI — наши поля, а не Screen.
            Screen.SetResolution(Width, Height, ToFullScreenMode(Mode), RefreshRate);
        }

        /// <summary>
        /// Записать выбор игрока поверх <c>QualitySettings</c>. <b>Зовётся при каждом применении, а не
        /// один раз на старте, намеренно:</b> <c>QualitySettings.SetQualityLevel</c> перезаписывает
        /// <c>vSyncCount</c> значением уровня, и наш владелец обязан вернуть своё после любой такой смены.
        /// </summary>
        private void ApplyPacing()
        {
            QualitySettings.vSyncCount = Pacing.VSync ? 1 : 0;

            // При vSyncCount > 0 движок игнорирует targetFrameRate; -1 означает «без потолка».
            Application.targetFrameRate = Pacing.FrameRateCap == FramePacing.Unlimited
                ? -1
                : Pacing.FrameRateCap;
        }

        private bool IsSupported(int width, int height)
        {
            foreach (Resolution r in _resolutions)
                if (r.width == width && r.height == height) return true;
            return false;
        }

        private Resolution ClosestSupported(int width, int height)
        {
            Resolution best = _resolutions[0];
            long bestDelta = long.MaxValue;

            foreach (Resolution r in _resolutions)
            {
                long delta = Math.Abs((long)r.width - width) + Math.Abs((long)r.height - height);
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = r;
            }
            return best;
        }

        private static FullScreenMode ToFullScreenMode(WindowMode mode) => mode switch
        {
            WindowMode.ExclusiveFullscreen => FullScreenMode.ExclusiveFullScreen,
            WindowMode.Windowed            => FullScreenMode.Windowed,
            _                              => FullScreenMode.FullScreenWindow,
        };

        // Частоты сравниваем по значению с допуском: 59.94 Гц приходит как 60000/1001, и точное
        // сравнение числителей развалилось бы на первом же таком мониторе.
        private static bool Same(RefreshRate a, RefreshRate b) => Math.Abs(a.value - b.value) < 0.01;
    }
}
