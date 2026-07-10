using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Чемпион» — стат-блок и боевые категории юнита. Из него + <see cref="StatsConfig"/>
    /// фабрика собирает рантайм-юнита (вики «10» §4.2, §5.2).
    /// </summary>
    /// <remarks>СКЕЛЕТ Фазы 1: стат-блок + категории. Активки/пассивки (через <see cref="EffectData"/>) — Фаза 2.</remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Relic", fileName = "Relic")]
    public sealed class RelicData : ContentDefinition
    {
        [Header("Identity")]
        [SerializeField] private string[] _tags;

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

        [Header("Resource gain (Phase 3)")]
        [Tooltip("Ресурс (мана) за авто-атаку, × ResourceGainEff, клампится к MaxResource. 0 = не копит от ударов. Копейщик = 5.")]
        [SerializeField] private float _resourceOnHit;

        [Header("Deployment (Arena)")]
        [Tooltip("Доступ к расширенным зонам расстановки (Extended): манёвренные чемпионы/убийцы. false = только базовые зоны (вики «15» §6).")]
        [SerializeField] private bool _canUseExtendedDeployment;

        [Header("Base stat block")]
        [Tooltip("Базовые модификаторы реликвии. Накладываются поверх дефолтов StatsConfig при сборке юнита.")]
        [SerializeField] private StatModifier[] _stats;

        // --- Фаза 2: даруемые эффекты/способности ---
        [Header("Passives")]
        [Tooltip("Пассивные эффекты (накладываются при сборке юнита, обычно постоянные).")]
        [SerializeField] private EffectData[] _grantedEffects;

        [Header("Active abilities")]
        [Tooltip("Активные способности (кулдаун/ресурс). Слотов — по редкости (Common 1 → Rare 2 → Epic 3).")]
        [SerializeField] private AbilityData[] _abilities;

        // --- Фаза 3: профиль AI ---
        [Header("AI profile (Phase 3)")]
        [Tooltip("Параметризованный пресет поведения (кого/когда/где). Интерпретируется ProfileBrain (вики «13» §2.1).")]
        [SerializeField] private AIProfile _ai = new AIProfile();

        public string[] Tags => _tags;
        public UnitVisual Visual => _visual;
        public DamageType DamageType => _damageType;
        public AttackType AttackType => _attackType;
        public ResourceType ResourceType => _resourceType;
        public AreaShape AutoAttackShape => _autoAttackShape;
        public float AutoAttackWidth => _autoAttackWidth;
        public EffectData[] AutoAttackEffects => _autoAttackEffects;
        public bool CanAttackWhileMoving => _canAttackWhileMoving;
        public float MovingAttackSpeedPenaltyPct => _movingAttackSpeedPenaltyPct;
        public float ResourceOnHit => _resourceOnHit;
        public bool CanUseExtendedDeployment => _canUseExtendedDeployment;
        public StatModifier[] Stats => _stats;
        public EffectData[] GrantedEffects => _grantedEffects;
        public AbilityData[] Abilities => _abilities;
        public AIProfile Ai => _ai;
    }
}
