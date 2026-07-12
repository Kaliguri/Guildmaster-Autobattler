using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Общий боевой кит всего, что выходит на арену (вики «13» §3.1). Наследники — <see cref="RelicData"/>
    /// (мета игрока) и <see cref="EnemyData"/> (мета врага). Сим и <c>RuntimeUnitFactory</c>/
    /// <c>BattleSetupBuilder</c> работают с этим базовым типом — им всё равно, кто перед ними.
    /// <para>Поля кита перенесены из <see cref="RelicData"/> БЕЗ смены имён (сериализация сохранена).</para>
    /// </summary>
    public abstract class UnitData : ContentDefinition
    {
        [Header("Combat categories")]
        [SerializeField] private DamageType _damageType = DamageType.Physical;
        [SerializeField] private AttackType _attackType = AttackType.Melee;
        [SerializeField] private ResourceType _resourceType = ResourceType.None;

        [Header("Visual (Phase 3)")]
        [Tooltip("Набор спрайт-кадров. Сим/фабрика читают отсюда кадр контакта авто-атаки для windup (вики «14»). null = статичный фолбэк (мгновенный удар).")]
        [SerializeField] private UnitVisual _visual;

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

        public DamageType DamageType => _damageType;
        public AttackType AttackType => _attackType;
        public ResourceType ResourceType => _resourceType;
        public UnitVisual Visual => _visual;
        public AreaShape AutoAttackShape => _autoAttackShape;
        public float AutoAttackWidth => _autoAttackWidth;
        public EffectData[] AutoAttackEffects => _autoAttackEffects;
        public bool CanAttackWhileMoving => _canAttackWhileMoving;
        public float MovingAttackSpeedPenaltyPct => _movingAttackSpeedPenaltyPct;
        public float AttackRecoverySeconds => _attackRecoverySeconds;
        public float ResourceOnHit => _resourceOnHit;
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
    }
}
