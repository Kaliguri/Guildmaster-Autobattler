namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Пользовательские настройки звука, значения [0..1]. Чистый POCO — персистится через
    /// <see cref="ISettingsService"/> (ES3), дефолты первого запуска берутся из GameConfig.
    /// Struct, чтобы снапшот для Cancel был копией по значению без аллокаций.
    /// Настройки локальны для клиента (в кооп-игре не синхронизируются).
    /// </summary>
    [System.Serializable]
    public struct AudioSettings
    {
        public float Master;
        public float Music;
        public float Sfx;

        public AudioSettings(float master, float music, float sfx)
        {
            Master = master;
            Music = music;
            Sfx = sfx;
        }
    }
}
