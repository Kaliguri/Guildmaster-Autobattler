using Guildmaster.Core.Simulation;
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
        [Header("Body separation (local avoidance)")]
        [Tooltip("Радиус тела = Size × это (мировые ед.). Size 1.0 → 0.575 (диаметр 1.15).")]
        [SerializeField] private float _bodyRadiusPerSize = 0.575f;
        [Tooltip("Доля перекрытия, устраняемая за тик (1 = жёстко за тик).")]
        [SerializeField] private float _separationStrength = 0.5f;
        [Tooltip("Проходов разделения за тик (больше = жёстче и дороже).")]
        [SerializeField] private int _separationIterations = 1;
        [Tooltip("Множитель расталкивания союзников (0..1); враги всегда на полную.")]
        [SerializeField] private float _separationSameTeamScale = 0.35f;

        [Header("Projectiles")]
        [Tooltip("Радиус коллизии снаряда/хил-снаряда = Size × это.")]
        [SerializeField] private float _projectileHitRadiusFactor = 0.25f;
        [Tooltip("Отступ деспавна снаряда за границами арены (мировые ед.).")]
        [SerializeField] private float _projectileDespawnMargin = 5f;

        [Header("AI / targeting")]
        [Tooltip("Fallback-полоса кайта при незаданных дистанциях: flee = AttackRange × это.")]
        [SerializeField] private float _kiteFleeFactor = 0.6f;
        [Tooltip("«Глобальный» радиус поиска целей (метка/ближайший враг) на масштабе арены.")]
        [SerializeField] private float _globalSearchRadius = 500f;

        /// <summary>Снять иммутабельный снапшот для бейка на старте боя.</summary>
        public SimTuning ToSnapshot() => new SimTuning(
            _bodyRadiusPerSize,
            _separationStrength,
            _separationIterations,
            _separationSameTeamScale,
            _projectileHitRadiusFactor,
            _projectileDespawnMargin,
            _kiteFleeFactor,
            _globalSearchRadius);
    }
}
