namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Значения, на которых боевые тесты строят симуляцию. ФИКСТУРА, а не владелец: настоящие владельцы —
    /// <c>StatsConfig.ArmorConstantK</c> (ассет) и <c>CombatLifetimeScope._spatialHashCellSize</c> (сцена).
    /// <para>Смысл в том, чтобы копия была ОДНА и её расхождение с игрой было видно: до аудита 2026-07-26
    /// (TS-17) эти два числа были выписаны в семи тестовых файлах каждое, поэтому правка баланса оставляла
    /// тесты считать по-старому — молча и во всех сразу. Guard-тест
    /// <c>CombatTestValuesMatchTheGameTests</c> сверяет их с ассетом и сценой.</para>
    /// <para>Тест, для которого само число — предмет проверки (например, <c>SpatialHashTests</c> с его
    /// собственным размером ячейки), берёт своё значение и сюда не смотрит: там это параметр, а не фон.</para>
    /// </summary>
    public static class CombatTestValues
    {
        /// <summary>Константа брони: <c>mult = K / (K + armor)</c>. Владелец — StatsConfig.asset.</summary>
        public const float ArmorK = 100f;

        /// <summary>Размер ячейки пространственного хэша. Владелец — поле боевого скоупа в сцене.</summary>
        public const float CellSize = 3f;
    }
}
