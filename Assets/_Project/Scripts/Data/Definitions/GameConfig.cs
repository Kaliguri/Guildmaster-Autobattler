using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Общие правила и экономика игры (вики «13» §3.4) плюс громкости первого запуска. ФАКТИЧЕСКИЕ
    /// настройки игрока живут в файле настроек — сюда не пишутся.
    /// <para><b>Единственный владелец значений — ассет</b> (<c>Configs/GameConfig.asset</c>), поэтому
    /// инициализаторов у полей здесь нет. Так и задумано: дефолт в C# на игру не влияет — Unity читает
    /// сериализованное значение, — но при чтении кода выглядит как правда. Однажды это уже разошлось:
    /// код обещал вместимость коллекции 8, ассет держал 12, играли по 12. Осмысленность значений
    /// проверяет <c>ConfigValidationTests</c>, поэтому пустой конфиг не пройдёт молча.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Config/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Default audio volumes (first launch)")]
        [Range(0f, 1f)] [SerializeField] private float _defaultMasterVolume;
        [Range(0f, 1f)] [SerializeField] private float _defaultMusicVolume;
        [Range(0f, 1f)] [SerializeField] private float _defaultSfxVolume;

        // Локаль по умолчанию тут не живёт: её ведёт Unity Localization (выбранная локаль) и файл
        // настроек игрока через SettingsService. Поле _defaultLocale не читал никто — снято.

        [Header("Rules")]
        [Tooltip("Команда локального игрока. По умолчанию первая (0). В бою нет «стороны игрока» — " +
                 "есть команды, и победа определяется сравнением исхода с этим номером (шов под PvP).")]
        [SerializeField] private int _localPlayerTeam;

        [Tooltip("Слотов предметов на персонажа (Vessel-скоуп, вики «13» §3.2 ItemData.Scope). GDD 16: 3, не 4.")]
        [SerializeField] private int _vesselItemSlots;

        [Tooltip("Сколько баннеров (Party-скоуп) можно взять активными на весь отряд.")]
        [SerializeField] private int _partyBannerSlots;

        [Tooltip("Стартовая вместимость коллекции реликов гильдии (запас ненадетых, план 11 §5.4).")]
        [SerializeField] private int _relicCapacityBase;

        [Tooltip("Потолок вместимости коллекции реликов (апгрейд в магазине не поднимет выше).")]
        [SerializeField] private int _relicCapacityMax;

        [Header("Economy (план act-map-run-loop §3.3)")]
        [Tooltip("Стартовое золото забега.")]
        [SerializeField] private int _startGold;

        [Tooltip("Награда золотом за победу в бою (обычный/элита/босс).")]
        [SerializeField] private int _battleGoldReward;

        [Tooltip("Базовая цена покупки реликвии по KitPower: Common.")]
        [SerializeField] private int _priceCommon;

        [Tooltip("Базовая цена покупки реликвии по KitPower: Cursed.")]
        [SerializeField] private int _priceCursed;

        [Tooltip("Базовая цена покупки реликвии по KitPower: Divine.")]
        [SerializeField] private int _priceDivine;

        [Tooltip("Случайный разброс цены вокруг базы (доля, напр. 0.2 = ±20%), детерминирован сидом.")]
        [Range(0f, 0.9f)] [SerializeField] private float _priceSpread;

        [Tooltip("Доля цены покупки, которую игрок получает при продаже реликвии (напр. 0.25 = 25%).")]
        [Range(0f, 1f)] [SerializeField] private float _sellPercent;

        [Tooltip("Стоимость реролла витрины магазина (перекат всех слотов).")]
        [SerializeField] private int _shopRerollCost;

        [Tooltip("Пул перезапусков боя НА АКТ (реш. №65): сбрасывается в начале акта, не копится.")]
        [SerializeField] private int _restartsPerAct;

        [Header("Guild (starting run)")]
        [Tooltip("Размер стартовой гильдии игрока (стандартных сосудов). GDD: 4.")]
        [SerializeField] private int _guildSize;

        [Tooltip("Релик на стартовом сосуде (пустой кит) — игрок навешивает собранное в лоадауте.")]
        [SerializeField] private string _startingRelicId;

        [Header("Saves (профили и гильдии)")]
        [Tooltip("Сколько профилей аккаунта можно завести. Профиль — мета игрока (открытия), переключаемая: " +
                 "напр. отдельный профиль под игры с друзьями. Реш. Макса 2026-07-26: 4.")]
        [SerializeField] private int _maxProfiles;

        [Tooltip("Сколько гильдий (домов) помещается в один профиль. Гильдия — она же слот сохранения: " +
                 "в ней живёт не более одного активного забега. Реш. Макса 2026-07-26: 8.")]
        [SerializeField] private int _maxGuildsPerProfile;

        [Header("Guild roster (дом между забегами)")]
        [Tooltip("Сколько людей помещается в новом доме. В забег уходят четверо, остальные ждут дома. " +
                 "Реш. Макса 2026-07-27: 8.")]
        [SerializeField] private int _startingRosterCapacity;

        [Tooltip("До скольки мест дом может вырасти за валюту гильдии. Глубокая скамейка — условие " +
                 "смертности и гейта ветеранов. Реш. Макса 2026-07-27: 64.")]
        [SerializeField] private int _maxRosterCapacity;

        [Tooltip("Сколько ветеранов дом должен потерять, чтобы открылся платный наём готовых ветеранов " +
                 "(предохранитель от грайнда после плохой ночи). Ориентир Макса 2026-07-27: 8.")]
        [SerializeField] private int _veteranHireUnlockDeaths;

        public float  DefaultMasterVolume => _defaultMasterVolume;
        public float  DefaultMusicVolume  => _defaultMusicVolume;
        public float  DefaultSfxVolume    => _defaultSfxVolume;
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

        public int    GuildSize           => _guildSize;
        public string StartingRelicId     => _startingRelicId;

        public int    MaxProfiles         => _maxProfiles;
        public int    MaxGuildsPerProfile => _maxGuildsPerProfile;

        public int    StartingRosterCapacity  => _startingRosterCapacity;
        public int    MaxRosterCapacity       => _maxRosterCapacity;
        public int    VeteranHireUnlockDeaths => _veteranHireUnlockDeaths;

        /// <summary>
        /// Заготовка значений: инстанс в памяти, заполненный тем, с чего начинают новый ассет. Нужна
        /// авторингу и тестам, которым конфиг требуется, но не важен по существу.
        /// <para>Это НЕ дублирование ассета, а его единственная альтернатива в коде: пока значения жили
        /// инициализаторами полей, каждое из них имело двух владельцев — и они разъехались (код обещал
        /// вместимость 8 против 12 в ассете, тест уверял про 4 слота предмета против 3). Здесь дефолты
        /// собраны в одном месте и явно названы заготовкой; играет всегда ассет.</para>
        /// </summary>
        public static GameConfig CreateDefault()
        {
            var c = CreateInstance<GameConfig>();
            c._defaultMasterVolume = 1f;
            c._defaultMusicVolume  = 0.8f;
            c._defaultSfxVolume    = 1f;
            c._localPlayerTeam     = 0;
            c._vesselItemSlots     = 3;
            c._partyBannerSlots    = 2;
            c._relicCapacityBase   = 12;
            c._relicCapacityMax    = 16;
            c._startGold           = 100;
            c._battleGoldReward    = 20;
            c._priceCommon         = 50;
            c._priceCursed         = 100;
            c._priceDivine         = 150;
            c._priceSpread         = 0.2f;
            c._sellPercent         = 0.25f;
            c._shopRerollCost      = 50;
            c._restartsPerAct      = 2;
            c._guildSize           = 4;
            c._startingRelicId     = ContentIds.BaseRelic;
            c._maxProfiles         = 4;
            c._maxGuildsPerProfile = 8;
            c._startingRosterCapacity  = 8;
            c._maxRosterCapacity       = 64;
            c._veteranHireUnlockDeaths = 8;
            return c;
        }
    }
}
