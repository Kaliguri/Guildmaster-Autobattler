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

        // «Команда локального игрока» тут больше не живёт (снято 08.08.2026). Сторона — не настройка
        // и не свойство режима, а НАЗНАЧЕНИЕ в составе сеанса, которое можно менять по ходу игры:
        // единственный её владелец — ISessionRoster. Пока поле было, у ответа «за кого я играю»
        // существовало два источника, и при пустом составе тихо побеждал этот.

        [Header("Rules")]
        [Tooltip("Слотов предметов на персонажа (Vessel-скоуп, вики «13» §3.2 ItemData.Scope). GDD 16: 3, не 4.")]
        [SerializeField] private int _vesselItemSlots;

        [Tooltip("ПОТОЛОК слотов предмета у «Сосуда» (ГДД: 4). Сверх базы открывается наградой " +
                 "внутри забега и сгорает вместе с ним — как и места отряда.")]
        [SerializeField] private int _vesselItemSlotsMax;

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
        [Tooltip("ПОТОЛОК мест в отряде забега (ГДД preparation-screens §2.1: 8). Сколько из них " +
                 "открыто сейчас — в RunState.OpenSlots: вместимость добывается наградой забега.")]
        [SerializeField] private int _guildSize;

        [Tooltip("Сколько мест отряда открыто на старте забега (ГДД: 6). Остальные до потолка " +
                 "открываются наградой внутри забега и сгорают вместе с ним.")]
        [SerializeField] private int _guildSlotsOpenAtStart;

        [Tooltip("Сколько «Сосудов» выходит в бой одновременно (ГДД: 4). Остальные ждут в запасе.")]
        [SerializeField] private int _battleSlots;

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

        [Header("Балансные конфиги (ссылки, не значения)")]
        [Tooltip("Статы: константа брони, реген ресурса, дефолты. Ссылка живёт ЗДЕСЬ, а не в скоупах — " +
                 "иначе каждая автономная сцена держит свою и они расходятся.")]
        [SerializeField] private StatsConfig _statsConfig;

        [Tooltip("Классовые коридоры баланса. Ссылка живёт здесь по той же причине, что и StatsConfig.")]
        [SerializeField] private ClassBalanceConfig _classBalanceConfig;

        [Tooltip("Набор скинов курсора: что можно надеть и что стоит по умолчанию. Ссылка здесь по той " +
                 "же причине — иначе каждая автономная сцена завела бы свой набор.")]
        [SerializeField] private CursorSkinCatalog _cursorSkins;

        [Tooltip("Набор знаков для профиля и дома: белые силуэты, красятся выбранным цветом. Пусто — " +
                 "экран создания просто не спросит про знак.")]
        [SerializeField] private GuildEmblemCatalog _guildEmblems;

        [Tooltip("Обращение и ссылки сообщества для главного меню. Пусто — панели сообщества не будет.")]
        [SerializeField] private CommunityConfig _community;

        public float  DefaultMasterVolume => _defaultMasterVolume;
        public float  DefaultMusicVolume  => _defaultMusicVolume;
        public float  DefaultSfxVolume    => _defaultSfxVolume;
        /// <summary>База слотов предмета у «Сосуда». Сколько открыто сейчас — в <c>RunState.OpenItemSlots</c>.</summary>
        public int    VesselItemSlots     => _vesselItemSlots;

        /// <summary>Потолок слотов предмета: четвёртый открывается наградой забега и сгорает с ним.</summary>
        public int    VesselItemSlotsMax  => _vesselItemSlotsMax;
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

        /// <summary>Потолок мест в отряде. Открытых может быть меньше — см. <c>RunState.OpenSlots</c>.</summary>
        public int    GuildSize           => _guildSize;

        /// <summary>Сколько мест отряда открыто на старте забега.</summary>
        public int    GuildSlotsOpenAtStart => _guildSlotsOpenAtStart;

        /// <summary>Сколько «Сосудов» выходит в бой одновременно.</summary>
        public int    BattleSlots         => _battleSlots;
        public string StartingRelicId     => _startingRelicId;

        public int    MaxProfiles         => _maxProfiles;
        public int    MaxGuildsPerProfile => _maxGuildsPerProfile;

        public int    StartingRosterCapacity  => _startingRosterCapacity;
        public int    MaxRosterCapacity       => _maxRosterCapacity;
        public int    VeteranHireUnlockDeaths => _veteranHireUnlockDeaths;

        /// <summary>
        /// Балансные конфиги, на которые ссылается игра. **Единственное место, где выбран играющий
        /// экземпляр** — и скоупы, и editor-бенчи берут их отсюда.
        /// </summary>
        /// <remarks>
        /// Ссылки собраны здесь не для удобства, а чтобы у факта «какой `StatsConfig` играет» был один
        /// владелец. Раньше эти поля сериализовались в <c>RootLifetimeScope</c> И в
        /// <c>CombatLifetimeScope</c> (боевая сцена обязана подниматься без Root — standalone dev-арена),
        /// то есть в двух сценах, и совпадали только под присмотром теста. Хуже было у бенчей: они брали
        /// «первый по алфавиту ассет типа <c>StatsConfig</c>», поэтому второй такой ассет молча увёл бы
        /// балансные отчёты на конфиг, которым игра не играет.
        /// <para>Пусто у инстанса из <see cref="CreateDefault"/> — в памяти ассетов нет. Потребителям
        /// рантайма конфиги обязательны, поэтому они запрашивают их через <c>ScopeWiring.Require</c> и
        /// падают громко, а не подставляют молча.</para>
        /// </remarks>
        public StatsConfig        Stats        => _statsConfig;
        public ClassBalanceConfig ClassBalance => _classBalanceConfig;

        /// <summary>Набор скинов курсора. <c>null</c> — курсор остаётся системным, игра идёт как обычно.</summary>
        public CursorSkinCatalog  CursorSkins  => _cursorSkins;

        /// <summary>Набор знаков профиля и дома. <c>null</c> — знак не выбирают, слоту хватает имени.</summary>
        public GuildEmblemCatalog GuildEmblems => _guildEmblems;

        /// <summary>Обращение и ссылки сообщества. <c>null</c> — меню обходится без правой панели.</summary>
        public CommunityConfig    Community    => _community;

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
            c._vesselItemSlots     = 3;
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
            c._vesselItemSlotsMax  = 4;
            c._guildSize           = 8;
            c._guildSlotsOpenAtStart = 6;
            c._battleSlots         = 4;
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
