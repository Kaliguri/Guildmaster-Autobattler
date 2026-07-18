using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Параметры генерации карты акта (<see cref="MapGenerator"/>, план [[act-map-run-loop]] §3.1). Чистый POCO
    /// без Unity-зависимостей — генератор тестируется headless. Оркестратор/фаза B подтянут значения из
    /// <c>ActConfig</c>-SO; здесь — согласованные с Максом дефолты (2026-07-17): 9 колонок с прицелом на ~15.
    /// </summary>
    [Serializable]
    public sealed class MapGenConfig
    {
        /// <summary>Глубина акта: всего колонок, включая Start (первая) и Boss (последняя). Дефолт 9 (прицел ~15).</summary>
        public int Columns = 9;

        /// <summary>Мин. ширина промежуточной колонки (параллельных узлов). ≥2 гарантирует ветвление/схождение.</summary>
        public int MinColumnWidth = 2;

        /// <summary>Макс. ширина промежуточной колонки.</summary>
        public int MaxColumnWidth = 3;

        /// <summary>Индекс колонки, раньше которой элитки не ставятся (первая пара колонок — «мягкий вход»).</summary>
        public int EliteMinColumn = 2;

        // --- Веса типов узлов на промежуточных колонках (относительные, не обязаны давать 100) ---
        public int WeightBattle    = 42;
        public int WeightElite     = 10;
        public int WeightTextEvent = 20;
        public int WeightShop      = 8;
        public int WeightChest     = 12;
        public int WeightUnknown   = 8;

        /// <summary>Валидирует и клампит поля к разумным границам (защита от кривого SO/ручного конфига).</summary>
        public MapGenConfig Validated()
        {
            if (Columns < 3) Columns = 3;                       // минимум: Start → одна промежуточная → Boss
            if (MinColumnWidth < 1) MinColumnWidth = 1;
            if (MaxColumnWidth < MinColumnWidth) MaxColumnWidth = MinColumnWidth;
            if (EliteMinColumn < 1) EliteMinColumn = 1;         // элитка не в Start-колонке
            return this;
        }
    }
}
