namespace Guildmaster.Core.Arena
{
    /// <summary>Чья сторона зоны расстановки (вики «15» §3).</summary>
    public enum DeploymentSide
    {
        /// <summary>Зона игрока — куда игрок расставляет своих юнитов.</summary>
        Player,

        /// <summary>Зона врага — где стоят враги из энкаунтера.</summary>
        Enemy,
    }

    /// <summary>Ранг зоны расстановки.</summary>
    public enum DeploymentTier
    {
        /// <summary>Базовая зона — доступна всем юнитам.</summary>
        Normal,

        /// <summary>Расширенная зона — только юнитам с правом (манёвренные/убийцы или навык).</summary>
        Extended,
    }

    /// <summary>
    /// Область расстановки внутри арены: прямоугольник + сторона + ранг. Чистые данные.
    /// Валидация «можно ли сюда ставить» живёт в <see cref="DeploymentService"/> (не в UI),
    /// чтобы работать и на хосте при host-authoritative кооперативе (вики «15» §6).
    /// </summary>
    public readonly struct DeploymentZone
    {
        public readonly Rect2D         Area;
        public readonly DeploymentSide Side;
        public readonly DeploymentTier Tier;

        public DeploymentZone(Rect2D area, DeploymentSide side, DeploymentTier tier)
        {
            Area = area;
            Side = side;
            Tier = tier;
        }
    }
}
