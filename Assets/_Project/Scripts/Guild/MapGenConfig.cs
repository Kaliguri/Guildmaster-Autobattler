using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Параметры генерации карты акта (<see cref="MapGenerator"/>, план [[act-map-run-loop]] §3.1). Чистый POCO
    /// без Unity-зависимостей (кроме сериализуемых структур) — генератор тестируется headless. Носитель для
    /// авторинга — <c>ActConfig</c>-SO (оборачивает этот класс); здесь — дефолты, согласованные с Максом
    /// (2026-07-19, оверхол карты): 14 колонок = Start + 12 испытаний + Boss; типы узлов — по ЗОНАМ этажей
    /// с ЯКОРЯМИ (фиксированный этаж → тип целой колонки).
    /// </summary>
    [Serializable]
    public sealed class MapGenConfig
    {
        /// <summary>Глубина акта: всего колонок, включая Start (первая) и Boss (последняя). Дефолт 14 (Start+12+Boss).</summary>
        public int Columns = 14;

        /// <summary>
        /// Ширина «горловин» акта: сколько параллельных узлов на первых и последних этажах. Акт начинается
        /// узко, раздаётся вширь к середине и снова сужается к боссу — силуэт читается как путь, а не как
        /// однородная решётка (решение Макса 2026-07-20).
        /// </summary>
        public int EdgeColumnWidth = 3;

        /// <summary>Сколько этажей с КАЖДОГО края держать на <see cref="EdgeColumnWidth"/> (не считая Start/Boss).</summary>
        public int EdgeColumns = 2;

        /// <summary>Мин. ширина колонки в середине акта (между горловинами).</summary>
        public int MinColumnWidth = 4;

        /// <summary>Макс. ширина колонки в середине акта.</summary>
        public int MaxColumnWidth = 6;

        /// <summary>
        /// Зонные правила: диапазон этажей (индекс колонки) → разрешённые типы с весами. Первая покрывающая
        /// этаж зона выигрывает. Этаж вне всех зон и якорей → безопасный дефолт (Бой). Перекрываются якорями.
        /// </summary>
        public ZoneRule[] Zones = DefaultZones();

        /// <summary>Якоря: фиксированный этаж → тип для ВСЕЙ колонки (гарант. сундук-ряд / привал перед боссом).</summary>
        public AnchorRule[] Anchors = DefaultAnchors();

        /// <summary>Валидирует и клампит поля к разумным границам (защита от кривого SO/ручного конфига).</summary>
        public MapGenConfig Validated()
        {
            if (Columns < 3) Columns = 3;                       // минимум: Start → одна промежуточная → Boss
            if (MinColumnWidth < 1) MinColumnWidth = 1;
            if (MaxColumnWidth < MinColumnWidth) MaxColumnWidth = MinColumnWidth;
            if (EdgeColumnWidth < 1) EdgeColumnWidth = 1;
            if (EdgeColumns < 0) EdgeColumns = 0;
            // Горловины с обоих краёв не должны съесть середину: иначе профиль вырождается в плоскую ширину.
            int middle = Columns - 2;                            // без Start и Boss
            if (EdgeColumns * 2 > middle) EdgeColumns = middle / 2;
            Zones   ??= Array.Empty<ZoneRule>();
            Anchors ??= Array.Empty<AnchorRule>();
            return this;
        }

        // Дефолтная раскладка (одобрено 2026-07-19). Этажи-испытания: 1..12; Boss — колонка 13.
        // Зоны: Разогрев 1–4 (только бой/событие), Развитие 5–8 (+элита/магазин), Пик 9–11 (+элита/сундук).
        // Якоря: этаж 7 — сундук-ряд (равная удалённость), этаж 12 — магазин-привал перед боссом.
        private static ZoneRule[] DefaultZones() => new[]
        {
            new ZoneRule(1, 4, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,    70),
                new NodeTypeWeight(MapNodeType.TextEvent, 30),
            }),
            new ZoneRule(5, 8, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,    45),
                new NodeTypeWeight(MapNodeType.TextEvent, 20),
                new NodeTypeWeight(MapNodeType.Elite,     20),
                new NodeTypeWeight(MapNodeType.Shop,      15),
            }),
            new ZoneRule(9, 11, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,    40),
                new NodeTypeWeight(MapNodeType.Elite,     30),
                new NodeTypeWeight(MapNodeType.Chest,     20),
                new NodeTypeWeight(MapNodeType.TextEvent, 10),
            }),
        };

        private static AnchorRule[] DefaultAnchors() => new[]
        {
            new AnchorRule(7,  MapNodeType.Chest),
            new AnchorRule(12, MapNodeType.Shop),
        };
    }

    /// <summary>Вес одного типа узла в зоне (относительный, суммы 100 не требуется).</summary>
    [Serializable]
    public struct NodeTypeWeight
    {
        public MapNodeType Type;
        public int Weight;
        public NodeTypeWeight(MapNodeType type, int weight) { Type = type; Weight = weight; }
    }

    /// <summary>Правило зоны: этажи <see cref="FromFloor"/>..<see cref="ToFloor"/> (включительно) → веса типов.</summary>
    [Serializable]
    public struct ZoneRule
    {
        public int FromFloor;
        public int ToFloor;
        public NodeTypeWeight[] Weights;

        public ZoneRule(int fromFloor, int toFloor, NodeTypeWeight[] weights)
        {
            FromFloor = fromFloor;
            ToFloor   = toFloor;
            Weights   = weights;
        }

        public bool Covers(int floor) => floor >= FromFloor && floor <= ToFloor;
    }

    /// <summary>Якорь: на этаже <see cref="Floor"/> ВСЯ колонка получает тип <see cref="Type"/> (перекрывает зону).</summary>
    [Serializable]
    public struct AnchorRule
    {
        public int Floor;
        public MapNodeType Type;
        public AnchorRule(int floor, MapNodeType type) { Floor = floor; Type = type; }
    }
}
