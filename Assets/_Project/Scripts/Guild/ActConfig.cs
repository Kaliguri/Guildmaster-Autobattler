using UnityEngine;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Авторинг-обёртка параметров генерации акта (<see cref="MapGenConfig"/>) как ScriptableObject —
    /// дизайнер крутит глубину/ширину/зоны/якоря в инспекторе, не трогая код. Генератор и тесты работают
    /// на чистом POCO; SO лишь несёт его и валидирует. Не назначен в скоуп → фолбэк на дефолтный POCO
    /// (тот же приём, что у AudioCatalog/CombatFeelConfig). Позже — один ActConfig на акт кампании.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Act Config", fileName = "ActConfig")]
    public sealed class ActConfig : ScriptableObject
    {
        [Tooltip("Параметры генерации карты: глубина (Start + испытания + Boss), ширина колонок, зоны этажей и якоря.")]
        [SerializeField] private MapGenConfig _map = new MapGenConfig();

        /// <summary>Валидированный конфиг генерации карты для <see cref="MapGenerator"/>.</summary>
        public MapGenConfig ToGenConfig() => (_map ?? new MapGenConfig()).Validated();
    }
}
