namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Пользовательские настройки геймплей-презентации (не аудио). Чистый POCO — персистится через
    /// <see cref="ISettingsService"/> рядом с <see cref="AudioVolumeSettings"/>. Struct — снапшот для
    /// Cancel это копия по значению без аллокаций. Настройки локальны для клиента (кооп не синхронизирует).
    /// <para>Дефолты — ВКЛ (анимация карточек живая из коробки): «пустой» <c>default</c> даёт false, поэтому
    /// значения по умолчанию задаёт <see cref="Defaults"/>, а не безымянный конструктор.</para>
    /// </summary>
    [System.Serializable]
    public struct GameplaySettings
    {
        /// <summary>Анимировать спрайты карточек реликвий в инвентаре (idle+attack выбранной). Off = всё статично.</summary>
        public bool CardAnimations;

        /// <summary>Проигрывать анимацию атаки у выбранной карточки (в цикле). Off = только idle. Игнорируется, если
        /// <see cref="CardAnimations"/> выключена.</summary>
        public bool CardAttackAnimation;

        public GameplaySettings(bool cardAnimations, bool cardAttackAnimation)
        {
            CardAnimations      = cardAnimations;
            CardAttackAnimation = cardAttackAnimation;
        }

        /// <summary>Значения первого запуска: вся анимация карточек включена.</summary>
        public static GameplaySettings Defaults() => new GameplaySettings(true, true);
    }
}
