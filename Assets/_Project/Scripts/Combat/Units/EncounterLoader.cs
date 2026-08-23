using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Один player-юнит для старта боя: кит (мементо/база) + сосуд + позиция (team 0). Player-сторону
    /// энкаунтер НЕ хранит — её подаёт вызывающий: в dev-срезе (шаг 1) это временный селектор мементо,
    /// в шаге 3 — ростер <c>BattlePresetData</c>. Так форма <see cref="EncounterData"/> финальна.
    /// </summary>
    public readonly struct PlayerSpawn
    {
        public readonly UnitData   Unit;
        public readonly VesselData Vessel;
        public readonly Vector2    Position;

        /// <summary>Предметы (Vessel) + баннеры (Party) юнита — статовые моды/пассивки (D1). null = без предметов.</summary>
        public readonly IReadOnlyList<ItemData> Items;

        /// <summary>Последствия забега на «Сосуде» — травмы и закалки. null = «Сосуд» цел.</summary>
        public readonly IReadOnlyList<ConsequenceData> Consequences;

        public PlayerSpawn(UnitData unit, VesselData vessel, Vector2 position,
                           IReadOnlyList<ItemData> items = null,
                           IReadOnlyList<ConsequenceData> consequences = null)
        {
            Unit         = unit;
            Vessel       = vessel;
            Position     = position;
            Items        = items;
            Consequences = consequences;
        }
    }

    /// <summary>
    /// Data-driven строитель СОСТАВА боя: ставит вражескую сторону из <see cref="EncounterData"/>
    /// (team 1) и переданную player-сторону (team 0). Собирает состав, а не бой: рождение и смерть боя
    /// принадлежат <c>BattleHost</c> и его скоупу.
    /// </summary>
    /// <remarks>
    /// <b>Загрузчик больше не помнит «последний бой».</b> Он был нужен рестарту на месте, пока боевой
    /// скоуп жил всю сессию; с 02.08.2026 рестарт — это новый скоуп с тем же пресетом
    /// (<c>BattleHost.Restart</c>), и помнить нечего. Здесь остался только состав: поставить сторону,
    /// доспавнить врагов, пересобрать расстановку внутри идущего боя.
    /// </remarks>
    public sealed class EncounterLoader
    {
        private readonly RuntimeUnitFactory _factory;
        private readonly CombatSimulation   _simulation;
        private readonly IContentDatabase   _content;

        public EncounterLoader(RuntimeUnitFactory factory, CombatSimulation simulation, IContentDatabase content)
        {
            _factory    = factory;
            _simulation = simulation;
            _content    = content;

            // Разводка призывов (M10): бой умеет ставить тела, но собирать их из SO — работа фабрики.
            // Точка одна и живёт здесь же, где собирается состав боя.
            _simulation.BindSummonFactory(factory);
        }

        /// <summary>
        /// Поднимается, когда загружен пресет с <see cref="DeploymentMode.Free"/> (после того как ростер+враги
        /// уже поставлены в очередь спавна). Слушатель — <c>DeploymentController</c> (шаг 4): ставит бой на
        /// паузу, флашит спавны и входит в интерактивную фазу расстановки. Нет слушателя = Free ведёт себя как
        /// Fixed (бой пойдёт по сохранённым позициям — безопасный фолбэк).
        /// </summary>
        public event System.Action<BattlePresetData> FreeDeploymentRequested;

        /// <summary>
        /// Пересобрать состав ИДУЩЕГО боя: очистить арену и поставить врагов энкаунтера (team 1) плюс
        /// player-сторону (team 0). Для пере-расстановки — когда игрок двигает строй и состав ставится
        /// заново на том же бою.
        /// </summary>
        /// <param name="encounter">Состав врагов. null — no-op с предупреждением.</param>
        /// <param name="playerSide">Player-юниты (team 0). null/пусто — бой без союзников (превью врагов).</param>
        public void Load(EncounterData encounter, IReadOnlyList<PlayerSpawn> playerSide = null)
        {
            if (encounter == null)
            {
                Debug.LogWarning("[EncounterLoader] - Load: encounter == null");
                return;
            }

            Build(encounter, playerSide);
        }

        private static List<PlayerSpawn> BuildRosterSide(IReadOnlyList<PlayerSlot> roster,
                                                         IReadOnlyList<ItemData> partyItems)
        {
            if (roster == null || roster.Count == 0) return null;
            var side = new List<PlayerSpawn>(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                PlayerSlot slot = roster[i];
                if (slot.Relic == null) continue; // слот Мементо должен быть заполнен (relic.base у «пустого» сосуда)
                IReadOnlyList<ItemData> items = CombineItems(slot.Items, partyItems);
                side.Add(new PlayerSpawn(slot.Relic, slot.Vessel, slot.Position, items, slot.Consequences));
            }
            return side;
        }

        /// <summary>
        /// Предметы слота (Vessel) + баннеры команды (Party) в один список для сборки юнита. null, если
        /// оба пусты. Публичный, потому что ровно тот же состав нужен телам мира вне боя: расходиться
        /// «что надето в бою» и «что надето во дворе» не имеют права.
        /// </summary>
        public static IReadOnlyList<ItemData> CombineItems(IReadOnlyList<ItemData> vesselItems,
                                                            IReadOnlyList<ItemData> partyItems)
        {
            bool hasVessel = vesselItems != null && vesselItems.Count > 0;
            bool hasParty  = partyItems  != null && partyItems.Count  > 0;
            if (!hasVessel && !hasParty) return null;

            var combined = new List<ItemData>();
            if (hasVessel) combined.AddRange(vesselItems);
            if (hasParty)  combined.AddRange(partyItems);
            return combined;
        }

        private void Build(EncounterData encounter, IReadOnlyList<PlayerSpawn> playerSide)
        {
            // Очистка арены перед новым составом: OnBattleReset снимет виды/цифры и slowmo/тряску.
            // Это НЕ граница боя (её держит скоуп), а именно «поставить состав заново на том же бою».
            _simulation.ResetBattle();
            _factory.ResetIds();

            // Player-сторона (team 0) — сначала, чтобы Id союзников шли раньше врагов (стабильнее для отладки).
            SpawnPlayerSide(playerSide);
            // Вражеская сторона (team 1) — из энкаунтера, по строковым id через реестр.
            SpawnEnemies(encounter);
        }

        /// <summary>
        /// Поставить отряд игрока (team 0) в начале боя. Врагов НЕ трогает — их доспавнит
        /// <see cref="SpawnEnemies"/>. Данные — ростер, разрешённый из гильдии забега
        /// (<c>GuildRoster.Resolve</c>).
        /// </summary>
        /// <remarks>
        /// Сброса здесь нет и быть не должно: скоуп боя только что родился, симуляция пуста, счётчик
        /// Id у фабрики свой. Сброс был границей боя, пока скоуп жил всю сессию; теперь граница — сам
        /// скоуп, а «очистить арену перед составом» осталось только у пере-расстановки внутри боя.
        /// </remarks>
        public void PlaceParty(IReadOnlyList<PlayerSlot> roster, IReadOnlyList<ItemData> partyItems)
            => SpawnPlayerSide(BuildRosterSide(roster, partyItems));

        /// <summary>
        /// Пересобрать состав из ЯВНО заданных сторон, без энкаунтера: обе команды описаны списками спавнов.
        /// Для Ристалища и любой другой площадки, где противник — такие же киты, а не авторенный состав
        /// врагов (ГДД «Modes - Proving Grounds»).
        /// </summary>
        /// <remarks>
        /// Отдельный вход, а не <c>Load(null, side)</c>: у <see cref="Load"/> энкаунтер обязателен по
        /// смыслу — он и есть «кто противник». Полигон отвечает на этот вопрос иначе, списком, и должен
        /// говорить об этом прямо, а не передавать пустоту и надеяться на снисхождение.
        /// </remarks>
        public void LoadSides(IReadOnlyList<PlayerSpawn> playerSide, IReadOnlyList<PlayerSpawn> opponentSide)
        {
            _simulation.ResetBattle();
            _factory.ResetIds();
            SpawnSide(playerSide, team: 0);
            SpawnSide(opponentSide, team: 1);
        }

        /// <summary>
        /// Заспавнить player-сторону (team 0) в очередь спавна — БЕЗ сброса боя. Для persist-мира: отряд
        /// можно поставить на тест-арену ВНЕ боя, а врагов доспавнить позже (<see cref="SpawnEnemies"/>) на
        /// входе в бой. Звать после фазы сброса (<see cref="CombatSimulation.ResetBattle"/> +
        /// <see cref="RuntimeUnitFactory.ResetIds"/>), не посреди активного боя.
        /// </summary>
        public void SpawnPlayerSide(IReadOnlyList<PlayerSpawn> playerSide) => SpawnSide(playerSide, team: 0);

        /// <summary>Заспавнить сторону в очередь спавна — БЕЗ сброса боя. Фабрике всё равно, чей это кит.</summary>
        public void SpawnSide(IReadOnlyList<PlayerSpawn> side, int team)
        {
            if (side == null) return;
            for (int i = 0; i < side.Count; i++)
            {
                PlayerSpawn p = side[i];
                if (p.Unit == null) continue;
                _simulation.EnqueueUnitSpawn(
                    _factory.Create(p.Unit, p.Vessel, team, p.Position, p.Items, p.Consequences));
            }
        }

        /// <summary>
        /// Persist-мир: войти в фазу расстановки для уже стоящего боя (отряд + доспавненные враги на арене).
        /// Поднимает <see cref="FreeDeploymentRequested"/> — <c>DeploymentController</c> ставит паузу, показывает
        /// расстановку и кнопку «Начать». В отличие от <see cref="LoadPreset"/> НЕ сбрасывает бой (отряд/враги
        /// уже на месте). Нет слушателя = no-op (бой останется на паузе — безопасно).
        /// </summary>
        public void RequestDeployment(BattlePresetData preset) => FreeDeploymentRequested?.Invoke(preset);

        /// <summary>
        /// Заспавнить вражескую сторону (team 1) из энкаунтера — БЕЗ сброса боя. Для persist-мира —
        /// доспавн врагов на входе в бой поверх уже стоящего отряда игрока.
        /// </summary>
        public void SpawnEnemies(EncounterData encounter)
        {
            IReadOnlyList<EncounterUnit> units = encounter.Units;
            if (units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                EncounterUnit u = units[i];

                if (string.IsNullOrEmpty(u.EnemyId) || !_content.TryGet(u.EnemyId, out EnemyData enemy))
                {
                    Debug.LogWarning($"[EncounterLoader] - энкаунтер '{encounter.Id}': враг '{u.EnemyId}' " +
                                     "не найден в контент-БД (пропущен). Синхронизирован ли ContentDatabase?");
                    continue;
                }

                int count = u.Count;
                for (int c = 0; c < count; c++)
                {
                    // Count>1 → вертикальный кластер вокруг якоря (как компактные ряды в хардкод-боях).
                    // Формулу держит сам пакет: её же читает балансный стенд, и разъехаться они не должны.
                    _simulation.EnqueueUnitSpawn(_factory.Create(enemy, null, team: 1, u.PositionOf(c)));
                }
            }
        }

    }
}
