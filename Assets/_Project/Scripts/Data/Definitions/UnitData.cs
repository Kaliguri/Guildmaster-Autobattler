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

        [Tooltip("Тинт тела (умножается на спрайт). White = «не задан» → дев-фолбэк: стабильный оттенок от " +
                 "имени, чтобы placeholder-болванки различались. Когда появится свой спрайт — задать акцент " +
                 "или оставить White. ЕДИНЫЙ источник цвета: и бой, и карточка инвентаря берут ResolveBodyTint().")]
        [SerializeField] private Color _tint = Color.white;

        [Tooltip("Палитра ЭФФЕКТОВ этого юнита: искры, всплеск каста, контур, снаряд и его след. " +
                 "Это ДИАПАЗОН, а не один цвет — каждая частица берёт случайный оттенок между концами " +
                 "градиента (жёлто-белые искры вразнобой, а не одинаково белые). Ровно один цвет = обе " +
                 "точки одинаковые. HDR: яркость >1 ловит bloom. Пусто = тинт тела.")]
        [GradientUsage(true)] [SerializeField] private Gradient _vfxGradient;

        [Header("Auto-attack shape (Phase 3)")]
        [Tooltip("Форма авто-атаки: None = одиночная цель; Line = линия перед юнитом (несколько целей, «Размашистый выпад»).")]
        [SerializeField] private AreaShape _autoAttackShape = AreaShape.None;

        [Tooltip("Ширина линии авто-атаки (мировые единицы), для Line. Длина линии = AttackRange.")]
        [SerializeField] private float _autoAttackWidth = 1f;

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
        public Color Tint => _tint;
        public AreaShape AutoAttackShape => _autoAttackShape;
        public float AutoAttackWidth => _autoAttackWidth;
        public EffectData[] AutoAttackEffects => _autoAttackEffects;
        public bool CanAttackWhileMoving => _canAttackWhileMoving;
        public float MovingAttackSpeedPenaltyPct => _movingAttackSpeedPenaltyPct;
        public float AttackRecoverySeconds => _attackRecoverySeconds;
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

        /// <summary>
        /// Итоговый цвет тела для рендера — ЕДИНЫЙ источник и для боя (<c>UnitView.SetTint</c>), и для
        /// карточки инвентаря (<c>RelicCardVisualRig</c>). Явно заданный <see cref="Tint"/> (не White) идёт
        /// как есть; White трактуется как «не задан» → дев-фолбэк: стабильный HSV-оттенок от имени SO
        /// (различает placeholder-болванки, как раньше делал только <c>CombatPresenter.TintFor</c>). Убирает
        /// рассинхрон «в бою тинт есть, в карточке — нет».
        /// </summary>
        public Color ResolveBodyTint()
        {
            if (_tint != Color.white) return _tint;
            float hue = (Mathf.Abs(name.GetHashCode()) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.5f, 1f);
        }

        /// <summary>
        /// Градиент эффектов юнита — единый ответ на «каким светит ЭТОТ боец». Не задан → одноцветный из
        /// тинта тела, чтобы у любого юнита цвет эффектов был осмысленным без ручной настройки.
        /// <para>Возвращается КЭШИРОВАННЫЙ объект: <see cref="Gradient"/> — класс, и собирать его на каждом
        /// касте значило бы мусорить в бою.</para>
        /// </summary>
        public Gradient ResolveVfxGradient()
        {
            if (_vfxGradient != null && _vfxGradient.colorKeys != null && _vfxGradient.colorKeys.Length > 0)
                return _vfxGradient;

            if (_fallbackGradient == null)
            {
                Color tint = ResolveBodyTint();
                _fallbackGradient = new Gradient();
                _fallbackGradient.SetKeys(
                    new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
            return _fallbackGradient;
        }

        /// <summary>Один цвет эффектов — начало градиента. Там, где градиенту негде развернуться (тело снаряда).</summary>
        public Color ResolveVfxColor() => ResolveVfxGradient().Evaluate(0f);

        private Gradient _fallbackGradient;   // не сериализуется: выводится из тинта при первом спросе
    }
}
