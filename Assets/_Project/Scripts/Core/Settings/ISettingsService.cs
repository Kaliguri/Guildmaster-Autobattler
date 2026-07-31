using System;

namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Единый источник пользовательских настроек + персист (за ISaveService, ключ prefs) + живое применение в аудио.
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

        /// <summary>Всегда показывать подробности в подсказках (Shift тогда временно даёт краткий вид, §II.10.4).</summary>
        void SetAlwaysDetailedTooltips(bool enabled);

        /// <summary>
        /// Запомнить, что бой игрок смотрит свободной камерой, а не сценарной. Экрана настроек у этого
        /// выбора нет — его пишет камера сразу после Tab, поэтому за сеттером тут же идёт <see cref="Save"/>.
        /// </summary>
        void SetFreeCombatCamera(bool free);

        /// <summary>Загрузить с диска (ключ <c>prefs</c>) или взять дефолты GameConfig, затем применить в аудио.</summary>
        void Load();

        /// <summary>Сохранить текущие значения на диск (ключ <c>prefs</c>).</summary>
        void Save();

        /// <summary>Сбросить к дефолтам GameConfig и применить (на диск НЕ пишет — это делает Save).</summary>
        void ResetToDefaults();
    }
}
