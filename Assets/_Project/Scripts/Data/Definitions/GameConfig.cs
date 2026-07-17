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
        [Tooltip("Команда локального игрока. По умолчанию первая (0). В бою нет «стороны игрока» — " +
                 "есть команды, и победа определяется сравнением исхода с этим номером (шов под PvP).")]
        [SerializeField] private int _localPlayerTeam;

        [Tooltip("Слотов предметов на персонажа (Vessel-скоуп, вики «13» §3.2 ItemData.Scope). GDD 16: 3, не 4.")]
        [SerializeField] private int _vesselItemSlots = 3;

        [Tooltip("Сколько баннеров (Party-скоуп) можно взять активными на весь отряд.")]
        [SerializeField] private int _partyBannerSlots = 2;

        [Tooltip("Стартовая вместимость коллекции реликов гильдии (запас ненадетых, план 11 §5.4).")]
        [SerializeField] private int _relicCapacityBase = 8;

        [Tooltip("Потолок вместимости коллекции реликов (апгрейд в магазине не поднимет выше).")]
        [SerializeField] private int _relicCapacityMax = 16;

        [Header("Economy (план act-map-run-loop §3.3)")]
        [Tooltip("Стартовое золото забега.")]
        [SerializeField] private int _startGold = 100;

        [Tooltip("Награда золотом за победу в бою (обычный/элита/босс).")]
        [SerializeField] private int _battleGoldReward = 20;

        [Tooltip("Базовая цена покупки реликвии по KitPower: Common.")]
        [SerializeField] private int _priceCommon = 50;

        [Tooltip("Базовая цена покупки реликвии по KitPower: Cursed.")]
        [SerializeField] private int _priceCursed = 100;

        [Tooltip("Базовая цена покупки реликвии по KitPower: Divine.")]
        [SerializeField] private int _priceDivine = 150;

        [Tooltip("Случайный разброс цены вокруг базы (доля, напр. 0.2 = ±20%), детерминирован сидом.")]
        [Range(0f, 0.9f)] [SerializeField] private float _priceSpread = 0.2f;

        [Tooltip("Доля цены покупки, которую игрок получает при продаже реликвии (напр. 0.25 = 25%).")]
        [Range(0f, 1f)] [SerializeField] private float _sellPercent = 0.25f;

        [Tooltip("Стоимость реролла витрины магазина (перекат всех слотов).")]
        [SerializeField] private int _shopRerollCost = 50;

        [Tooltip("Пул перезапусков боя НА АКТ (реш. №65): сбрасывается в начале акта, не копится.")]
        [SerializeField] private int _restartsPerAct = 2;

        public float  DefaultMasterVolume => _defaultMasterVolume;
        public float  DefaultMusicVolume  => _defaultMusicVolume;
        public float  DefaultSfxVolume    => _defaultSfxVolume;
        public string DefaultLocale       => _defaultLocale;
        public int    LocalPlayerTeam     => _localPlayerTeam;
        public int    VesselItemSlots     => _vesselItemSlots;
        public int    PartyBannerSlots    => _partyBannerSlots;
        public int    RelicCapacityBase   => _relicCapacityBase;
        public int    RelicCapacityMax    => _relicCapacityMax;

        public int    StartGold           => _startGold;
        public int    BattleGoldReward    => _battleGoldReward;
        public int    PriceCommon         => _priceCommon;
        public int    PriceCursed         => _priceCursed;
        public int    PriceDivine         => _priceDivine;
        public float  PriceSpread         => _priceSpread;
        public float  SellPercent         => _sellPercent;
        public int    ShopRerollCost      => _shopRerollCost;
        public int    RestartsPerAct      => _restartsPerAct;
    }
}
