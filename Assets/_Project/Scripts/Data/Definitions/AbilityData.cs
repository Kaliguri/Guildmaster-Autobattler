using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Как выбирается цель активной способности (полный AI-таргетинг — Фаза 3).</summary>
    public enum AbilityTargetMode
    {
        /// <summary>На себя.</summary>
        Self = 0,

        /// <summary>Ближайший враг (в Фазе 2 — текущая цель автоатаки).</summary>
        NearestEnemy = 1,

        /// <summary>Ближайший союзник (для бафф/хил-способностей).</summary>
        NearestAlly = 2,

        /// <summary>Союзник с наименьшим HP% — глобально, без ограничения дальности (хилер-ульта «Длань жизни»).</summary>
        LowestHpAlly = 3,

        /// <summary>Все живые враги с тегом <see cref="AbilityData.TriggerTag"/> — глобально, без ограничения дальности (масс-стан «Ледяные оковы» по «Заморозке»). Цель не одиночная.</summary>
        AllEnemiesWithTag = 4,

        /// <summary>Все живые союзники в <see cref="AbilityData.AreaRadius"/> вокруг кастующего, включая его самого
        /// (групповой баф «Командный клич»; лечение — если задана хил-нагрузка). Цель не одиночная.</summary>
        AlliesInRadius = 5,
    }

    /// <summary>
    /// Определение активной способности реликвии: эффекты при касте, кулдаун, стоимость ресурса,
    /// способ выбора цели. Сериализуется на <see cref="RelicData"/> (вики «6» §1, «12» §2.4).
    /// Пассивки — это <see cref="RelicData.GrantedEffects"/> с постоянной длительностью, отдельной
    /// сущности не требуют.
    /// </summary>
    [Serializable]
    public sealed class AbilityData
    {
        [SerializeField] private string _id;

        [Tooltip("Эффекты, накладываемые на цель при касте.")]
        [SerializeField] private EffectData[] _effects;

        [Tooltip("Эффекты, накладываемые на САМОГО кастующего при применении («Стальной вихрь»: щит от нанесённого урона). Не зависят от формы способности.")]
        [SerializeField] private EffectData[] _selfEffects;

        [Tooltip("Базовый кулдаун, сек. Фактический = base × CooldownEff кастующего.")]
        [SerializeField] private float _baseCooldown = 5f;

        [Tooltip("Стоимость ресурса за каст (0 = бесплатно).")]
        [SerializeField] private float _resourceCost;

        [SerializeField] private AbilityTargetMode _targetMode = AbilityTargetMode.NearestEnemy;

        [Header("Direct damage (Phase 3)")]
        [Tooltip("Множитель прямого урона от AutoAttackDamage кастующего. 0 = только эффекты (поведение Ф2). «Стальной вихрь» = 3.")]
        [SerializeField] private float _damageMultiplier;

        [Tooltip("Школа урона способности. Inherit = школа юнита-кастера (ГДД «8»: школа задаётся каждой атаке/способности отдельно).")]
        [SerializeField] private DamageSchoolOverride _schoolOverride = DamageSchoolOverride.Inherit;

        [Tooltip("Физ-подтип урона способности (Дробящий/Режущий/Колющий) — при школе Physical. Inherit = подтип кастера. Копейщик: ульта Slash при автоатаке Pierce.")]
        [SerializeField] private PhysicalSubtypeOverride _physicalSubtypeOverride = PhysicalSubtypeOverride.Inherit;

        [Tooltip("Магический элемент урона способности (Огонь/Лёд/Молния/Аркана) — при школе Magical. Inherit = элемент кастера.")]
        [SerializeField] private MagicElementOverride _magicElementOverride = MagicElementOverride.Inherit;

        [Tooltip("Сродство урона способности (Яд/Свет/Тьма). Inherit = сродство юнита-кастера.")]
        [SerializeField] private DamageAffinityOverride _affinityOverride = DamageAffinityOverride.Inherit;

        [Header("Area of effect (Phase 3)")]
        [Tooltip("Форма зоны поражения. None = одиночная цель по TargetMode (поведение Ф2).")]
        [SerializeField] private AreaShape _areaShape = AreaShape.None;

        [Tooltip("Радиус зоны для Circle (мировые единицы). Центр — кастующий (удар вокруг себя).")]
        [SerializeField] private float _areaRadius;

        [Header("Heal payload (Phase 3) — Светлый пастырь")]
        [Tooltip("Плоское лечение цели (X). >0 делает способность лечащей: Heal вместо урона. Итог × HealShieldDealtEff/TakenEff.")]
        [SerializeField] private float _healFlat;

        [Tooltip("Доля недостающего HP цели, добавляемая к лечению («Длань жизни» = 1.0 → долечивает до полного). >0 делает способность лечащей.")]
        [SerializeField] private float _healPctTargetMissingHp;

        [Tooltip("Эффект вместо разового лечения: накладывается на каждого лечимого союзника (Друид = HoT «Грибной покров»). Множитель лечения превращается в стаки эффекта. Задан → тоже делает способность лечащей.")]
        [SerializeField] private EffectData _healEffect;

        [Tooltip("Доля лечения, когда цель — САМ кастующий (Пастырь = 0.25: себе вчетверо хуже, чем союзнику). 1 = без разницы.")]
        [Range(0f, 1f)]
        [SerializeField] private float _selfHealFraction = 1f;

        [Header("Cast condition (blocks D/E, Phase 3)")]
        [Tooltip("Когда кастовать: Immediately = как только готова; EnemiesInRadius = врагов в радиусе ≥ CastConditionCount; AllyTargetHpBelowPct = HP% выбранной цели ≤ CastConditionHpPct.")]
        [SerializeField] private CastCondition _castCondition = CastCondition.Immediately;

        [Tooltip("X для EnemiesInRadius: минимум врагов в радиусе условия.")]
        [SerializeField] private int _castConditionCount = 1;

        [Tooltip("Радиус подсчёта врагов для условия каста. ≤ 0 = взять AreaRadius.")]
        [SerializeField] private float _castConditionRadius;

        [Tooltip("Порог HP% (0..1) для AllyTargetHpBelowPct: кастуем, когда HP% выбранной цели ≤ этого.")]
        [SerializeField] private float _castConditionHpPct = 0.5f;

        [Tooltip("Отмена условия (блок E): если HP% кастующего ≤ этого — кастуем независимо от условия. У лечащих способностей цель тогда = сам кастующий. 0 = выкл.")]
        [SerializeField] private float _castOverrideSelfHpPct;

        [Header("Tagged targeting (§9.10) — Криомант")]
        [Tooltip("Тег для AllEnemiesWithTag / EnemiesWithTagCount / ConsumesTriggerTag (Криомант = Frozen). None = не используется.")]
        [SerializeField] private EffectTag _triggerTag = EffectTag.None;

        [Tooltip("После наложения эффектов снять TriggerTag с цели (конверсия: «Ледяные оковы» превращают «Заморозку» в стан).")]
        [SerializeField] private bool _consumesTriggerTag;

        [Header("Cast time and channel (M3) — Копейщик, Маг молний, Барабанщик")]
        [Tooltip("Подготовка перед применением, сек. 0 = мгновенно (поведение всего текущего контента). Маг молний = 1.5. Ресурс и КД списываются в НАЧАЛЕ подготовки: прерывание контролем жжёт каст.")]
        [SerializeField] private float _castSeconds;

        [Tooltip("Длительность канала, сек: после подготовки нагрузка применяется периодически, пока канал держится. 0 = разовое применение. Барабанщик «Марш».")]
        [SerializeField] private float _channelSeconds;

        [Tooltip("Период срабатывания канала, сек (первое — сразу на старте канала). ≤ 0 = взять 1 с. Барабанщик лечит раз в секунду.")]
        [SerializeField] private float _channelTickSeconds = 1f;

        [Tooltip("Кастовать и держать канал НА ХОДУ (по образцу «Стрельбы на ходу» Рейнджера). Выкл = каст держит на месте, как авто-атака. Барабанщик марширует.")]
        [SerializeField] private bool _canMoveWhileCasting;

        [Header("Stat conversions (M4) — Убийца, Маг молний")]
        [Tooltip("Правила «стат юнита → параметр способности»: ускорение каста и кулдауна от скорости атаки, прибавка к множителю удара. Пусто = параметры берутся ровно из полей выше.")]
        [SerializeField] private AbilityStatScaling[] _statScalings;

        [Header("Summons (M10) — Некромант, Хранитель")]
        [Tooltip("Кого призывать. Пусто = способность не призывает. Статы призыва берутся из ЭТОГО ассета " +
                 "и множатся на SummonHealthEff/SummonDamageEff призывателя.")]
        [SerializeField] private UnitData _summonUnit;

        [Tooltip("Сколько призывает за один каст.")]
        [Min(1)]
        [SerializeField] private int _summonCount = 1;

        [Tooltip("Максимум ЖИВЫХ призывов от этой способности. Лимит достигнут — каст не идёт вовсе " +
                 "(мана и КД целы, игрок видит предел глазами). 0 = без лимита.")]
        [Min(0)]
        [SerializeField] private int _summonLimit = 3;

        [Tooltip("Срок жизни призыва, сек. 0 = бессрочно (обычный случай): живёт до конца боя или своей смерти.")]
        [Min(0f)]
        [SerializeField] private float _summonLifetimeSeconds;

        [Tooltip("Призыв умирает вместе с призывателем. Выкл = переживает его (земляной голем Мага бандитов).")]
        [SerializeField] private bool _summonDiesWithSummoner;

        [Header("Displacement (§9.9) — Монах")]
        [Tooltip("Отталкивает цель (Knockback) на DisplaceDistance; длительность полёта считается из дистанции. На линии полёта — урон-ядро.")]
        [SerializeField] private bool _displaces;

        [Tooltip("Дистанция отбрасывания (фиксированная, мировые единицы).")]
        [SerializeField] private float _displaceDistance = 4f;

        [Tooltip("Множитель урона-ядра от AutoAttackDamage кастующего (0 = без урона на линии).")]
        [SerializeField] private float _displaceDamageMult = 1f;

        [Tooltip("Ширина линии «ядра» (мировые единицы).")]
        [SerializeField] private float _displaceWidth = 1f;

        [Header("Visual")]
        [Tooltip("Слот визуала каста: проигрывается клип UnitVisual.SkillClip(этот слот). По умолчанию Skill1.")]
        [SerializeField] private SkillSlot _visualSlot = SkillSlot.Skill1;

        [Header("Info")]
        [Tooltip("Информационные теги скилла для тултипов.")]
        [SerializeField] private TagData[] _infoTags;

        public string Id => _id;
        public EffectData[] Effects => _effects;

        /// <summary>
        /// Эффекты на самого кастующего. Заведены отдельным списком, потому что <see cref="Effects"/>
        /// адресован ЦЕЛИ, и у круговых или масс-способностей цели вообще нет — «дай себе щит» иначе
        /// выразить нечем, кроме второй способности-пустышки.
        /// </summary>
        public EffectData[] SelfEffects => _selfEffects;
        public float BaseCooldown => _baseCooldown;
        public float ResourceCost => _resourceCost;
        public AbilityTargetMode TargetMode => _targetMode;

        public float DamageMultiplier => _damageMultiplier;
        public DamageSchoolOverride SchoolOverride => _schoolOverride;
        public PhysicalSubtypeOverride PhysicalSubtypeOverride => _physicalSubtypeOverride;
        public MagicElementOverride MagicElementOverride => _magicElementOverride;
        public DamageAffinityOverride AffinityOverride => _affinityOverride;

        /// <summary>Тип урона способности: override поверх типа урона кастера (Inherit = взять у него).</summary>
        public DamageType ResolveDamageType(UnitData caster)
        {
            DamageSchool school = DamageCategories.Resolve(_schoolOverride, caster.DamageSchool);
            PhysicalSubtype subtype = DamageCategories.Resolve(_physicalSubtypeOverride, caster.PhysicalSubtype);
            MagicElement element = DamageCategories.Resolve(_magicElementOverride, caster.MagicElement);
            DamageAffinity affinity = DamageCategories.Resolve(_affinityOverride, caster.Affinity);
            return new DamageType(school, subtype, element, affinity);
        }
        public AreaShape AreaShape => _areaShape;
        public float AreaRadius => _areaRadius;
        public float HealFlat => _healFlat;
        public float HealPctTargetMissingHp => _healPctTargetMissingHp;

        /// <summary>
        /// Эффект-нагрузка лечения: если задан, союзник получает ЕГО (со стаками по множителю лечения)
        /// вместо мгновенного восстановления HP. Плоский хил и процент при этом не отменяются — заданы
        /// оба, союзник получит и то, и то.
        /// </summary>
        public EffectData HealEffect => _healEffect;

        /// <summary>
        /// Во сколько раз слабее лечение, когда цель — сам кастующий. Отдавать выгоднее, чем брать:
        /// та же асимметрия, что у пассивки Пастыря (решение 2026-07-28), но для адресной способности.
        /// </summary>
        /// <remarks>
        /// Заведено вместо прежнего запрета «ульта не может целиться в себя»: запрет оставлял хилера
        /// без единственного инструмента ровно тогда, когда фокус переводили на него. Цена честнее
        /// невозможности — спасти себя можно, но вчетверо дороже.
        /// </remarks>
        public float SelfHealFraction => _selfHealFraction;

        /// <summary>Способность лечит (а не бьёт), если задана любая хил-нагрузка — мгновенная или эффектом.</summary>
        public bool IsHeal => _healFlat > 0f || _healPctTargetMissingHp > 0f || _healEffect != null;
        public CastCondition CastCondition => _castCondition;
        public int CastConditionCount => _castConditionCount;
        /// <summary>Радиус условия каста; при ≤ 0 откатывается к <see cref="AreaRadius"/>.</summary>
        public float CastConditionRadius => _castConditionRadius > 0f ? _castConditionRadius : _areaRadius;
        public float CastConditionHpPct => _castConditionHpPct;
        public float CastOverrideSelfHpPct => _castOverrideSelfHpPct;
        public EffectTag TriggerTag => _triggerTag;
        public bool ConsumesTriggerTag => _consumesTriggerTag;
        /// <summary>
        /// Подготовка перед применением, сек (0 = мгновенно). Цена платится на СТАРТЕ подготовки —
        /// решение Макса 2026-07-29: иначе контроль лишь задерживает каст, и телеграф не стоит ничего.
        /// </summary>
        public float CastSeconds => _castSeconds;

        /// <summary>Длительность канала, сек (0 = разовое применение после подготовки).</summary>
        public float ChannelSeconds => _channelSeconds;

        /// <summary>Период срабатывания канала, сек; при ≤ 0 — одна секунда.</summary>
        public float ChannelTickSeconds => _channelTickSeconds > 0f ? _channelTickSeconds : 1f;

        /// <summary>Способность кастуется на ходу — исключение из «каст держит на месте» (Q9, форма — поле ассета).</summary>
        public bool CanMoveWhileCasting => _canMoveWhileCasting;

        /// <summary>Способность занимает время: есть подготовка или канал (иначе применяется в тот же тик).</summary>
        public bool TakesTime => _castSeconds > 0f || _channelSeconds > 0f;

        /// <summary>Правила конвертации статов в параметры этой способности (M4).</summary>
        public AbilityStatScaling[] StatScalings => _statScalings;

        /// <summary>Кулдаун с учётом конвертаций статов носителя (до умножения на <c>CooldownEff</c>).</summary>
        public float ResolveCooldown(IStatReader stats) => Resolve(AbilityParameter.Cooldown, _baseCooldown, stats);

        /// <summary>Длительность подготовки с учётом конвертаций: скорость атаки укорачивает каст.</summary>
        public float ResolveCastSeconds(IStatReader stats) => Resolve(AbilityParameter.CastSeconds, _castSeconds, stats);

        /// <summary>Множитель прямого урона с учётом конвертаций («Удар из скрытности» растёт от AS).</summary>
        public float ResolveDamageMultiplier(IStatReader stats) => Resolve(AbilityParameter.DamageMultiplier, _damageMultiplier, stats);

        /// <summary>
        /// Свести все правила для одного параметра. Правила ОДНОГО параметра применяются подряд, в
        /// порядке ассета: порядок значим для обратной формы, поэтому он берётся из данных, а не из
        /// сортировки — иначе одно и то же содержимое давало бы разные числа между сборками.
        /// </summary>
        private float Resolve(AbilityParameter parameter, float baseValue, IStatReader stats)
        {
            if (_statScalings == null || _statScalings.Length == 0) return baseValue;

            float value = baseValue;
            for (int i = 0; i < _statScalings.Length; i++)
                if (_statScalings[i].Target == parameter) value = _statScalings[i].Apply(value, stats);

            return value;
        }

        /// <summary>Кого призывает способность. null = не призывает.</summary>
        public UnitData SummonUnit => _summonUnit;

        /// <summary>Сколько тел появляется за каст.</summary>
        public int SummonCount => _summonCount < 1 ? 1 : _summonCount;

        /// <summary>Максимум живых призывов от этой способности; 0 = без лимита.</summary>
        public int SummonLimit => _summonLimit;

        /// <summary>Срок жизни призыва в секундах; 0 = бессрочно.</summary>
        public float SummonLifetimeSeconds => _summonLifetimeSeconds;

        /// <summary>Призыв уходит вместе с призывателем.</summary>
        public bool SummonDiesWithSummoner => _summonDiesWithSummoner;

        /// <summary>Способность призывает тела на поле.</summary>
        public bool Summons => _summonUnit != null;

        public bool Displaces => _displaces;
        public float DisplaceDistance => _displaceDistance;
        public float DisplaceDamageMult => _displaceDamageMult;
        public float DisplaceWidth => _displaceWidth;
        public TagData[] InfoTags => _infoTags;

        /// <summary>Слот визуала каста (клип из <see cref="UnitVisual.SkillClip"/>).</summary>
        public SkillSlot VisualSlot => _visualSlot;
    }
}
