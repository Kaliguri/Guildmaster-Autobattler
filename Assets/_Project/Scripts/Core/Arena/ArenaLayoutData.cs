using System;
using System.Collections.Generic;

namespace Guildmaster.Core.Arena
{
    /// <summary>
    /// Неизменяемый снапшот арены на один бой: границы поля + зоны расстановки. Печётся из
    /// авторинга (ArenaLayoutAuthoring) или собирается тестами; отдаётся в симуляцию (<see cref="Bounds"/>)
    /// и в <see cref="DeploymentService"/> (<see cref="Zones"/>). Источник правды в рантайме — этот
    /// снапшот, а не сцена/префаб (вики «15» §2, §3).
    /// </summary>
    public sealed class ArenaLayoutData
    {
        /// <summary>Границы боевого поля.</summary>
        public ArenaBounds Bounds { get; }

        /// <summary>Зоны расстановки внутри поля.</summary>
        public IReadOnlyList<DeploymentZone> Zones { get; }

        /// <summary>
        /// Зона камеры: прямоугольник, за который не выходит ВИДИМАЯ область камеры (Overview/Action).
        /// Презентация (вики «16» §5), симуляции не касается. Не задана → совпадает с полем (<see cref="Bounds"/>).
        /// </summary>
        public Rect2D CameraZone { get; }

        public ArenaLayoutData(ArenaBounds bounds, IReadOnlyList<DeploymentZone> zones, Rect2D? cameraZone = null)
        {
            Bounds     = bounds;
            Zones      = zones ?? Array.Empty<DeploymentZone>();
            CameraZone = cameraZone ?? bounds.Rect;
        }

        /// <summary>Бесконечное поле без зон: дефолт, когда арена не задана (вики «15» §2).</summary>
        public static ArenaLayoutData Unbounded { get; } =
            new ArenaLayoutData(ArenaBounds.Unbounded, Array.Empty<DeploymentZone>());
    }
}
