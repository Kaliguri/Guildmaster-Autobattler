using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Вражеский юнит: боевой кит (<see cref="UnitData"/>) + мета врага (очки опасности, награда).
    /// Сим не читает мету — она для превью боя и бюджетов генерации (вики «13» §3.1). Ассеты врагов
    /// авторятся при строительстве флоу; можно копией кита реликвии с ослабленными статами.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Enemy", fileName = "Enemy")]
    public sealed class EnemyData : UnitData
    {
        [Header("Enemy meta")]
        [Tooltip("«Очки опасности» — ГД-метрика для превью опасности боя и бюджетов генерации; сим её не читает.")]
        [SerializeField] private int _threatPoints;

        [Tooltip("Золото за убийство/бой (если по дизайну; черновик).")]
        [SerializeField] private int _goldBounty;

        public int ThreatPoints => _threatPoints;
        public int GoldBounty => _goldBounty;
    }
}
