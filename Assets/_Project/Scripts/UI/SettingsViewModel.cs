using System;
using Guildmaster.Core.Settings;

namespace Guildmaster.UI
{
    /// <summary>
    /// ViewModel экрана настроек — посредник между <see cref="ISettingsService"/> (модель) и View (UXML).
    /// POCO (создаётся DI, НЕ MonoBehaviour) — тестируется без сцены. Держит baseline-снапшот для Cancel:
    /// <see cref="BeginEdit"/> запоминает текущее, сеттеры применяют живьём (слышно при драге),
    /// <see cref="Cancel"/> откатывает, <see cref="Save"/> фиксирует. Значения — [0..1].
    /// </summary>
    public sealed class SettingsViewModel
    {
        private readonly ISettingsService _settings;
        private AudioVolumeSettings _baseline;
        private GameplaySettings _baselineGameplay;

        public SettingsViewModel(ISettingsService settings) => _settings = settings;

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

        /// <summary>Запомнить текущее состояние как точку отката (при открытии экрана).</summary>
        public void BeginEdit()
        {
            _baseline = _settings.Audio;
            _baselineGameplay = _settings.Gameplay;
        }

        public void SetMaster(float v) => _settings.SetMasterVolume(v);
        public void SetMusic(float v) => _settings.SetMusicVolume(v);
        public void SetSfx(float v) => _settings.SetSfxVolume(v);

        public void SetCardAnimations(bool v) => _settings.SetCardAnimations(v);
        public void SetCardAttackAnimation(bool v) => _settings.SetCardAttackAnimation(v);
        public void SetAlwaysDetailedTooltips(bool v) => _settings.SetAlwaysDetailedTooltips(v);

        /// <summary>Сохранить на диск и обновить точку отката.</summary>
        public void Save()
        {
            _settings.Save();
            _baseline = _settings.Audio;
            _baselineGameplay = _settings.Gameplay;
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
        }

        /// <summary>Вернуть к начальным (дефолты GameConfig), применить, но не сохранять.</summary>
        public void ResetToDefaults() => _settings.ResetToDefaults();
    }
}
