using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Вражеский юнит: боевой кит (<see cref="UnitData"/>) + мета врага (очки опасности, награда).
    /// Сим не читает мету — она для превью боя и бюджетов генерации (вики «13» §3.1). Ассеты врагов
    /// авторятся при строительстве флоу; можно копией кита мементо с ослабленными статами.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Enemy", fileName = "Enemy")]
    public sealed class EnemyData : UnitData
    {
        [Header("Species (стат-скейлы вида/подвида)")]
        [Tooltip("Вид врага (Гоблины/Звери/Големы) — стат-скейлы поверх классовой базы (каскад класс→вид→подвид→юнит). Пусто = вид не масштабирует.")]
        [SerializeField] private SpeciesData _species;

        [Tooltip("Подвид (Северные/Степные гоблины) — малые скейлы поверх вида. Задел; пусто = без подвида.")]
        [SerializeField] private SpeciesData _subspecies;

        [Header("Enemy meta")]
        [Tooltip("«Очки опасности» — ГД-метрика для превью опасности боя и бюджетов генерации; сим её не читает.")]
        [SerializeField] private int _threatPoints;

        [Tooltip("Золото за убийство/бой (если по дизайну; черновик).")]
        [SerializeField] private int _goldBounty;

        public SpeciesData Species => _species;
        public SpeciesData Subspecies => _subspecies;
        public int ThreatPoints => _threatPoints;
        public int GoldBounty => _goldBounty;
    }
}
