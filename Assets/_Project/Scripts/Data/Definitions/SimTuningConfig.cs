using Guildmaster.Core.Simulation;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Балансные тюнинг-ручки симуляции (вики «13» §3.4). Единственный экземпляр на проект; боевой скоуп
    /// печёт из него иммутабельный <see cref="SimTuning"/>-снапшот на старте боя (§4.1). Из тика читается
    /// снапшот, не этот SO. Дефолты полей = <see cref="SimTuning.Default"/> — при рассинхроне падает тест-страховка.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Config/Sim Tuning Config", fileName = "SimTuningConfig")]
    public sealed class SimTuningConfig : ScriptableObject
    {
        [TabGroup("Tuning", "Разведение тел"), SuffixLabel("м на Size", overlay: true), LabelText("Радиус тела")]
        [Tooltip("Радиус тела = Size × это (мировые ед.). Size 1.0 → 0.3 (диаметр 0.6).")]
        [SerializeField] private float _bodyRadiusPerSize = SimTuning.Default.BodyRadiusPerSize;
        [TabGroup("Tuning", "Разведение тел"), LabelText("Сила разведения")]
        [Tooltip("Доля перекрытия, устраняемая за тик (1 = жёстко за тик).")]
        [SerializeField] private float _separationStrength = SimTuning.Default.SeparationStrength;
        [TabGroup("Tuning", "Разведение тел"), SuffixLabel("итераций", overlay: true), LabelText("Количество итераций")]
        [Tooltip("Проходов разделения за тик (больше = жёстче и дороже).")]
        [SerializeField] private int _separationIterations = SimTuning.Default.SeparationIterations;
        [TabGroup("Tuning", "Разведение тел"), LabelText("Масштаб для союзников")]
        [Tooltip("Множитель расталкивания союзников (0..1); враги всегда на полную.")]
        [SerializeField] private float _separationSameTeamScale = SimTuning.Default.SeparationSameTeamScale;

        [TabGroup("Tuning", "Снаряды"), LabelText("Множитель радиуса попадания")]
        [Tooltip("Радиус коллизии снаряда/хил-снаряда = Size × это.")]
        [SerializeField] private float _projectileHitRadiusFactor = SimTuning.Default.ProjectileHitRadiusFactor;
        [TabGroup("Tuning", "Снаряды"), SuffixLabel("м", overlay: true), LabelText("Запас до удаления")]
        [Tooltip("Отступ деспавна снаряда за границами арены (мировые ед.).")]
        [SerializeField] private float _projectileDespawnMargin = SimTuning.Default.ProjectileDespawnMargin;

        [TabGroup("Tuning", "AI и цель"), LabelText("Множитель отхода при кайтинге")]
        [Tooltip("Fallback-полоса кайта при незаданных дистанциях: flee = AttackRange × это.")]
        [SerializeField] private float _kiteFleeFactor = SimTuning.Default.KiteFleeFactor;
        [TabGroup("Tuning", "AI и цель"), SuffixLabel("м", overlay: true), LabelText("Глобальный радиус поиска")]
        [Tooltip("«Глобальный» радиус поиска целей (метка/ближайший враг) на масштабе арены.")]
        [SerializeField] private float _globalSearchRadius = SimTuning.Default.GlobalSearchRadius;

        [TabGroup("Tuning", "Побег"), LabelText("Вес отталкивания от врагов")]
        [Tooltip("Насколько побег отталкивается от центроида ближних врагов. Основной драйвер направления.")]
        [SerializeField] private float _fleeThreatWeight = SimTuning.Default.FleeThreatWeight;
        [TabGroup("Tuning", "Побег"), LabelText("Вес притяжения к тылу")]
        [Tooltip("Насколько побег тянет к своей стороне (Team 0 → влево, 1 → вправо). Держать < веса врагов.")]
        [SerializeField] private float _fleeHomeWeight = SimTuning.Default.FleeHomeWeight;
        [TabGroup("Tuning", "Побег"), LabelText("Вес избегания стен")]
        [Tooltip("Насколько сильно побег отворачивает от стен в пределах отступа (гасит углы/прижимания).")]
        [SerializeField] private float _fleeWallWeight = SimTuning.Default.FleeWallWeight;
        [TabGroup("Tuning", "Побег"), SuffixLabel("м", overlay: true), LabelText("Отступ избегания стен")]
        [Tooltip("Дистанция до стены, с которой включается превентивное избегание.")]
        [SerializeField] private float _fleeWallMargin = SimTuning.Default.FleeWallMargin;
        [TabGroup("Tuning", "Побег"), SuffixLabel("м", overlay: true), LabelText("Радиус центроида угрозы")]
        [Tooltip("Радиус, в котором враги усредняются в центр масс для направления побега (иначе — ближайший).")]
        [SerializeField] private float _fleeThreatRadius = SimTuning.Default.FleeThreatRadius;
        [TabGroup("Tuning", "Побег"), LabelText("Вес бокового ухода кайтера")]
        [Tooltip("Боковой (тангенциальный) уход при кайте: дуга вокруг цели вместо пятящегося отхода. 0 = выкл.")]
        [SerializeField] private float _kiteStrafeWeight = SimTuning.Default.KiteStrafeWeight;

        [TabGroup("Tuning", "Смещение"), SuffixLabel("ед/с", overlay: true), LabelText("Скорость полёта (дефолт)")]
        [Tooltip("Скорость отбрасывания по умолчанию, мировых единиц в секунду. Длительность полёта = дистанция ÷ скорость, поэтому дальний толчок держит цель в оглушении дольше. Источник может задать свою скорость в запросе смещения — «сколько тиков лететь» не настраивается нигде намеренно.")]
        [SerializeField] private float _displaceSpeedPerSecond = SimTuning.Default.DisplaceSpeedPerSecond;
        [TabGroup("Tuning", "Смещение"), LabelText("Ширина коридора «ядра»")]
        [Tooltip("Во сколько раз коридор летящего тела шире заданной ширины (1.25 = +25%). Толчок в плотный строй должен цеплять соседей, а не только тех, кто ровно на линии.")]
        [SerializeField] private float _cannonballWidthMult = SimTuning.Default.CannonballWidthMult;
        [TabGroup("Tuning", "Смещение"), LabelText("Урон об край арены")]
        [Tooltip("Доля урона толчка, добиваемая цели, впечатанной в край арены (1 = ещё раз столько же). 0 = удар о стену безвреден.")]
        [SerializeField] private float _wallImpactDamageMult = SimTuning.Default.WallImpactDamageMult;
        [TabGroup("Tuning", "Смещение"), SuffixLabel("с", overlay: true), LabelText("Лежит после удара о стену")]
        [Tooltip("Сколько цель лежит оглушённой, впечатавшись в край арены. Полёт при этом СТОИТ (скольжение вдоль стены выглядит сломанным), а реактивы «на конец смещения» — например телепорт Монаха — срабатывают уже после лежания.")]
        [SerializeField] private float _wallImpactStunSeconds = SimTuning.Default.WallImpactStunSeconds;

        [TabGroup("Tuning", "Овертайм"), SuffixLabel("с", overlay: true), LabelText("Начало овертайма")]
        [Tooltip("С какой секунды боя урон начинает расти. Медиана боя — 20-29 с, так что до порога доживает только клинч.")]
        [SerializeField] private float _overtimeStartSeconds = SimTuning.Default.OvertimeStartSeconds;
        [TabGroup("Tuning", "Овертайм"), LabelText("Прибавка урона за секунду")]
        [Tooltip("Насколько растёт НАНОСИМЫЙ урон за каждую секунду сверх порога (0.05 = +5%). Лечение и щиты не растут — этим клинч и ломается. 0 = овертайм выключен.")]
        [SerializeField] private float _overtimeDamagePerSecond = SimTuning.Default.OvertimeDamagePerSecond;

        /// <summary>Снять иммутабельный снапшот для бейка на старте боя.</summary>
        public SimTuning ToSnapshot() => new SimTuning(
            _bodyRadiusPerSize,
            _separationStrength,
            _separationIterations,
            _separationSameTeamScale,
            _projectileHitRadiusFactor,
            _projectileDespawnMargin,
            _kiteFleeFactor,
            _globalSearchRadius,
            _fleeThreatWeight,
            _fleeHomeWeight,
            _fleeWallWeight,
            _fleeWallMargin,
            _fleeThreatRadius,
            _kiteStrafeWeight,
            _displaceSpeedPerSecond,
            _cannonballWidthMult,
            _wallImpactDamageMult,
            _wallImpactStunSeconds,
            _overtimeStartSeconds,
            _overtimeDamagePerSecond);
    }
}
