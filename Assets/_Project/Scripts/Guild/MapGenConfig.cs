using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Параметры генерации карты акта (<see cref="MapGenerator"/>, план [[act-map-run-loop]] §3.1). Чистый POCO
    /// без Unity-зависимостей (кроме сериализуемых структур) — генератор тестируется headless. Носитель для
    /// авторинга — <c>ActConfig</c>-SO (оборачивает этот класс); здесь — дефолты, согласованные с Максом
    /// (2026-07-19, оверхол карты; 2026-07-20 — ряд привалов): 15 колонок = Start + 13 испытаний + Boss;
    /// типы узлов — по ЗОНАМ этажей с ЯКОРЯМИ (фиксированный этаж → тип целой колонки).
    /// </summary>
    [Serializable]
    public sealed class MapGenConfig
    {
        /// <summary>Глубина акта: всего колонок, включая Start (первая) и Boss (последняя). Дефолт 15 (Start+13+Boss).</summary>
        public int Columns = 15;

        /// <summary>
        /// Ширина «горловин» акта: сколько параллельных узлов на первых и последних этажах. Акт начинается
        /// узко, раздаётся вширь к середине и снова сужается к боссу — силуэт читается как путь, а не как
        /// однородная решётка (решение Макса 2026-07-20).
        /// </summary>
        public int EdgeColumnWidth = 3;

        /// <summary>
        /// Сколько этажей с КАЖДОГО края держать на <see cref="EdgeColumnWidth"/> (не считая Start/Boss).
        /// Один: горловина — это ровно вход и выход акта, дальше сразу широкая часть (решение Макса).
        /// </summary>
        public int EdgeColumns = 1;

        /// <summary>Мин. ширина колонки в середине акта (между горловинами). Роллится на КАЖДЫЙ этаж отдельно.</summary>
        public int MinColumnWidth = 5;

        /// <summary>Макс. ширина колонки в середине акта. Диапазон Min..Max — и есть разброс ширины акта.</summary>
        public int MaxColumnWidth = 7;

        /// <summary>
        /// Потолок путей, выходящих из узла (и входящих в него). Один-четыре исхода — нормальный выбор
        /// (решение Макса 2026-07-20: развилка это и есть содержание карты, в том числе выбор биома),
        /// но веер шире четырёх перестаёт читаться и превращает карту в кашу.
        /// </summary>
        public int MaxEdgesPerNode = 4;

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
            if (MaxEdgesPerNode < 2) MaxEdgesPerNode = 2;      // < 2 сделало бы карту цепочкой без выбора
            if (EdgeColumns < 0) EdgeColumns = 0;
            // Горловины с обоих краёв не должны съесть середину: иначе профиль вырождается в плоскую ширину.
            int middle = Columns - 2;                            // без Start и Boss
            if (EdgeColumns * 2 > middle) EdgeColumns = middle / 2;
            Zones   ??= Array.Empty<ZoneRule>();
            Anchors ??= Array.Empty<AnchorRule>();
            return this;
        }

        // Дефолтная раскладка (одобрено 2026-07-20). Этажи-испытания: 1..13; Boss — колонка 14.
        // Зоны: Разогрев 1–4 (бой + «?»), Развитие 5–8 (+элита/магазин), Пик 9–13 (+элита/сундук).
        // ТЕКСТОВОЕ СОБЫТИЕ СВОЕГО УЗЛА НЕ ИМЕЕТ (решение Макса 2026-07-20): оно приходит только изнутри
        // «?». Событие и есть неизвестность — объявлять её иконкой заранее значит её же и снимать.
        // Поэтому «?» стоит и в разогреве: без него первые четыре этажа выродились бы в сплошной бой.
        // Якоря режут середину и хвост: 7 — сундук, 8 — привалы, 13 — привал перед боссом.
        private static ZoneRule[] DefaultZones() => new[]
        {
            new ZoneRule(1, 4, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  70),
                new NodeTypeWeight(MapNodeType.Unknown, 30),
            }),
            new ZoneRule(5, 8, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  42),
                new NodeTypeWeight(MapNodeType.Unknown, 26),
                new NodeTypeWeight(MapNodeType.Elite,   18),
                new NodeTypeWeight(MapNodeType.Shop,    14),
            }),
            new ZoneRule(9, 13, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  36),
                new NodeTypeWeight(MapNodeType.Elite,   28),
                new NodeTypeWeight(MapNodeType.Unknown, 20),
                new NodeTypeWeight(MapNodeType.Chest,   16),
            }),
        };

        // Талия акта (решение Макса 2026-07-20): широкая середина сходится в ОДИН сундук (7), из него веер
        // в ТРИ привала (8), и оттуда акт снова расходится по зонам. Один узел на весь этаж — это ритмическая
        // пауза: карта на секунду перестаёт быть выбором и становится общей точкой, а следующий шаг —
        // осмысленной развилкой из трёх. Перед боссом — привал (13), и только он: ряд-якорь магазинов на
        // этаже 12 убран (решение Макса 2026-07-20) — целая колонка лавок перед финалом читалась как
        // распродажа, а не как последний вдох. Магазин остаётся обычным узлом зоны развития.
        private static AnchorRule[] DefaultAnchors() => new[]
        {
            new AnchorRule(7,  MapNodeType.Chest, width: 1),
            new AnchorRule(8,  MapNodeType.Camp,  width: 3),
            new AnchorRule(13, MapNodeType.Camp,  width: 3),
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

        /// <summary>
        /// Своя ширина этажа-якоря: 0 = ширина по общему профилю акта. Позволяет сделать якорный этаж
        /// горловиной (сундук-ряд узкий, чтобы читался как передышка, а не как ещё одна широкая колонка).
        /// </summary>
        public int Width;

        public AnchorRule(int floor, MapNodeType type, int width = 0)
        {
            Floor = floor;
            Type  = type;
            Width = width;
        }
    }
}
