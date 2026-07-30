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

        [Tooltip("Школа урона по умолчанию (гасится соответствующей бронёй). Способности могут переопределять её у себя.")]
        [FormerlySerializedAs("_damageType")]
        [SerializeField] private DamageSchool _damageSchool = DamageSchool.Physical;

        [Tooltip("Физический подтип автоатаки (Дробящий/Режущий/Колющий) — при школе Physical. Питает тег быстрого чтения; None = не задан.")]
        [SerializeField] private PhysicalSubtype _physicalSubtype = PhysicalSubtype.None;

        [Tooltip("Магический элемент автоатаки (Огонь/Лёд/Молния/Аркана) — при школе Magical. Питает тег быстрого чтения; None = не задан.")]
        [SerializeField] private MagicElement _magicElement = MagicElement.None;

        [Tooltip("Сродство урона по умолчанию (Яд/Свет/Тьма). Бронёй не гасится — взаимодействует с типом существа цели.")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

        [Tooltip("Тип существа САМОГО юнита. Определяет, как по нему бьют сродства (Нежить иммунна к Яду, уязвима к Свету и т.д.).")]
        [SerializeField] private CreatureType _creatureType = CreatureType.Living;

        [SerializeField] private AttackType _attackType = AttackType.Melee;
        [SerializeField] private ResourceType _resourceType = ResourceType.None;

        [Header("Visual (Phase 3)")]
        [Tooltip("Набор спрайт-кадров. Сим/фабрика читают отсюда кадр контакта авто-атаки для windup (вики «14»). null = статичный фолбэк (мгновенный удар).")]
        [SerializeField] private UnitVisual _visual;

        [Tooltip("Свой префаб визуала юнита (с настроенным Animator и реальным размером ПРЯМО в префабе). " +
                 "Сейчас всем можно прицепить единый placeholder, позже — индивидуальные. Пусто = дефолтный из презентера.")]
        [SerializeField] private GameObject _viewPrefab;

        [Tooltip("Приглушение тела: различитель тех, кто носит ОДИН спрайт. Один юнит из группы остаётся " +
                 "None и показывает арт как есть, остальные берут ступень. Тинт умножается на готовый арт, " +
                 "поэтому перекрасить им персонажа нельзя — это работа Palette Remapper. " +
                 "Свой арт → всегда None (сторож — UnitTintPolicyTests).")]
        [SerializeField] private BodyShade _bodyShade = BodyShade.None;

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
                 "тычок. 0 = взять из кадров UnitVisual (покадровые юниты так и делают). ЗАДАВАТЬ ОБЯЗАТЕЛЬНО " +
                 "юнитам без UnitVisual (скелетный риг): кадров у них нет, расчёт падает на телеграф-пол в " +
                 "3 тика, и клип атаки скрабится в 0.1 с — удар прилетает почти мгновенно и выглядит рвано.")]
        [SerializeField, Range(0f, 1f)] private float _windupShare;

        [Tooltip("Замах ПЕРВОГО удара после разбега, долей от обычного: 1 = такой же (особого удара нет), " +
                 "1.5 = в полтора раза длиннее (размашистый удар с ходу — дольше телеграф, весомее вход в " +
                 "бой), 0.6 = короче (выпад на скорости). Тратится одним ударом: добежал разбегом — ударил — " +
                 "дальше бьёт как обычно. Значение клампится теми же границами, что обычный замах, поэтому " +
                 "не может ни выйти за интервал атаки, ни опуститься ниже телеграф-пола.")]
        [SerializeField] private float _chargeAttackWindupMult = 1f;

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
        public DamageSchool DamageSchool => _damageSchool;
        public PhysicalSubtype PhysicalSubtype => _physicalSubtype;
        public MagicElement MagicElement => _magicElement;
        public DamageAffinity Affinity => _affinity;

        /// <summary>Тип урона автоатаки юнита (прямые поля источника, без override).</summary>
        public DamageType ResolveAutoAttackDamageType()
            => new DamageType(_damageSchool, _physicalSubtype, _magicElement, _affinity);
        public CreatureType CreatureType => _creatureType;
        public AttackType AttackType => _attackType;
        public ResourceType ResourceType => _resourceType;
        public UnitVisual Visual => _visual;
        public GameObject ViewPrefab => _viewPrefab;
        /// <summary>
        /// Ступень приглушения тела. Цвет из неё достаёт <c>UnitColorRoles.Shade</c> — здесь только
        /// решение автора, потому что владелец цвета один и это палитра.
        /// </summary>
        public BodyShade BodyShade => _bodyShade;

        /// <summary>Оттенок свечения юнита; цвет по роли отдаёт <c>CombatColorPalette</c>.</summary>
        public UnitTone VfxTone => _vfxTone;

        public AreaShape AutoAttackShape => _autoAttackShape;
        public float AutoAttackWidth => _autoAttackWidth;

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

        /// <summary>Доля свинга до кадра контакта (0..1). 0 = считать из кадров <see cref="UnitVisual"/>.</summary>
        public float WindupShare => _windupShare;

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

        // Цветов у юнита больше нет НИ ОДНОГО — только роли выше (BodyShade / VfxTone). Владелец значений
        // один: палитра проекта (UI/Theme/tokens.*.uss → GuildmasterPalette). Резолв тинта — в
        // UnitColorRoles.Shade, резолв свечения с HDR-множителями — в CombatColorPalette: множители это
        // авторинг фидбэка, и в снимок палитры значения больше единицы не едут.
    }
}
