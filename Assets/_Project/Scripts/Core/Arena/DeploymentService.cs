using UnityEngine;

namespace Guildmaster.Core.Arena
{
    /// <summary>
    /// Правила фазы расстановки: можно ли поставить юнита в точку. Работает над снапшотом арены
    /// (<see cref="ArenaLayoutData"/>). Чистая логика — единый источник правды и для UI, и для
    /// валидации команд на хосте (кооп host-authoritative, вики «15» §6). Право на Extended
    /// приходит извне флагом (<c>canUseExtended</c>) — сервис не знает про юниты/чемпионов.
    /// </summary>
    public sealed class DeploymentService
    {
        private readonly ArenaLayoutData _layout;

        public DeploymentService(ArenaLayoutData layout)
        {
            _layout = layout ?? ArenaLayoutData.Unbounded;
        }

        /// <summary>
        /// Можно ли поставить юнита стороны <paramref name="side"/> в точку <paramref name="pos"/>.
        /// Normal-зоны доступны всем; Extended — только при <paramref name="canUseExtended"/>.
        /// Свободное размещение: любая точка внутри разрешённой зоны (вики «15» §6).
        /// </summary>
        public bool CanPlace(Vector2 pos, DeploymentSide side, bool canUseExtended)
        {
            var zones = _layout.Zones;
            for (int i = 0; i < zones.Count; i++)
            {
                DeploymentZone z = zones[i];
                if (z.Side != side) continue;
                if (z.Tier == DeploymentTier.Extended && !canUseExtended) continue;
                if (z.Area.Contains(pos)) return true;
            }
            return false;
        }
    }
}
