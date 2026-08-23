using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Режим расстановки готового боя (план шаг 3/4). <see cref="Fixed"/> — чистый запуск по сохранённым
    /// позициям (реплей-подобный). <see cref="Free"/> — отдать player-сторону в интерактивную фазу
    /// расстановки (шаг 4); пока (до шага 4) ведёт себя как Fixed.
    /// </summary>
    public enum DeploymentMode { Fixed, Free }

    /// <summary>
    /// Слот player-ростера: сосуд + надетый на него Мементо (весь боевой кит) + сохранённая стартовая
    /// позиция (team 0). Модель «фигурка = Сосуд, Мементо даёт кит»: слот Мементо всегда заполнен — у
    /// «пустого» сосуда стоит базовое Мементо без особенностей (<c>relic.base</c>).
    /// </summary>
    [System.Serializable]
    public struct PlayerSlot
    {
        [Tooltip("Мементо = весь боевой кит юнита. relic.base = «пустой» сосуд (дамми-кит). Обязателен.")]
        [SerializeField] private RelicData _relic;

        [Tooltip("Сосуд («пилот»). Опционален: null = без сосуда (vessel-контента пока нет). Даёт перк-моды при наличии.")]
        [SerializeField] private VesselData _vessel;

        [Tooltip("Сохранённая стартовая позиция на арене (team 0). Fixed спавнит прямо сюда; Free — старт фазы расстановки.")]
        [SerializeField] private Vector2 _position;

        [Tooltip("Предметы юнита (Vessel-скоуп): статовые моды/пассивки, до VesselItemSlots штук (D1). Опц.")]
        [SerializeField] private ItemData[] _items;

        [Tooltip("Последствия забега на «Сосуде» — травмы и закалки. В забеге приходят из RunState; " +
                 "здесь задаются, чтобы дев-бой можно было прогнать раненым отрядом. Опц.")]
        [SerializeField] private ConsequenceData[] _consequences;

        [Tooltip("Индекс места в RunState.Guild, откуда собран слот. Нужен, чтобы расстановка писала " +
                 "позицию НЕ тому, кто стоит рядом: в бой выходят не все места отряда, и порядок " +
                 "боевого ростера больше не совпадает с гильдией. Дев-пресет оставляет 0 — там " +
                 "запись в сейв всё равно не срабатывает.")]
        [HideInInspector] [SerializeField] private int _guildIndex;

        public RelicData  Relic    => _relic;
        public VesselData Vessel   => _vessel;
        public Vector2    Position => _position;
        public IReadOnlyList<ItemData> Items => _items;

        /// <summary>Травмы и закалки «Сосуда» (ГДД <c>injuries-mettle</c>). Пусто = цел.</summary>
        public IReadOnlyList<ConsequenceData> Consequences => _consequences;

        /// <summary>
        /// Место в <c>RunState.Guild</c>, из которого собран слот. Единственный способ дописать
        /// позицию и надетый кит обратно тому же «Сосуду»: боевой ростер собирается ТОЛЬКО из
        /// помеченных «в бою», поэтому его индексы гильдии больше не повторяют.
        /// </summary>
        public int GuildIndex => _guildIndex;

        /// <summary>Рантайм-слот (бридж гильдии игрока в бой): собирается из <c>RunState</c>, не из инспектора.</summary>
        public PlayerSlot(RelicData relic, VesselData vessel, Vector2 position, ItemData[] items = null,
                          ConsequenceData[] consequences = null, int guildIndex = 0)
        {
            _relic        = relic;
            _vessel       = vessel;
            _position     = position;
            _items        = items;
            _consequences = consequences;
            _guildIndex   = guildIndex;
        }
    }

    /// <summary>
    /// Готовый бой (план шаг 3): вражеская сторона (<see cref="EncounterData"/>) + player-ростер
    /// (<see cref="PlayerSlot"/>[]) + режим расстановки. Загрузчик <c>EncounterLoader.LoadPreset</c> строит
    /// из этого полный бой: враги из энкаунтера (team 1) + ростер (team 0). Разделение ответственности —
    /// энкаунтер знает только врагов, пресет добавляет игрока (вики «13» §3.3, «10» §3).
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Battle Preset", fileName = "BattlePreset")]
    public sealed class BattlePresetData : ContentDefinition
    {
        [Header("Battle preset")]
        [Tooltip("Вражеский состав боя.")]
        [SerializeField] private EncounterData _encounter;

        [Tooltip("Player-ростер (team 0): слоты {Мементо + сосуд + позиция}.")]
        [SerializeField] private PlayerSlot[] _roster;

        [Tooltip("Fixed = спавн сразу по сохранённым позициям; Free = интерактивная расстановка (шаг 4; пока = Fixed).")]
        [SerializeField] private DeploymentMode _deploymentMode = DeploymentMode.Fixed;

        [Tooltip("Баннеры боя (Party-скоуп предметы): действуют на всю команду team 0 (D1). Опц.")]
        [SerializeField] private ItemData[] _partyItems;

        [Tooltip("ОБОЛОЧКА, а не бой: враги и ростер приходят снаружи, поэтому пустыми они и должны быть. " +
                 "Такой пресет — носитель настроек для чужого сценария (бои за главным меню задаёт " +
                 "MenuBattleConfig). Обычному пресету НЕ ставить: он обязан нести свой состав.")]
        [SerializeField] private bool _isCarrier;

        public EncounterData            Encounter      => _encounter;
        public IReadOnlyList<PlayerSlot> Roster        => _roster;
        public DeploymentMode           DeploymentMode => _deploymentMode;
        public IReadOnlyList<ItemData>  PartyItems     => _partyItems;

        /// <summary>
        /// Пресет-оболочка: состав ему задают снаружи, поэтому энкаунтер и ростер у него пусты законно.
        /// </summary>
        /// <remarks>
        /// Роль объявлена ПОЛЕМ, а не выведена из пустоты полей и не опознана валидатором по обратной
        /// ссылке. Причина ровно одна и она про людей: пустой пресет выглядит недоделанным. Валидатор
        /// требовал у него энкаунтер, тест краснел, и первым побуждением — проверено на себе 2026-08-08 —
        /// было «дозаполнить ассет», что сломало бы механику: составы боёв за меню живут в
        /// <see cref="MenuBattleConfig"/>, а не здесь. Флаг в инспекторе отвечает на этот вопрос до того,
        /// как его зададут.
        /// </remarks>
        public bool IsCarrier => _isCarrier;

        /// <summary>
        /// Сложность боя. Владелец один — энкаунтер: сложность есть свойство вражеского состава, а не
        /// обёртки вокруг него. Прежде рядом жил свой <c>bool _isElite</c>, и владельцев было два —
        /// причём проигравший оказался тем, кого читали: флаг не проставлен НИ В ОДНОМ пресете, поэтому
        /// элитные узлы не находили ни одного элитного боя и откатывались на обычный. Тир энкаунтеров
        /// при этом заполнен по-настоящему. Плюс тир умеет то, чего булев не умеет в принципе, —
        /// отличать финал акта (ГДД: обычный / элитный / босс).
        /// </summary>
        public EncounterTier            Tier           => _encounter != null ? _encounter.Tier : EncounterTier.Common;

        /// <summary>
        /// Уместен ли бой пресета на этаже акта. Пресет без энкаунтера уместен везде — ограничивать
        /// нечего, а спрятать его молча значило бы выкинуть дев-бой из пула по чужой причине.
        /// </summary>
        public bool FitsFloor(int floor) => _encounter == null || _encounter.FitsFloor(floor);

        /// <summary>
        /// Собрать ТРАНЗИЕНТНЫЙ пресет боя в рантайме (узел забега): враги — из авторского пресета, а player-ростер —
        /// из гильдии игрока (<c>RunState</c>), режим — обычно <see cref="DeploymentMode.Free"/> (расстановка перед
        /// боем). Не ассет, в контент-БД не регистрируется, живёт один бой. Так узел деплоит СВОЮ четвёрку, а не
        /// канон-ростер пресета. Весь боевой пайплайн (загрузчик/расстановка) остаётся нетронутым.
        /// </summary>
        public static BattlePresetData CreateRuntime(
            EncounterData encounter, PlayerSlot[] roster, DeploymentMode mode,
            ItemData[] partyItems = null, string id = "battle.runtime")
        {
            var preset = CreateInstance<BattlePresetData>();
            preset._encounter      = encounter;
            preset._roster         = roster ?? System.Array.Empty<PlayerSlot>();
            preset._deploymentMode = mode;
            preset._partyItems     = partyItems;
            preset.SetId(id);   // сложность не копируем: она приезжает вместе с энкаунтером
            return preset;
        }
    }
}
