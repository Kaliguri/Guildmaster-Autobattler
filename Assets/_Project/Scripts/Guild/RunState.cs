using System;
using UnityEngine;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Тип узла карты акта (план 11 §3.3, [[act-map-run-loop]] §3.1). Полезная нагрузка — по строковому id
    /// соответствующего типа. Значения только <b>дописываются</b> (сериализуется в сейв по индексу) —
    /// порядок существующих не менять.
    /// </summary>
    public enum MapNodeType
    {
        Start,      // стартовый узел (без нагрузки)
        Battle,     // рядовой бой (payload = battle_preset.* или encounter.*)
        Elite,      // элитный бой
        TextEvent,  // текстовый ивент (payload = event.*)
        Shop,       // магазин (payload = shop.* пул; пусто = дефолт)
        Boss,       // финалист акта
        Chest,      // сундук: сцена-фасад → награда 1-из-3 (payload = reward-пул; пусто = дефолт)
        Unknown,    // «?»-узел: тип роллится на входе (RandomEventFlow, план §5.4/B4)
        Camp,       // привал: бюджет действий отряда тратится на несколько трат подряд (CampFlow)
    }

    /// <summary>
    /// Узел графа акта (сериализуемый DTO). Рёбра — по id соседних узлов. <see cref="Cleared"/> помечает
    /// пройденные (для валидации хода). Нагрузка — строковый id (пресет/ивент/пул).
    /// <para>Хранит только ТОПОЛОГИЮ (этаж + ряд). Мировые координаты, шаг, центрирование ряда и разброс —
    /// забота презентера карты: домен про отрисовку не знает.</para>
    /// </summary>
    [Serializable]
    public sealed class MapNode
    {
        public string      Id;
        public MapNodeType Type;
        public string      PayloadId;
        public string[]    Edges = Array.Empty<string>();
        public bool        Cleared;
        /// <summary>Этаж (индекс колонки) от старта: 0 = Start, последний = Boss. На него завязаны зоны и якоря.</summary>
        public int Floor;

        /// <summary>Индекс узла внутри своего этажа, сверху вниз (0..ширина-1).</summary>
        public int Row;
    }

    /// <summary>Состояние карты акта в <see cref="RunState"/>: граф узлов + где игрок сейчас.</summary>
    [Serializable]
    public sealed class MapState
    {
        public string     CurrentNodeId;
        public MapNode[]   Nodes = Array.Empty<MapNode>();
    }

    /// <summary>
    /// Слот ростера гильдии (план 11 §3.1): сосуд + надетый релик (весь кит) + пресет AI + предметы + позиция.
    /// Всё по строковым content id (relic.*, vessel.*, ai_preset.*, item.*). Слот релика всегда заполнен
    /// (relic.base у «пустого» сосуда). Пустые строки = не задано (сосуд опц., aiPreset = дефолт релика).
    /// </summary>
    [Serializable]
    public sealed class RosterSlot
    {
        public string   VesselId = string.Empty;
        public string   RelicId = Data.Definitions.ContentIds.BaseRelic;
        public string   AiPresetId = string.Empty;
        public string[] VesselItemIds = Array.Empty<string>();
        public Vector2  SavedPosition;
    }

    /// <summary>
    /// Durable-состояние забега (план 11 §3.1, вики «7» §4): единственный источник истины забега, всё по
    /// строковым content id. Переживает забег и <b>сохраняется</b> (JSON через <c>ISaveService</c>); собирается
    /// в боевые <c>RuntimeUnit</c> штатной фабрикой на каждый бой, изменения пишутся обратно. Плоский
    /// <c>[Serializable]</c> = сам себе save-DTO (нет SO-ссылок и рантайм-состояния — сплит на отдельный DTO
    /// не нужен; появится рантайм-поле — тогда и разделим). Сетевой-ready: реплицируется хостом как есть.
    /// </summary>
    [Serializable]
    public sealed class RunState
    {
        public int    SchemaVersion = 1;

        public long   Seed;
        public int    CurrentActIndex;
        public int    Difficulty;
        public int    Gold;

        /// <summary>Перезапуски боя, оставшиеся В ЭТОМ АКТЕ (реш. №65). Сброс в начале акта, не копится между актами.</summary>
        public int    RestartsRemaining;

        /// <summary>Вместимость коллекции запаса реликов (план 11 §5.4). База/потолок — в GameConfig.</summary>
        public int    RelicCapacity;

        /// <summary>Собранные, но не надетые релики (запас для свапа между боями). Кап = <see cref="RelicCapacity"/>.</summary>
        public string[] RelicInventory = Array.Empty<string>();

        /// <summary>Баннеры (Party-скоуп предметы, действуют на всю команду в бою).</summary>
        public string[] PartyItemIds = Array.Empty<string>();

        /// <summary>Ростер гильдии — 4 сосуда.</summary>
        public RosterSlot[] Guild = Array.Empty<RosterSlot>();

        /// <summary>Карта текущего акта.</summary>
        public MapState Map = new MapState();

        /// <summary>Кооп-шов: индекс игрока-владельца по каждому слоту ростера (соло — все 0). Реализация — Фаза 6.</summary>
        public int[] SlotOwner = Array.Empty<int>();
    }
}
