using System;
using System.Collections.Generic;
using Guildmaster.Core.Settings;
using UnityEngine;

namespace Guildmaster.UI
{
    /// <summary>
    /// ViewModel экрана настроек — посредник между моделью и View (UXML). Моделей две:
    /// <see cref="ISettingsService"/> (звук и геймплей, едут в облако) и <see cref="IDisplayService"/>
    /// (дисплей, машинно-локальный). Экран один, поэтому VM одна — но хранилища за ней разные.
    /// POCO (создаётся DI, НЕ MonoBehaviour) — тестируется без сцены. Держит baseline-снапшот для Cancel:
    /// <see cref="BeginEdit"/> запоминает текущее, сеттеры применяют живьём (слышно при драге и видно
    /// при смене режима окна), <see cref="Cancel"/> откатывает, <see cref="Save"/> фиксирует.
    /// <para>Подписи здесь не собираются: VM отдаёт данные (режимы, разрешения, частоты), а текст к ним
    /// подставляет роутер через локализацию.</para>
    /// </summary>
    public sealed class SettingsViewModel
    {
        private readonly ISettingsService _settings;
        private readonly IDisplayService  _display;
        private AudioVolumeSettings _baseline;
        private GameplaySettings _baselineGameplay;
        private (int Width, int Height, WindowMode Mode, RefreshRate Rate) _baselineDisplay;

        public SettingsViewModel(ISettingsService settings, IDisplayService display)
        {
            _settings = settings;
            _display  = display;
        }

        public float Master => _settings.Audio.Master;
        public float Music => _settings.Audio.Music;
        public float Sfx => _settings.Audio.Sfx;

        public bool CardAnimations => _settings.Gameplay.CardAnimations;
        public bool CardAttackAnimation => _settings.Gameplay.CardAttackAnimation;
        public bool AlwaysDetailedTooltips => _settings.Gameplay.AlwaysDetailedTooltips;

        /// <summary>Поднимается при изменении значений (для обновления слайдеров/подписей).</summary>
        public event Action Changed
        {
            add => _settings.Changed += value;
            remove => _settings.Changed -= value;
        }

        // ── Дисплей (машинно-локальный, мимо Steam Cloud) ────────────────────

        /// <summary>Режимы окна в порядке показа. Подписи — за роутером (loc).</summary>
        public static readonly WindowMode[] WindowModes =
        {
            WindowMode.BorderlessWindow,
            WindowMode.ExclusiveFullscreen,
            WindowMode.Windowed,
        };

        public WindowMode WindowMode => _display.Mode;

        public int WindowModeIndex => Array.IndexOf(WindowModes, _display.Mode);

        /// <summary>
        /// Разрешения без дублей. <c>Screen.resolutions</c> отдаёт по строке на КАЖДУЮ пару
        /// «разрешение + частота», поэтому 1920x1080 приходит столько раз, сколько монитор знает частот.
        /// </summary>
        public IReadOnlyList<(int Width, int Height)> Resolutions
        {
            get
            {
                var list = new List<(int Width, int Height)>();
                foreach (Resolution r in _display.AvailableResolutions)
                    if (!list.Contains((r.width, r.height))) list.Add((r.width, r.height));
                return list;
            }
        }

        public int ResolutionIndex
        {
            get
            {
                IReadOnlyList<(int Width, int Height)> list = Resolutions;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Width == _display.Width && list[i].Height == _display.Height) return i;
                return -1;
            }
        }

        /// <summary>Частоты, доступные для текущего разрешения.</summary>
        public IReadOnlyList<RefreshRate> RefreshRates => _display.RefreshRatesFor(_display.Width, _display.Height);

        public int RefreshRateIndex
        {
            get
            {
                IReadOnlyList<RefreshRate> list = RefreshRates;
                for (int i = 0; i < list.Count; i++)
                    if (Math.Abs(list[i].value - _display.RefreshRate.value) < 0.01) return i;
                return -1;
            }
        }

        /// <summary>
        /// Можно ли выбирать частоту обновления. Вне эксклюзивного полноэкранного — нельзя: её держит
        /// композитор рабочего стола, и список надо гасить, а не показывать неработающим.
        /// </summary>
        public bool RefreshRateSelectable => _display.RefreshRateSelectable;

        public void SetWindowMode(int index)
        {
            if (index < 0 || index >= WindowModes.Length) return;
            _display.SetMode(WindowModes[index]);
        }

        public void SetResolution(int index)
        {
            IReadOnlyList<(int Width, int Height)> list = Resolutions;
            if (index < 0 || index >= list.Count) return;
            _display.SetResolution(list[index].Width, list[index].Height);
        }

        public void SetRefreshRate(int index)
        {
            IReadOnlyList<RefreshRate> list = RefreshRates;
            if (index < 0 || index >= list.Count) return;
            _display.SetRefreshRate(list[index]);
        }

        /// <summary>Поднимается при изменении настроек дисплея (список частот зависит от разрешения).</summary>
        public event Action DisplayChanged
        {
            add => _display.Changed += value;
            remove => _display.Changed -= value;
        }

        /// <summary>Запомнить текущее состояние как точку отката (при открытии экрана).</summary>
        public void BeginEdit()
        {
            _baseline = _settings.Audio;
            _baselineGameplay = _settings.Gameplay;
            _baselineDisplay = (_display.Width, _display.Height, _display.Mode, _display.RefreshRate);
        }

        public void SetMaster(float v) => _settings.SetMasterVolume(v);
        public void SetMusic(float v) => _settings.SetMusicVolume(v);
        public void SetSfx(float v) => _settings.SetSfxVolume(v);

        public void SetCardAnimations(bool v) => _settings.SetCardAnimations(v);
        public void SetCardAttackAnimation(bool v) => _settings.SetCardAttackAnimation(v);
        public void SetAlwaysDetailedTooltips(bool v) => _settings.SetAlwaysDetailedTooltips(v);

        /// <summary>
        /// Есть ли правки, которых нет на диске, — то, о чём предупреждают на выходе.
        /// </summary>
        /// <remarks>
        /// Считается сравнением с точкой отката, а не отдельным флагом «трогали»: игрок, вернувший
        /// ползунок туда, откуда начал, ничего не менял, и спрашивать его не о чем. Флаг такой разницы
        /// не видит и превращает предупреждение в шум, который перестают читать.
        /// </remarks>
        public bool HasUnsavedChanges
        {
            get
            {
                AudioVolumeSettings audio = _settings.Audio;
                GameplaySettings play = _settings.Gameplay;

                bool audioSame = Mathf.Approximately(audio.Master, _baseline.Master)
                                 && Mathf.Approximately(audio.Music, _baseline.Music)
                                 && Mathf.Approximately(audio.Sfx, _baseline.Sfx);

                bool playSame = play.CardAnimations == _baselineGameplay.CardAnimations
                                && play.CardAttackAnimation == _baselineGameplay.CardAttackAnimation
                                && play.AlwaysDetailedTooltips == _baselineGameplay.AlwaysDetailedTooltips;

                bool displaySame = _display.Width == _baselineDisplay.Width
                                   && _display.Height == _baselineDisplay.Height
                                   && _display.Mode == _baselineDisplay.Mode
                                   && _display.RefreshRate.Equals(_baselineDisplay.Rate);

                return !(audioSame && playSame && displaySame);
            }
        }

        /// <summary>Сохранить на диск и обновить точку отката. Два хранилища — два вызова.</summary>
        public void Save()
        {
            _settings.Save();
            _display.Save();
            _baseline = _settings.Audio;
            _baselineGameplay = _settings.Gameplay;
            _baselineDisplay = (_display.Width, _display.Height, _display.Mode, _display.RefreshRate);
        }

        /// <summary>Откатить к состоянию на момент <see cref="BeginEdit"/> (и переприменить в аудио).</summary>
        public void Cancel()
        {
            _settings.SetMasterVolume(_baseline.Master);
            _settings.SetMusicVolume(_baseline.Music);
            _settings.SetSfxVolume(_baseline.Sfx);
            _settings.SetCardAnimations(_baselineGameplay.CardAnimations);
            _settings.SetCardAttackAnimation(_baselineGameplay.CardAttackAnimation);
            _settings.SetAlwaysDetailedTooltips(_baselineGameplay.AlwaysDetailedTooltips);

            // Дисплей откатываем тоже — и это здесь не формальность: игрок мог выбрать режим, в котором
            // изображение пропало, и «Отмена» остаётся единственным способом вернуться.
            _display.SetMode(_baselineDisplay.Mode);
            _display.SetResolution(_baselineDisplay.Width, _baselineDisplay.Height);
            _display.SetRefreshRate(_baselineDisplay.Rate);
        }

        /// <summary>Вернуть к начальным, применить, но не сохранять. Для дисплея начальное = «как у монитора».</summary>
        public void ResetToDefaults()
        {
            _settings.ResetToDefaults();
            _display.ResetToNative();
        }
    }
}
