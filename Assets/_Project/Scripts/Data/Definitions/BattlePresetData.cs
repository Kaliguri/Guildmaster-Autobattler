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
    /// Слот player-ростера: сосуд + надетый на него релик (весь боевой кит) + сохранённая стартовая
    /// позиция (team 0). Модель «фигурка = Сосуд, релик даёт кит»: слот релика всегда заполнен — у
    /// «пустого» сосуда стоит базовый релик без особенностей (<c>relic.base</c>).
    /// </summary>
    [System.Serializable]
    public struct PlayerSlot
    {
        [Tooltip("Релик = весь боевой кит юнита. relic.base = «пустой» сосуд (дамми-кит). Обязателен.")]
        [SerializeField] private RelicData _relic;

        [Tooltip("Сосуд («пилот»). Опционален: null = без сосуда (vessel-контента пока нет). Даёт перк-моды при наличии.")]
        [SerializeField] private VesselData _vessel;

        [Tooltip("Сохранённая стартовая позиция на арене (team 0). Fixed спавнит прямо сюда; Free — старт фазы расстановки.")]
        [SerializeField] private Vector2 _position;

        [Tooltip("Предметы юнита (Vessel-скоуп): статовые моды/пассивки, до VesselItemSlots штук (D1). Опц.")]
        [SerializeField] private ItemData[] _items;

        public RelicData  Relic    => _relic;
        public VesselData Vessel   => _vessel;
        public Vector2    Position => _position;
        public IReadOnlyList<ItemData> Items => _items;

        /// <summary>Рантайм-слот (бридж гильдии игрока в бой): собирается из <c>RunState</c>, не из инспектора.</summary>
        public PlayerSlot(RelicData relic, VesselData vessel, Vector2 position, ItemData[] items = null)
        {
            _relic    = relic;
            _vessel   = vessel;
            _position = position;
            _items    = items;
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

        [Tooltip("Player-ростер (team 0): слоты {релик + сосуд + позиция}.")]
        [SerializeField] private PlayerSlot[] _roster;

        [Tooltip("Fixed = спавн сразу по сохранённым позициям; Free = интерактивная расстановка (шаг 4; пока = Fixed).")]
        [SerializeField] private DeploymentMode _deploymentMode = DeploymentMode.Fixed;

        [Tooltip("Баннеры боя (Party-скоуп предметы): действуют на всю команду team 0 (D1). Опц.")]
        [SerializeField] private ItemData[] _partyItems;

        public EncounterData            Encounter      => _encounter;
        public IReadOnlyList<PlayerSlot> Roster        => _roster;
        public DeploymentMode           DeploymentMode => _deploymentMode;
        public IReadOnlyList<ItemData>  PartyItems     => _partyItems;

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
