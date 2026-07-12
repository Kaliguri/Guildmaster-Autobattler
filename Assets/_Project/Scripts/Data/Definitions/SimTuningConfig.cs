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
        [Tooltip("Радиус тела = Size × это (мировые ед.). Size 1.0 → 0.575 (диаметр 1.15).")]
        [SerializeField] private float _bodyRadiusPerSize = 0.575f;
        [TabGroup("Tuning", "Разведение тел"), LabelText("Сила разведения")]
        [Tooltip("Доля перекрытия, устраняемая за тик (1 = жёстко за тик).")]
        [SerializeField] private float _separationStrength = 0.5f;
        [TabGroup("Tuning", "Разведение тел"), SuffixLabel("итераций", overlay: true), LabelText("Количество итераций")]
        [Tooltip("Проходов разделения за тик (больше = жёстче и дороже).")]
        [SerializeField] private int _separationIterations = 1;
        [TabGroup("Tuning", "Разведение тел"), LabelText("Масштаб для союзников")]
        [Tooltip("Множитель расталкивания союзников (0..1); враги всегда на полную.")]
        [SerializeField] private float _separationSameTeamScale = 0.35f;

        [TabGroup("Tuning", "Снаряды"), LabelText("Множитель радиуса попадания")]
        [Tooltip("Радиус коллизии снаряда/хил-снаряда = Size × это.")]
        [SerializeField] private float _projectileHitRadiusFactor = 0.25f;
        [TabGroup("Tuning", "Снаряды"), SuffixLabel("м", overlay: true), LabelText("Запас до удаления")]
        [Tooltip("Отступ деспавна снаряда за границами арены (мировые ед.).")]
        [SerializeField] private float _projectileDespawnMargin = 5f;

        [TabGroup("Tuning", "AI и цель"), LabelText("Множитель отхода при кайтинге")]
        [Tooltip("Fallback-полоса кайта при незаданных дистанциях: flee = AttackRange × это.")]
        [SerializeField] private float _kiteFleeFactor = 0.6f;
        [TabGroup("Tuning", "AI и цель"), SuffixLabel("м", overlay: true), LabelText("Глобальный радиус поиска")]
        [Tooltip("«Глобальный» радиус поиска целей (метка/ближайший враг) на масштабе арены.")]
        [SerializeField] private float _globalSearchRadius = 500f;

        [TabGroup("Tuning", "Побег"), LabelText("Вес отталкивания от врагов")]
        [Tooltip("Насколько побег отталкивается от центроида ближних врагов. Основной драйвер направления.")]
        [SerializeField] private float _fleeThreatWeight = 1f;
        [TabGroup("Tuning", "Побег"), LabelText("Вес притяжения к тылу")]
        [Tooltip("Насколько побег тянет к своей стороне (Team 0 → влево, 1 → вправо). Держать < веса врагов.")]
        [SerializeField] private float _fleeHomeWeight = 0.5f;
        [TabGroup("Tuning", "Побег"), LabelText("Вес избегания стен")]
        [Tooltip("Насколько сильно побег отворачивает от стен в пределах отступа (гасит углы/прижимания).")]
        [SerializeField] private float _fleeWallWeight = 1.5f;
        [TabGroup("Tuning", "Побег"), SuffixLabel("м", overlay: true), LabelText("Отступ избегания стен")]
        [Tooltip("Дистанция до стены, с которой включается превентивное избегание.")]
        [SerializeField] private float _fleeWallMargin = 2.5f;
        [TabGroup("Tuning", "Побег"), SuffixLabel("м", overlay: true), LabelText("Радиус центроида угрозы")]
        [Tooltip("Радиус, в котором враги усредняются в центр масс для направления побега (иначе — ближайший).")]
        [SerializeField] private float _fleeThreatRadius = 6f;
        [TabGroup("Tuning", "Побег"), LabelText("Вес бокового ухода кайтера")]
        [Tooltip("Боковой (тангенциальный) уход при кайте: дуга вокруг цели вместо пятящегося отхода. 0 = выкл.")]
        [SerializeField] private float _kiteStrafeWeight = 0.35f;

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
            _kiteStrafeWeight);
    }
}
