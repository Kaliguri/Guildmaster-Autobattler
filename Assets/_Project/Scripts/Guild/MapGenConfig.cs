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

        /// <summary>
        /// Валидированная КОПИЯ: клампит поля к разумным границам (защита от кривого SO/ручного конфига).
        /// <para>Именно копия, а не <c>this</c>. Носитель этого POCO — сериализованное поле <c>ActConfig</c>,
        /// то есть ассет на диске: клампя себя на месте, метод переписывал бы авторский конфиг тихо и
        /// необратимо, а генератор зовёт его на каждую генерацию карты. Пока ассет не был подключён к сцене,
        /// порча была недостижима — и стала бы живой ровно в момент подключения (аудит 2026-07-26,
        /// R1-49/AC-3/T-5).</para>
        /// </summary>
        public MapGenConfig Validated()
        {
            var c = new MapGenConfig
            {
                Columns         = Columns,
                EdgeColumnWidth = EdgeColumnWidth,
                EdgeColumns     = EdgeColumns,
                MinColumnWidth  = MinColumnWidth,
                MaxColumnWidth  = MaxColumnWidth,
                MaxEdgesPerNode = MaxEdgesPerNode,
                // ZoneRule/AnchorRule — структуры, поэтому клон массива отвязывает копию полностью.
                Zones   = Zones   != null ? (ZoneRule[])Zones.Clone()     : Array.Empty<ZoneRule>(),
                Anchors = Anchors != null ? (AnchorRule[])Anchors.Clone() : Array.Empty<AnchorRule>(),
            };

            if (c.Columns < 3) c.Columns = 3;                       // минимум: Start → одна промежуточная → Boss
            if (c.MinColumnWidth < 1) c.MinColumnWidth = 1;
            if (c.MaxColumnWidth < c.MinColumnWidth) c.MaxColumnWidth = c.MinColumnWidth;
            if (c.EdgeColumnWidth < 1) c.EdgeColumnWidth = 1;
            if (c.MaxEdgesPerNode < 2) c.MaxEdgesPerNode = 2;      // < 2 сделало бы карту цепочкой без выбора
            if (c.EdgeColumns < 0) c.EdgeColumns = 0;
            // Горловины с обоих краёв не должны съесть середину: иначе профиль вырождается в плоскую ширину.
            int middle = c.Columns - 2;                              // без Start и Boss
            if (c.EdgeColumns * 2 > middle) c.EdgeColumns = middle / 2;

            return c;
        }

        // Дефолтная раскладка (решение Макса 2026-07-26). Этажи-испытания: 1..13; Boss — колонка 14.
        // Зоны: вход 1 (бой + «?»), 2–6 (+магазин, с 5 ещё и элита), 9–12 (+элита/магазин).
        // Этажи 7, 8 и 13 зон не имеют вовсе — они целиком отданы якорям.
        //
        // МАГАЗИН живёт на этажах 2–6 и 9–12 (решение Макса 2026-07-26): встреча с лавкой — элемент
        // маршрута, а не расписание акта. Первый этаж от него свободен: акт открывается боем, а не
        // прилавком.
        // СУНДУК своего зонного веса не имеет НИГДЕ: он гарантирован ровно на якорном этаже 8, а во всех
        // прочих местах может выпасть только изнутри «?». Гарантия одна и явная — иначе награда
        // размазывается по карте и якорь перестаёт быть событием.
        // ТЕКСТОВОЕ СОБЫТИЕ СВОЕГО УЗЛА НЕ ИМЕЕТ (решение Макса 2026-07-20): оно приходит только изнутри
        // «?». Событие и есть неизвестность — объявлять её иконкой заранее значит её же и снимать.
        private static ZoneRule[] DefaultZones() => new[]
        {
            new ZoneRule(1, 1, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  70),
                new NodeTypeWeight(MapNodeType.Unknown, 30),
            }),
            new ZoneRule(2, 4, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  62),
                new NodeTypeWeight(MapNodeType.Unknown, 26),
                new NodeTypeWeight(MapNodeType.Shop,    12),
            }),
            new ZoneRule(5, 6, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  42),
                new NodeTypeWeight(MapNodeType.Unknown, 26),
                new NodeTypeWeight(MapNodeType.Elite,   18),
                new NodeTypeWeight(MapNodeType.Shop,    14),
            }),
            new ZoneRule(9, 12, new[]
            {
                new NodeTypeWeight(MapNodeType.Battle,  38),
                new NodeTypeWeight(MapNodeType.Elite,   30),
                new NodeTypeWeight(MapNodeType.Unknown, 20),
                new NodeTypeWeight(MapNodeType.Shop,    12),
            }),
        };

        // Талия акта (решение Макса 2026-07-26, замещает форму 2026-07-20): широкая середина сходится в
        // ОДИН привал (7), из него веер в ТРИ сундука (8), и оттуда акт снова расходится по зонам.
        // Порядок именно такой: развилка обязана быть выбором, а три одинаковых привала — это один узел
        // трижды; три сундука дают разную добычу. Привал же самодостаточен как общая точка — узел трат,
        // к нему осмысленно сводить всю ширину карты.
        // Перед боссом — привал (13), и он ОДИН: последняя точка трат не может быть лотереей, а веер
        // здесь повторял бы форму талии второй раз подряд и обесценивал её.
        private static AnchorRule[] DefaultAnchors() => new[]
        {
            new AnchorRule(7,  MapNodeType.Camp,  width: 1),
            new AnchorRule(8,  MapNodeType.Chest, width: 3),
            new AnchorRule(13, MapNodeType.Camp,  width: 1),
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
