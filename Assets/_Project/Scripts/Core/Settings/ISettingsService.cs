using System;

namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Единый источник пользовательских настроек + персист (ES3) + живое применение в аудио.
    /// Дефолты первого запуска — из GameConfig. Настройки локальны для клиента (кооп не задевают).
    /// <para>UX: сеттеры применяют значение СРАЗУ (слышно при драге слайдера) и поднимают
    /// <see cref="Changed"/>; на диск пишет только <see cref="Save"/>. Снапшот для Cancel держит
    /// вызывающий (ViewModel): запомнил <see cref="Audio"/> до правок, при отмене — вернул через сеттеры.</para>
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>Текущие значения звука (то, что применено сейчас).</summary>
        AudioVolumeSettings Audio { get; }

        /// <summary>Текущие геймплей-настройки презентации (анимация карточек и т.п.).</summary>
        GameplaySettings Gameplay { get; }

        /// <summary>Поднимается при любом изменении значений (для биндинга UI).</summary>
        event Action Changed;

        /// <summary>Общая громкость [0..1]: обновить + применить живьём + <see cref="Changed"/>.</summary>
        void SetMasterVolume(float volume01);

        /// <summary>Громкость музыки [0..1].</summary>
        void SetMusicVolume(float volume01);

        /// <summary>Громкость SFX [0..1].</summary>
        void SetSfxVolume(float volume01);

        /// <summary>Включить/выключить анимацию карточек реликвий целиком (idle+attack). Поднимает <see cref="Changed"/>.</summary>
        void SetCardAnimations(bool enabled);

        /// <summary>Включить/выключить анимацию атаки выбранной карточки (при включённой анимации). Поднимает <see cref="Changed"/>.</summary>
        void SetCardAttackAnimation(bool enabled);

        /// <summary>Загрузить с диска (ES3) или взять дефолты GameConfig, затем применить в аудио.</summary>
        void Load();

        /// <summary>Сохранить текущие значения на диск (ES3).</summary>
        void Save();

        /// <summary>Сбросить к дефолтам GameConfig и применить (на диск НЕ пишет — это делает Save).</summary>
        void ResetToDefaults();
    }
}
