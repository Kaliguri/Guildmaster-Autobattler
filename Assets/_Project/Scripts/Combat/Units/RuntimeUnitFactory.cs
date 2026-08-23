using System.Collections.Generic;
using Guildmaster.Combat.Abilities;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Единственная точка сборки <see cref="RuntimeUnit"/> из SO-данных.
    /// Шаги сборки (вики «10» §5.2, «6» §3): дефолты из <see cref="StatsConfig"/> → классовая база
    /// (<see cref="ClassBalanceConfig"/>) → видовые скейлы врага (<see cref="SpeciesData"/>) → моды
    /// мементо → перки сосуда → предметы → последствия боёв (<see cref="ConsequenceData"/>) → пассивки
    /// (<see cref="RelicData.GrantedEffects"/> с постоянной длительностью)
    /// → активки (<see cref="AbilityRuntime"/>) → ресурс (<see cref="StatType.StartResource"/>)
    /// → <c>CurrentHP = EffectiveStats.StartingHp(...)</c>.
    /// </summary>
    /// <remarks>
    /// Пассивки применяются <b>до</b> инициализации <c>CurrentHP</c>: пассив на +MaxHP должен поднять
    /// и стартовое здоровье. Поэтому фабрике нужны <see cref="EffectSystem"/> и боевой контекст —
    /// наложение пассивки зовёт <c>OnApply</c> компонентов (вики «12» §6, шаг 9).
    /// </remarks>
    public sealed class RuntimeUnitFactory : ISummonFactory
    {
        private readonly StatsConfig   _config;
        private readonly ClassBalanceConfig _classBalance;
        private readonly EffectSystem  _effects;
        private readonly ICombatContext _combat;
        private int _nextId;

        /// <summary>Сколько юнитов уже создано на каждую команду — источник фазы стаггера AI.</summary>
        private readonly int[] _perTeamCount = new int[MaxTeams];

        /// <summary>Потолок команд боя: кооп до 4 игроков + сторона врага.</summary>
        private const int MaxTeams = 8;

        public RuntimeUnitFactory(StatsConfig config, ClassBalanceConfig classBalance,
                                  EffectSystem effects, ICombatContext combat)
        {
            _config       = config;
            _classBalance = classBalance;
            _effects      = effects;
            _combat       = combat;
        }

        /// <summary>
        /// Сбросить счётчик Id (перезапуск боя на месте, dev-R). Без этого счётчик фабрики уезжает от
        /// схемы Id болванчиков (<c>Units.Count+1</c> в харнессе) → коллизия Id → в презентере два юнита
        /// на один ключ, вид одного осиротевает (стоит на месте, HP-бар не реагирует). Раньше это лечил
        /// релоад сцены (новый скоуп → новая фабрика); теперь чистим явно.
        /// </summary>
        public void ResetIds()
        {
            _nextId = 0;
            System.Array.Clear(_perTeamCount, 0, _perTeamCount.Length);
        }

        /// <summary>
        /// Сдвинуть счётчик так, чтобы следующие Id шли ПОСЛЕ <paramref name="lastUsedId"/>. Нужно тем,
        /// кто раздал Id мимо фабрики (стенд баланса размечает бойцов по индексу списка, часть из них —
        /// синтетика без фабрики): без сдвига первое призванное в бою тело получит Id живого бойца, и
        /// всё, что оно сделает, ляжет в чужую строку. Счётчик только растёт — назад не отматывает.
        /// </summary>
        public void AdvanceIdsPast(int lastUsedId)
        {
            if (lastUsedId >= _nextId) _nextId = lastUsedId + 1;
        }

        /// <summary>
        /// Создать <see cref="RuntimeUnit"/> из SO-данных. Принимает базовый <see cref="UnitData"/> —
        /// мементо или врага; сборке всё равно, кто перед ней (вики «13» §3.1).
        /// </summary>
        /// <param name="data">Боевой кит («Чемпион»/враг). null — юнит получит только дефолты StatsConfig.</param>
        /// <param name="vessel">SO «Пилот». null — перки не применяются.</param>
        /// <param name="team">Команда: 0 = союзники, 1 = враги.</param>
        /// <param name="spawnPosition">Начальная позиция на поле боя.</param>
        /// <param name="items">
        /// Предметы юнита (Vessel-скоуп) и баннеры команды (Party-скоуп) — их статовые моды и пассивки
        /// (план 11 §5.5, D1). Применяются как перки: моды до инициализации HP (чтобы +MaxHP поднял старт).
        /// null = без предметов. Активки предметов пока не реализованы (только статы/эффекты).
        /// </param>
        /// <param name="consequences">
        /// Последствия боёв на «Сосуде» — травмы и закалки забега (ГДД <c>injuries-mettle</c>). Их моды
        /// ложатся последним слоем каскада, а травма по <see cref="StatType.StartHpPct"/> вдобавок
        /// срезает запас, с которым боец выходит на арену. null = «Сосуд» цел.
        /// </param>
        public RuntimeUnit Create(UnitData data, VesselData vessel, int team, Vector2 spawnPosition,
                                  IReadOnlyList<ItemData> items = null,
                                  IReadOnlyList<ConsequenceData> consequences = null)
        {
            // Каскад целиком — у EffectiveStats: дефолты конфига → класс → вид → персона → Судьба
            // сосуда → предметы. Своей копии здесь нет намеренно: она уже расходилась с показанными
            // игроку числами (аудит 2026-07-26), а теперь по тому же каскаду собираются ещё и тела
            // мира вне боя — три переписи одного порядка разъехались бы молча.
            Stats stats = EffectiveStats.Build(data, vessel, items, consequences, _config, _classBalance);

            int id = _nextId++;

            // Фаза стаггера считается от порядкового номера ВНУТРИ КОМАНДЫ, а не от сквозного Id.
            // Сквозной давал командам разные фазы (первая заспавненная думала раньше), и в равном бою
            // это решало исход за бойцов: зеркальный отряд заканчивал со счётом 59.7% против нуля.
            // Внутри команды фазы по-прежнему разные — нагрузка размазана, как и задумано.
            int teamIndex = team >= 0 && team < _perTeamCount.Length ? _perTeamCount[team]++ : id;

            var unit = new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentResource  = stats.Get(StatType.StartResource),
                CurrentShield    = 0f,
                Position         = spawnPosition,
                PreviousPosition = spawnPosition,
                Vessel           = vessel,
                // AI (Фаза 3): мозг из профиля кита + фаза стаггера по месту в команде (вики «13» §2.7, §4.1).
                Brain            = new ProfileBrain(data?.Ai),
                BrainPhase       = teamIndex % SimConstants.AiTickInterval,
            };

            // Форма авто-атаки — снимком, одним вызовом: тип урона, доставка, on-hit, канал.
            unit.AdoptKit(data);

            RegisterPassives(unit, data);
            RegisterItemPassives(unit, items);
            RegisterAbilities(unit, data);

            // Пассивки правят статы по закону видимости — отложенно, до конца боевого тика. Здесь тика нет
            // и ждать нечего: юнит должен родиться с уже действующими пассивками, поэтому проявляем их сразу.
            // Без этого он выходит на арену с недобранным MaxHP (и, значит, с неполным стартовым HP).
            EffectSystem.CommitPending(unit);

            // CurrentHP — после пассивок: они могли поднять MaxHP, юнит должен стартовать с полным.
            // «Полный» — это MaxHP, срезанный долей StartHpPct: раненый выходит на арену уже неполным,
            // и отыграть это можно только лечением в бою (в отличие от травмы по самому MaxHP).
            unit.CurrentHP = EffectiveStats.StartingHp(stats);

            return unit;
        }

        /// <summary>
        /// Собрать призванного юнита (M10): та же сборка, что у всех, плюс множители силы призывов от
        /// призывателя. Множители приезжают ОТДЕЛЬНОЙ группой модификаторов, поэтому база ассета остаётся
        /// читаемой в отладке: видно и «сколько у скелета своего», и «сколько добавил хозяин».
        /// </summary>
        /// <remarks>
        /// Множители применяются ДО инициализации HP — иначе призыв родился бы с полным HP по базе, а
        /// потолок вырос бы после, и усиленный скелет выходил бы уже раненым.
        /// </remarks>
        public RuntimeUnit CreateSummon(UnitData data, int team, Vector2 position, RuntimeUnit summoner)
        {
            RuntimeUnit summon = Create(data, vessel: null, team, position);

            if (summoner?.Stats == null) return summon;

            float healthEff = summoner.Stats.Get(StatType.SummonHealthEff);
            float damageEff = summoner.Stats.Get(StatType.SummonDamageEff);

            bool scalesHealth = !Mathf.Approximately(healthEff, 1f);
            bool scalesDamage = !Mathf.Approximately(damageEff, 1f);

            if (scalesHealth || scalesDamage)
            {
                // PercentMult принимает ПРИБАВКУ (множитель считается как 1 + x), а сам стат силы призывов
                // живёт вокруг единицы: 1.3 = «+30%». Отсюда −1.
                StatModifier[] mods =
                    scalesHealth && scalesDamage
                        ? new[]
                        {
                            new StatModifier(StatType.MaxHP, ModifierOp.PercentMult, healthEff - 1f),
                            new StatModifier(StatType.AutoAttackDamage, ModifierOp.PercentMult, damageEff - 1f),
                        }
                        : scalesHealth
                            ? new[] { new StatModifier(StatType.MaxHP, ModifierOp.PercentMult, healthEff - 1f) }
                            : new[] { new StatModifier(StatType.AutoAttackDamage, ModifierOp.PercentMult, damageEff - 1f) };

                summon.Stats.AddModifiersFrom("summoner", mods);

                // HP пересобираем: потолок только что вырос, а призыв обязан выйти целым.
                summon.CurrentHP = summon.Stats.Get(StatType.MaxHP);
            }

            summon.Summoner = summoner;
            return summon;
        }

        /// <summary>Наложить пассивные эффекты предметов/баннеров (источник — сам юнит, длительность из Def).</summary>
        private void RegisterItemPassives(RuntimeUnit unit, IReadOnlyList<ItemData> items)
        {
            if (items == null || _effects == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                EffectData[] granted = items[i]?.GrantedEffects;
                if (granted == null) continue;
                for (int j = 0; j < granted.Length; j++)
                    if (granted[j] != null) _effects.Apply(unit, granted[j], unit, _combat);
            }
        }

        /// <summary>Наложить пассивные эффекты кита (источник — сам юнит, длительность из Def, обычно −1).</summary>
        private void RegisterPassives(RuntimeUnit unit, UnitData data)
        {
            EffectData[] passives = data?.GrantedEffects;
            if (passives == null || _effects == null) return;

            for (int i = 0; i < passives.Length; i++)
            {
                if (passives[i] != null) _effects.Apply(unit, passives[i], unit, _combat);
            }
        }

        /// <summary>Собрать рантайм-обёртки активных способностей кита (кулдаун/ресурс/дальность каста).</summary>
        /// <remarks>
        /// Ступень дальности разрешается в число здесь, потому что дистанции ступеней живут в
        /// <see cref="StatsConfig"/> — а он есть у сборки и не должен протаскиваться в боевые системы.
        /// Наследование «как у авто-атаки» остаётся неразвёрнутым (−1): дальность удара у кита может
        /// меняться по ходу боя, и умение обязано ехать за ней.
        /// </remarks>
        private void RegisterAbilities(RuntimeUnit unit, UnitData data)
        {
            AbilityData[] abilities = data?.Abilities;
            if (abilities == null) return;

            for (int i = 0; i < abilities.Length; i++)
            {
                AbilityData ability = abilities[i];
                if (ability == null) continue;

                unit.Abilities.Add(new AbilityRuntime(ability)
                {
                    CastRange = ability.CastRange == CastRangeBand.LikeAutoAttack || _config == null
                        ? -1f
                        : _config.RangeOf((AttackRangeBand)(ability.CastRange - 1)),
                });
            }
        }
    }
}
