using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Общие дефолты игры (вики «13» §3.4): начальные пользовательские настройки (громкости, локаль) и
    /// мета-правила. ФАКТИЧЕСКИЕ настройки игрока живут в файле настроек (ES3) — сюда не пишутся.
    /// Потребителей пока нет (Фаза 6/7 UI/аудио подключатся) — сейчас только тип, ассет, DI.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Config/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Default audio volumes (first launch)")]
        [Range(0f, 1f)] [SerializeField] private float _defaultMasterVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float _defaultMusicVolume  = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float _defaultSfxVolume    = 1f;

        [Header("Localization")]
        [Tooltip("Локаль по умолчанию (код Unity Localization, напр. \"en\"/\"ru\"). Пусто = авто из системы.")]
        [SerializeField] private string _defaultLocale = "en";

        [Header("Rules")]
        [Tooltip("Слотов предметов на персонажа (Vessel-скоуп, вики «13» §3.2 ItemData.Scope).")]
        [SerializeField] private int _vesselItemSlots = 3;

        public float  DefaultMasterVolume => _defaultMasterVolume;
        public float  DefaultMusicVolume  => _defaultMusicVolume;
        public float  DefaultSfxVolume    => _defaultSfxVolume;
        public string DefaultLocale       => _defaultLocale;
        public int    VesselItemSlots     => _vesselItemSlots;
    }
}
