namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Пользовательские настройки громкости, значения [0..1]. Чистый POCO — персистится через
    /// <see cref="ISettingsService"/>, дефолты первого запуска берутся из GameConfig. Struct — снапшот
    /// для Cancel это копия по значению без аллокаций. Имя НЕ <c>AudioSettings</c> во избежание коллизии
    /// с <c>UnityEngine.AudioSettings</c>. Настройки локальны для клиента (кооп не синхронизирует).
    /// </summary>
    [System.Serializable]
    public struct AudioVolumeSettings
    {
        public float Master;
        public float Music;
        public float Sfx;

        public AudioVolumeSettings(float master, float music, float sfx)
        {
            Master = master;
            Music = music;
            Sfx = sfx;
        }
    }
}
