using System;
using Guildmaster.Data.Stats;
using UnityEngine;
using UnityEngine.Serialization;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Общий боевой кит всего, что выходит на арену (вики «13» §3.1). Наследники — <see cref="RelicData"/>
    /// (мета игрока) и <see cref="EnemyData"/> (мета врага). Сим и <c>RuntimeUnitFactory</c>/
    /// <c>EncounterLoader</c> работают с этим базовым типом — им всё равно, кто перед ними.
    /// <para>Поля кита перенесены из <see cref="RelicData"/> БЕЗ смены имён (сериализация сохранена).</para>
    /// </summary>
    public abstract class UnitData : ContentDefinition
    {
        [Header("Combat categories")]
        [Tooltip("Боевой класс — задаёт базовый баланс HP и скорости (через ClassBalanceConfig, 2-й уровень " +
                 "стат-каскада). Персональные отличия юнита кладутся ДЕЛЬТОЙ поверх (Flat/Percent в Base stat block). " +
                 "Bruiser = эталон 100%/100%.")]
        [SerializeField] private UnitClass _combatClass = UnitClass.Bruiser;

        [Tooltip("Тип урона АВТОАТАКИ. Обязателен: школа брони выводится из него. У способностей свой " +
                 "тип у каждой — наследования от юнита нет (реформа 2026-07-30).")]
        [SerializeField] private DamageType _autoAttackDamageType = DamageType.Undefined;

        [Tooltip("Тип существа САМОГО юнита. Определяет, как по нему бьют сродства (Нежить иммунна к Яду, уязвима к Свету и т.д.).")]
        [SerializeField] private CreatureType _creatureType = CreatureType.Living;

        [SerializeField] private AttackType _attackType = AttackType.Melee;
        [SerializeField] private ResourceType _resourceType = ResourceType.None;

        [Tooltip("Ступень дальности авто-атаки. Число за ступенью живёт в StatsConfig — здесь юнит " +
                 "объявляет, кто он по дистанции, а не сколько метров достаёт. Своё число в стат-блоке " +
                 "задавать нельзя: тогда у дальности снова стало бы два владельца (AttackRangeBandTests).")]
        [SerializeField] private AttackRangeBand _rangeBand = AttackRangeBand.Melee;

        [Tooltip("Личная поправка к ступени, доля: 0.1 = на 10% дальше своих. Намеренно мелкая и в долях, " +
                 "а не в единицах, — чтобы правка ступени доезжала и до тех, кто от неё отличается.")]
        [Range(-0.25f, 0.25f)]
        [SerializeField] private float _rangeAdjustPct;

        [Header("Архетип анимаций")]
        [Tooltip("Набор клипов, по которому юнит двигается. Сим/фабрика читают отсюда кадр контакта " +
                 "авто-атаки для windup. ОБЯЗАТЕЛЕН: пусто = UnitView не найдёт клип атаки и ругнётся в " +
                 "лог, а замах свалится на телеграф-пол в три тика. Сторож — ContentValidationService " +
                 "(Doctor в Content Hub).")]
        // Поле звалось _visual до 06.08.2026: имя врало, внутри клипы, а не спрайты. FormerlySerializedAs
        // держит ссылки 47 живых ассетов — без него они обнулились бы молча, и весь ростер потерял бы
        // анимацию при следующем сохранении.
        [FormerlySerializedAs("_visual")]
        [SerializeField] private AnimationArchetypeData _archetype;

        [Tooltip("Свой префаб визуала юнита (с настроенным Animator и реальным размером ПРЯМО в префабе). " +
                 "ОБЯЗАТЕЛЕН: пусто = юнит выйдет на арену дефолтным видом презентера и станет неотличим " +
                 "от соседа. Временное тело берётся переиспользованием чужого пака — заглушкой это не " +
                 "закрывают.")]
        [SerializeField] private GameObject _viewPrefab;

        [Tooltip("Облачение: броня и предметы в руках. Пусто = играет то, что лежит на префабе рига. " +
                 "Именно здесь снимается щит у тех, кто его не носит: строка с пустым спрайтом.")]
        [SerializeField] private OutfitData _outfit;

        [Tooltip("Оттенок, которым юнит СВЕТИТ: снаряд, его след, контур каста, искры, осколки. Хранится " +
                 "роль, а не цвет — значение живёт в палитре (UI/Theme/tokens.*.uss → GuildmasterPalette), " +
                 "яркость накручивает CombatColorPalette множителями. Повторять оттенок между юнитами " +
                 "разрешено: героя от врага отличает полоса HP. Кому какой — gdd/10-vision/vfx-color §Ростер.")]
        [SerializeField] private UnitTone _vfxTone = UnitTone.NeutralWarm;

        [Header("Auto-attack shape (Phase 3)")]
        [Tooltip("Форма авто-атаки: None = одиночная цель; Line = линия перед юнитом (несколько целей, «Размашистый выпад»).")]
        [SerializeField] private AreaShape _autoAttackShape = AreaShape.None;

        [Tooltip("Ширина линии авто-атаки (мировые единицы), для Line.")]
        [SerializeField] private float _autoAttackWidth = 1f;

        [Tooltip("Длина линии = AttackRange × это. Копейщик = 2: бьёт цель на 2, но полоса накрывает от его ног до 4 — древко достаёт дальше, чем он выбирает цель.")]
        [Min(0.01f)]
        [SerializeField] private float _autoAttackLengthMult = 1f;

        [Tooltip("On-hit эффекты авто-атаки (§9.1): накладываются на каждую задетую цель в момент удара — мили (single/Line) и при попадании снаряда. Криомант = «Заморозка». Пусто = нет (поведение Ф1/Ф2).")]
        [SerializeField] private EffectData[] _autoAttackEffects;

        [Header("Channelled auto-attack")]
        [Tooltip("Канал авто-атаки: удар не одномоментный, а поток тиков между замахом и хвостом. " +
                 "Duration = 0 (дефолт) — обычная атака, поле не работает вовсе. Урон и частота тиков НЕ " +
                 "задаются здесь: тик канала — это обычный удар (AutoAttackDamage с интервалом 1/AttackSpeed).")]
        [SerializeField] private AttackChannel _channel = AttackChannel.None;

        [Header("Attack while moving (Phase 3, §9.8)")]
        [Tooltip("Стрельба на ходу: авто-атака НЕ рутит движение (Следопыт). false = стоп на атаку (поведение Ф1).")]
        [SerializeField] private bool _canAttackWhileMoving;

        [Tooltip("Штраф MoveSpeed (0..1) пока идёт замах при стрельбе на ходу. Следопыт = 0.5 (−50%).")]
        [SerializeField] private float _movingAttackSpeedPenaltyPct = 0.5f;

        [Tooltip("ДОПОЛНИТЕЛЬНЫЙ хвост-восстановление после удара, сек, СВЕРХ анимационного доигрыша клипа " +
                 "(тот считается автоматически). Юнит «занят» (рут/штраф скорости) весь хвост. 0 = только " +
                 "доигрыш клипа. Ненулевое — сознательный «оверкоммит» (замедляет эффективную скорость атаки).")]
        [SerializeField] private float _attackRecoverySeconds;

        [Tooltip("Доля свинга до кадра контакта, 0..1: сколько удар «замахивается», прежде чем прилететь. " +
                 "0.45 = контакт чуть позже середины (размашистый удар с внятным телеграфом), 0.2 = быстрый " +
                 "тычок. 0 = взять из кадров AnimationArchetypeData (покадровые юниты так и делают). ЗАДАВАТЬ ОБЯЗАТЕЛЬНО " +
                 "юнитам без AnimationArchetypeData (скелетный риг): кадров у них нет, расчёт падает на телеграф-пол в " +
                 "3 тика, и клип атаки скрабится в 0.1 с — удар прилетает почти мгновенно и выглядит рвано.")]
        [SerializeField, Range(0f, 1f)] private float _windupShare;

        [Tooltip("Потолок длительности свинга ЭТОГО юнита в сим-тиках (30 = 1 сек при 30 Гц; 45 = 1.5 сек, " +
                 "60 = 2 сек). 0 = глобальный дефолт SimConstants.MaxAttackAnimTicks. Ставить тем, чья " +
                 "идентичность — редкий тяжёлый удар: длинный занос = широкое окно прерывания и парирования. " +
                 "ВАЖНО: свинг съедает паузу между атаками (пауза = интервал − свинг), поэтому длинный занос " +
                 "имеет смысл только вместе с низкой скоростью атаки у того же юнита — иначе окно исчезнет.")]
        [SerializeField, Min(0)] private int _attackSwingTicks;

        [Tooltip("Замах ПЕРВОГО удара после разбега, долей от обычного: 1 = такой же (особого удара нет), " +
                 "1.5 = в полтора раза длиннее (размашистый удар с ходу — дольше телеграф, весомее вход в " +
                 "бой), 0.6 = короче (выпад на скорости). Тратится одним ударом: добежал разбегом — ударил — " +
                 "дальше бьёт как обычно. Значение клампится теми же границами, что обычный замах, поэтому " +
                 "не может ни выйти за интервал атаки, ни опуститься ниже телеграф-пола.")]
        [SerializeField] private float _chargeAttackWindupMult = 1f;

        [Tooltip("Сила КАЖДОГО Удара в Атаке, долей от урона авто-атаки: по числу на контакт, в порядке " +
                 "ударов. Пусто = каждый Удар бьёт в полную силу (1.0), в том числе при нескольких " +
                 "контактах — двухударный кит без настройки бьёт вдвое. Монах: 0.5 / 0.5 (два Удара по " +
                 "половине). Ограничений сверху нет: 2 = удар вдвое сильнее обычного, годится для " +
                 "финишера серии. Длина списка обязана совпадать с числом маркеров в клипе атаки.")]
        [SerializeField] private float[] _hitDamageShares;

        [Header("Resource gain (Phase 3)")]
        [Tooltip("Ресурс (мана) за авто-атаку, × ResourceGainEff, клампится к MaxResource. 0 = не копит от ударов. Копейщик = 5.")]
        [SerializeField] private float _resourceOnHit;

        [Tooltip("Потолок набора ресурса, единиц В СЕКУНДУ. 0 = без потолка. Держит связку «разгон темпа → " +
                 "вдвое больше маны»: темп каста должен ускоряться решениями игрока, но не улетать. Мечник = 10.")]
        [SerializeField] private float _maxResourceGainPerSecond;

        [Header("Base stat block")]
        [Tooltip("Модификаторы юнита. Накладываются поверх дефолтов StatsConfig при сборке (отличия от базы).")]
        [SerializeField] private StatModifier[] _stats;

        [Header("Passives")]
        [Tooltip("Пассивные эффекты (накладываются при сборке юнита, обычно постоянные).")]
        [SerializeField] private EffectData[] _grantedEffects;

        [Header("Active abilities")]
        [Tooltip("Активные способности (кулдаун/ресурс). Слотов — по редкости (Common 1 → Rare 2 → Epic 3).")]
        [SerializeField] private AbilityData[] _abilities;

        [Header("AI")]
        [Tooltip("Дефолтный пресет поведения юнита (вики «13» §3.2). У врагов — единственный.")]
        [SerializeField] private AIPresetData _aiPreset;

        // Легаси inline-профиль AI: источник миграции в AIPresetData (§3.2). Удаляется после назначения
        // пресетов (отдельный шаг пакета 3). До тех пор — фолбэк для Ai, если пресет ещё не назначен.
        [SerializeField, HideInInspector] private AIProfile _ai = new AIProfile();

        [Header("Info")]
        [Tooltip("Иконка для UI.")]
        [SerializeField] private Sprite _icon;
        [Tooltip("Ручные информационные теги (роль, стиль); авто-теги считаются из DamageType и др. (§3.0).")]
        [SerializeField] private TagData[] _infoTags;

        public UnitClass CombatClass => _combatClass;

        /// <summary>
        /// Тип урона автоатаки. Школа брони — <c>DamageTypes.SchoolOf(AutoAttackDamageType)</c>;
        /// отдельного поля школы у юнита больше нет.
        /// </summary>
        public DamageType AutoAttackDamageType => _autoAttackDamageType;
        public CreatureType CreatureType => _creatureType;
        public AttackType AttackType => _attackType;

        /// <summary>Ступень дальности авто-атаки; дистанцию за ней знает <see cref="StatsConfig"/>.</summary>
        public AttackRangeBand RangeBand => _rangeBand;

        /// <summary>Личная поправка к ступени, доля от неё (0.1 = +10%).</summary>
        public float RangeAdjustPct => _rangeAdjustPct;

        public ResourceType ResourceType => _resourceType;
        /// <summary>Архетип анимаций: какие клипы играет юнит. Звался <c>Visual</c> до 06.08.2026.</summary>
        public AnimationArchetypeData Archetype => _archetype;
        /// <summary>Облачение: броня и предметы в руках. <c>null</c> — как на префабе.</summary>
        public OutfitData Outfit => _outfit;
        public GameObject ViewPrefab => _viewPrefab;
        /// <summary>
        /// Оттенок юнита: и чем он СВЕТИТ (снаряд, искры, контур каста), и каким цветом окрашено его
        /// ТЕЛО. Один источник на оба (05.08.2026); цвет по роли отдаёт <c>CombatColorPalette</c>.
        /// </summary>
        public UnitTone VfxTone => _vfxTone;

        public AreaShape AutoAttackShape => _autoAttackShape;
        public float AutoAttackWidth => _autoAttackWidth;

        /// <summary>Канал авто-атаки; <see cref="AttackChannel.Exists"/> = false у всех, кто бьёт обычно.</summary>
        public AttackChannel Channel => _channel;

        /// <summary>
        /// Во сколько раз полоса линейной авто-атаки длиннее дальности выбора цели. 1 = полоса ровно
        /// до цели (прежнее поведение всего контента).
        /// </summary>
        /// <remarks>
        /// Разведено 2026-07-28 под Копейщика: дальность решает, кого он МОЖЕТ бить и где встанет строй,
        /// а длина полосы — сколько он заденет по пути. Раньше это было одно число, и попытка дать ему
        /// вторую линию автоматически укорачивала его же зону поражения вдвое.
        /// </remarks>
        public float AutoAttackLengthMult => _autoAttackLengthMult;
        public EffectData[] AutoAttackEffects => _autoAttackEffects;
        public bool CanAttackWhileMoving => _canAttackWhileMoving;
        public float MovingAttackSpeedPenaltyPct => _movingAttackSpeedPenaltyPct;
        public float AttackRecoverySeconds => _attackRecoverySeconds;

        /// <summary>Доля свинга до кадра контакта (0..1). 0 = считать из кадров <see cref="AnimationArchetypeData"/>.</summary>
        public float WindupShare => _windupShare;

        /// <summary>Заданные доли урона по Ударам; <c>null</c>/пусто = каждый Удар в полную силу.</summary>
        public float[] HitDamageShares => _hitDamageShares;

        /// <summary>
        /// Доля урона <paramref name="hitIndex"/>-го Удара Атаки. Не задана — <c>1</c>: полная сила.
        /// </summary>
        /// <remarks>
        /// Дефолт именно единица, а не «поровну между контактами» (вердикт Макса 2026-07-31): два Удара
        /// без настройки бьют вдвое, и это осознанно — сила серии задаётся автором кита, а не выводится
        /// движком из числа маркеров. Половинки Монаха живут в его данных, а не в формуле.
        /// </remarks>
        public float HitDamageShare(int hitIndex)
        {
            if (_hitDamageShares == null || hitIndex < 0 || hitIndex >= _hitDamageShares.Length) return 1f;
            return _hitDamageShares[hitIndex];
        }

        /// <summary>
        /// Потолок длительности свинга этого юнита в сим-тиках. <c>0</c> = глобальный
        /// <c>SimConstants.MaxAttackAnimTicks</c>.
        /// </summary>
        /// <remarks>
        /// Заведено 2026-07-30 по вердикту Макса. Глобальный потолок остаётся дефолтом сознательно: одна
        /// длина заноса для всех = читаемость поля боя, когда игрок парсит восемь юнитов сразу. Override —
        /// исключение для китов, чей характер именно в редком тяжёлом ударе (ориентир 45–60 тиков).
        /// <para>
        /// Ловушка, из-за которой это поле нельзя крутить в одиночку: <b>пауза между атаками равна
        /// «интервал − свинг»</b>. Поднять потолок, не снизив скорость атаки того же юнита, — значит
        /// сократить или обнулить его окно ожидания, то есть получить обратное задуманному.
        /// </para>
        /// </remarks>
        public int AttackSwingTicks => _attackSwingTicks;

        /// <summary>Замах первого удара после разбега, долей от обычного. 1 = особого удара у юнита нет.</summary>
        public float ChargeAttackWindupMult => _chargeAttackWindupMult;
        public float ResourceOnHit => _resourceOnHit;
        public float MaxResourceGainPerSecond => _maxResourceGainPerSecond;
        public StatModifier[] Stats => _stats;
        public EffectData[] GrantedEffects => _grantedEffects;
        public AbilityData[] Abilities => _abilities;
        public AIPresetData AiPreset => _aiPreset;
        public Sprite Icon => _icon;
        public TagData[] InfoTags => _infoTags;

        /// <summary>
        /// Профиль поведения для сима. Источник — назначенный <see cref="AiPreset"/>; если пресет ещё не
        /// назначен (переходный период миграции) — легаси inline-профиль. Комбат читает только это.
        /// </summary>
        public AIProfile Ai => _aiPreset != null ? _aiPreset.Profile : _ai;

        // Цветов у юнита больше нет НИ ОДНОГО, и роль осталась ровно ОДНА — VfxTone. Ею красится и тело,
        // и всё, чем юнит светит: ступень приглушения тела снята 05.08.2026 как второй владелец цвета.
        // Владелец значений один: палитра проекта (UI/Theme/tokens.*.uss → GuildmasterPalette). Резолв
        // цвета тела — в UnitColorRoles.Body, резолв свечения с HDR-множителями — в CombatColorPalette:
        // множители это авторинг фидбэка, и в снимок палитры значения больше единицы не едут.
    }

    /// <summary>
    /// Канал авто-атаки: удар растянут в поток тиков урона, который идёт МЕЖДУ замахом и хвостом
    /// (<c>AttackPhase.Channel</c>). Носитель — Десятина (кровавый поток дальней формы).
    /// </summary>
    /// <remarks>
    /// <b>Здесь нет ни урона тика, ни его частоты — и это главное свойство модели</b> (решение Макса
    /// 2026-07-30). Тик канала есть обычный удар: бьёт <c>AutoAttackDamage</c>, интервал между тиками —
    /// <c>1 / AttackSpeed</c>, «скорость атаки = расстояние между тиками». Поэтому DPS канала считается
    /// той же формулой, что у любого бойца, и классовый коридор из <c>ClassBalanceConfig</c> продолжает
    /// его судить без поправок.
    /// <para>Отдельное поле «DPS канала» было бы вторым владельцем величины, у которой владелец уже есть
    /// (пара статов), и первый же балансный прогон их развёл бы — так уже разошлись нормы в карточках.</para>
    /// <para><b>Чем канал платит:</b> замахом и хвостом, в которые он не бьёт вовсе. Средний DPS тем ниже
    /// нормы, чем чаще поток срывают, — и в этом вся цена непрерывности.</para>
    /// <para><b>Секунды, а не доли интервала атаки:</b> канал бывает длиннее интервала (в пределе — «пока
    /// цель жива»), поэтому доля от интервала для него невыразима.</para>
    /// </remarks>
    [Serializable]
    public struct AttackChannel
    {
        [Tooltip("Длительность потока, сек. 0 = канала у кита нет (обычная одномоментная атака).")]
        [Min(0f)] public float DurationSeconds;

        [Tooltip("Замах перед потоком, сек. 0 = штатный расчёт замаха из скорости атаки и клипа. " +
                 "Своё число нужно потому, что штатный замах клампится интервалом атаки, а вход в канал " +
                 "по смыслу длиннее одного интервала.")]
        [Min(0f)] public float WindupSeconds;

        [Tooltip("Сворачивание потока, сек: юнит занят и не бьёт. Живёт ЗДЕСЬ, а не в общем " +
                 "AttackRecoverySeconds кита, потому что длинный хвост — свойство канала: тот же кит в " +
                 "ближней форме бьёт короткими выпадами и такого хвоста не имеет.")]
        [Min(0f)] public float RecoverySeconds;

        /// <summary>Канал у этого кита есть (длительность задана).</summary>
        public bool Exists => DurationSeconds > 0f;

        /// <summary>Кит бьёт обычными одномоментными ударами.</summary>
        public static AttackChannel None =>
            new AttackChannel { DurationSeconds = 0f, WindupSeconds = 0f, RecoverySeconds = 0f };
    }
}
