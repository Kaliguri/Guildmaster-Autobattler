using System.Collections.Generic;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Собирает тела отряда для арены ВНЕ боя: ростер игрока → снимки и паспорта для
    /// <see cref="WorldBodyStage"/>. Симуляции при этом не существует, и она не нужна.
    /// </summary>
    /// <remarks>
    /// <b>Зачем отдельный сборщик.</b> Боевой путь (<see cref="RuntimeUnitFactory"/> →
    /// <see cref="CombatSimulation"/>) поднимает вместе с юнитом весь бой: систему эффектов, боевой
    /// контекст, мозги, очередь спавна. Телу, которое просто стоит во дворе гильдии, из этого не нужно
    /// ничего — а раньше двор показывался именно так, замороженной паузой боевой симуляцией.
    /// <para><b>Статы считаются тем же каскадом</b> (<see cref="EffectiveStats"/>), что и в бою: из
    /// снимка показ берёт размер тела и полосы, и разойтись они не должны. <b>Чего каскад не знает</b> —
    /// пассивок из <c>GrantedEffects</c>: они накладываются системой эффектов уже в бою. У кита с
    /// пассивным «+X к HP» полоса во дворе покажет число ниже боевого; тот же охват, что у панели
    /// инвентаря (<see cref="UnitStatPreview"/>), и по той же причине.</para>
    /// <para><b>Id тела — индекс слота в ростере.</b> Совпадение с боевым id не гарантируется и не
    /// требуется: при входе в бой состав арены меняется целиком, и показ пересобирает виды по новому
    /// кадру. Требуется другое — чтобы id было устойчиво между перестановками отряда, иначе вид
    /// пересоздавался бы на каждое движение в хабе.</para>
    /// </remarks>
    public sealed class WorldBodyBuilder
    {
        private readonly StatsConfig        _config;
        private readonly ClassBalanceConfig _classBalance;

        public WorldBodyBuilder(StatsConfig config, ClassBalanceConfig classBalance)
        {
            _config       = config;
            _classBalance = classBalance;
        }

        /// <summary>
        /// Ростер игрока → тела на арене (team 0). Пустой/отсутствующий ростер даёт пустой список —
        /// пустая арена законна (главное меню, хаб до сбора отряда).
        /// </summary>
        public List<WorldBody> Build(IReadOnlyList<PlayerSlot> roster, IReadOnlyList<ItemData> partyItems)
        {
            var bodies = new List<WorldBody>(roster?.Count ?? 0);
            if (roster == null) return bodies;

            for (int i = 0; i < roster.Count; i++)
            {
                PlayerSlot slot = roster[i];
                if (slot.Relic == null) continue; // слот без кита нечем ставить (см. GuildRoster)

                bodies.Add(BuildOne(slot.Relic, slot.Vessel, slot.Position,
                                    EncounterLoader.CombineItems(slot.Items, partyItems),
                                    slot.Consequences, id: i));
            }
            return bodies;
        }

        /// <summary>Одно тело: где стоит, кто это, и какие у него числа на полосах.</summary>
        private WorldBody BuildOne(UnitData data, VesselData vessel, Vector2 position,
                                   IReadOnlyList<ItemData> items,
                                   IReadOnlyList<ConsequenceData> consequences, int id)
        {
            Stats stats = EffectiveStats.Build(data, vessel, items, consequences, _config, _classBalance);

            // Тело МИРА стоит целым: StartHpPct — про вход в бой, а не про то, как боец выглядит во
            // дворе. Травма по самому MaxHP здесь видна (потолок ниже), травма по стартовому запасу —
            // нет, и это верно: до боя запас ещё не срезан.
            float maxHp = stats.Get(StatType.MaxHP);

            var body = new UnitSnapshot(
                id,
                team: 0,
                position,
                // Прошлая позиция равна текущей: интерполировать у стоящего тела нечего, и показ
                // обязан увидеть именно это, а не рывок из нуля координат.
                previousPosition: position,
                currentHp: maxHp,
                maxHp: maxHp,
                currentShield: 0f,
                currentResource: stats.Get(StatType.StartResource),
                maxResource: stats.Get(StatType.MaxResource),
                size: stats.Get(StatType.Size),
                phase: AttackPhase.Idle,
                windupTicks: 0,
                windupRemaining: 0,
                attackCooldownTicks: 0,
                targetId: -1,
                effectTagMask: EffectTag.None,
                isDead: false,
                attackRange: stats.Get(StatType.AttackRange));

            return new WorldBody(body, new UnitIdentity(data, team: 0, id));
        }
    }
}
