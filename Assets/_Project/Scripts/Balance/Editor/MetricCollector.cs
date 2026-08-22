using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>Метрики одного юнита за бой (агрегат по outward-событиям сима).</summary>
    internal sealed class UnitMetric
    {
        public int Id;
        public string Label;
        public string Archetype;
        public int Team;

        // --- ПРИЗЫВЫ ---
        // Тело получает СВОЮ строку метрик, как любой юнит: «сколько бьёт один скелет» — такой же
        // законный вопрос, как «сколько бьёт кит». Связь с хозяином держит OwnerId, и по ней же
        // строится третий взгляд — призыватель вместе с армией.

        /// <summary>Id хозяина для призванного тела; −1 у обычного юнита. Цепочка свёрнута до корня.</summary>
        public int OwnerId = -1;

        /// <summary>Строка описывает призванное тело, а не бойца отряда.</summary>
        public bool IsSummon => OwnerId >= 0;

        /// <summary>Тик появления. У расставленных до боя — 0.</summary>
        public int SpawnTick;

        /// <summary>
        /// Сколько тиков юнит прожил в бою. У тел это шкала аптайма: сумма по армии, поделённая на
        /// длительность боя, даёт СРЕДНЕЕ ЧИСЛО ЖИВЫХ ТЕЛ — без неё «набрал восемь к сороковой секунде»
        /// неотличимо от «держал три весь бой», а играются они противоположно.
        /// </summary>
        public int AliveTicks;

        public double DamageDealt;
        public double DamageTaken;
        public double ShieldAbsorbed;
        public double Overkill;
        public double HealingDone;

        // Разбивка нанесённого урона по источнику (авто-атака / способность / DoT / ответка).
        public double DamageAuto;
        public double DamageAbility;
        public double DamagePeriodic;
        public double DamageReactive;

        // Авто-атака отдельно по школам: расщеплённый кит (The Pyre) бьёт одной атакой в две школы,
        // и без этого разреза «клинок» и «огонь» слипаются в одно число.
        public double DamageAutoPhysical;
        public double DamageAutoMagical;

        /// <summary>
        /// Сколько урона добавили уязвимости цели («Угли»). Справочная величина: она НЕ отдельное
        /// слагаемое, а часть, уже сидящая внутри строк выше.
        /// </summary>
        public double DamageFromVulnerability;

        /// <summary>
        /// Урон, нанесённый САМОМУ СЕБЕ (плата за разгон у «Пылающих клинков»). Считается отдельно и в
        /// <see cref="DamageDealt"/> НЕ входит: собственная кровь — цена кита, а не его вклад в бой.
        /// </summary>
        public double SelfDamage;

        // --- ВЫЖИВАЕМОСТЬ: чем именно юнит не умер ---
        // Стенд мерил только «сколько прожил», и любой танк выглядел одинаково — а держатся они разным:
        // один бронёй, другой щитом, третий тем, что его лечат. Без разреза не видно, что чинить.

        /// <summary>Полученное лечение (от союзников и от себя).</summary>
        public double HealingReceived;

        /// <summary>
        /// Сколько урона приняли на себя щиты, которые ВЫДАЛ этот юнит (свои и чужие). Вторая половина
        /// поддержки рядом с <see cref="HealingDone"/>: щитовик не лечит вовсе, и без этой строки вся его
        /// работа читалась нулём.
        /// </summary>
        /// <remarks>
        /// Считается по факту поглощения, а не по величине выданного щита: невостребованный щит команде
        /// ничего не сберёг, и приписывать его как работу значило бы награждать за холостой каст.
        /// </remarks>
        public double ShieldGranted;

        /// <summary>Урон, срезанный бронёй и эффективностями до того, как коснулся щита или HP.</summary>
        public double DamageMitigated;

        /// <summary>Сколько раз входящий удар был отменён целиком («Отход»).</summary>
        public int HitsEvaded;

        // --- КОНТРОЛЬ: сколько времени юнит отнял у врагов ---

        /// <summary>Суммарные секунды контроля, наложенного на ВРАГОВ (стан/корень/немота/подкидывание).</summary>
        public double ControlSecondsDealt;

        /// <summary>Сколько раз наложил контроль на врагов.</summary>
        public int ControlAppliedCount;

        /// <summary>Секунды контроля, полученные самим юнитом.</summary>
        public double ControlSecondsTaken;

        /// <summary>
        /// Счёт контроля: сумма «секунды × <see cref="EffectData.ControlWeight"/>» по всем наложенным
        /// на врагов эффектам с ненулевым весом.
        /// </summary>
        /// <remarks>
        /// Рядом с <see cref="ControlSecondsDealt"/>, а не вместо: сырые секунды — факт («сколько цель
        /// простояла»), счёт — оценка («сколько это стоило»). Замедление на четыре секунды и оглушение
        /// на четыре секунды дают одинаковые секунды и вшестеро разный счёт.
        /// </remarks>
        public double ControlScore;

        /// <summary>
        /// Урон, нанесённый по цели, которая В ЭТОТ МОМЕНТ под контролем (сон, оглушение, заморозка,
        /// подброс) — не важно, чьим.
        /// </summary>
        /// <remarks>
        /// Ради китов, у которых урон живёт в окне контроля, а не в открытом размене: карточка
        /// Пожирателя снов прямо просит мерить её по интервалу «уснула → проснулась»
        /// (<c>docs/balance-issues.md</c> §BAL-032). Для обычного кита это число околонулевое, и по
        /// самому его размеру видно, к какому типу кит относится.
        /// </remarks>
        public double DamageOnControlled;

        // --- ПРОКЛЯТИЯ: порча, наложенная на врагов ---

        /// <summary>Сколько дебаффов наложил на врагов (каждое наложение, включая рефреши и стаки).</summary>
        public int DebuffsApplied;

        /// <summary>Суммарная длительность наложенных на врагов дебаффов, секунд.</summary>
        public double DebuffSecondsDealt;

        /// <summary>Наложено дебаффов с тегом яда/горения — «химия» отдельно от прочей порчи.</summary>
        public int DotsApplied;

        // --- СТАКОВЫЕ ЛИНИИ: лёд и угли ---
        // Кит стаковой линии живёт не числом наложений, а тем, СКОЛЬКО стаков он успел собрать НА ОДНОЙ
        // цели: сила там растёт со стаком, а размазанные по четверым стаки не дают ничего. Без разреза
        // «всего / на цель / максимум» вопрос «он мало кладёт или кладёт не туда» неразрешим.

        /// <summary>Наложено стаков «Изморози» суммарно по всем целям.</summary>
        public int FrostStacksApplied;

        /// <summary>Наибольшее число стаков «Изморози», доведённое до ОДНОЙ цели.</summary>
        public int FrostStacksMaxOnTarget;

        /// <summary>Сколько разных целей получили хотя бы один стак «Изморози».</summary>
        public int FrostTargets;

        /// <summary>Наложено стаков «Углей» суммарно по всем целям.</summary>
        public int EmberStacksApplied;

        /// <summary>Наибольшее число стаков «Углей» на одной цели.</summary>
        public int EmberStacksMaxOnTarget;

        // --- УТИЛИТА: что юнит дал своим ---

        /// <summary>Сколько бафов выдал СОЮЗНИКАМ (себя не считаем — свои пассивки это не помощь команде).</summary>
        public int BuffsGranted;

        /// <summary>Суммарная длительность выданных союзникам бафов, секунд.</summary>
        public double BuffSecondsGranted;

        /// <summary>
        /// Снято ЧУЖИХ дебаффов со СВОИХ (очистка). Потребление собственного дебаффа — например, крио
        /// съедает свою же «Заморозку» ульткой — сюда НЕ идёт: это конверсия его механики, не помощь команде.
        /// </summary>
        public int CleansesDone;

        public bool Died;
        public int DeathTick = -1;

        /// <summary>Остаток HP на конец боя (абсолютный). У погибшего — 0.</summary>
        public double HpLeft;

        /// <summary>Максимальное HP на конец боя — знаменатель для доли и для сумм по команде.</summary>
        public double MaxHp;

        /// <summary>Остаток HP на конец боя, доля [0,1]. У погибшего — 0.</summary>
        public double HpPctLeft => MaxHp > 0.0 ? HpLeft / MaxHp : 0.0;

        /// <summary>
        /// Имя для таблиц: у тела с пометкой. Без неё строка «погиб SkeletonSwordsman» читается как
        /// потеря бойца, хотя тело для того и призывалось, чтобы умереть вместо живого.
        /// </summary>
        public string DisplayLabel => IsSummon ? Label + " (призыв)" : Label;
    }

    /// <summary>
    /// Свёрнутая работа ВСЕЙ армии одного призывателя за бой — третий взгляд рядом с «кит сам» и
    /// «одно тело»: сколько армия нанесла, сколько приняла на себя и сколько её было.
    /// </summary>
    internal readonly struct SummonRollup
    {
        public readonly double DamageDealt;
        public readonly double DamageTaken;
        public readonly double HealingDone;
        public readonly int Spawned;
        public readonly int Deaths;
        public readonly int AliveTicks;
        public readonly int FirstSpawnTick;

        public SummonRollup(double damageDealt, double damageTaken, double healingDone,
            int spawned, int deaths, int aliveTicks, int firstSpawnTick)
        {
            DamageDealt = damageDealt;
            DamageTaken = damageTaken;
            HealingDone = healingDone;
            Spawned = spawned;
            Deaths = deaths;
            AliveTicks = aliveTicks;
            FirstSpawnTick = firstSpawnTick;
        }

        /// <summary>Среднее число живых тел за бой: аптайм армии, а не число вызовов.</summary>
        public double AvgAlive(int durationTicks)
            => durationTicks > 0 ? AliveTicks / (double)durationTicks : 0.0;

        /// <summary>Секунда появления первого тела — рампа кита. −1, если не призвал ни разу.</summary>
        public double FirstSpawnSeconds
            => FirstSpawnTick < 0 ? -1.0 : FirstSpawnTick / (double)SimConstants.TickRate;
    }

    /// <summary>Итог одного боя: исход, длительность, timeout и метрики отслеживаемых юнитов.</summary>
    /// <remarks>
    /// В <see cref="Units"/> лежат и призванные тела (у них <see cref="UnitMetric.IsSummon"/>). Всё, что
    /// считает ОТРЯД — павших, остаток HP, состав — обязано их отбросить: тело расходное, его смерть не
    /// потеря бойца, а его HP не часть запаса отряда. Инвариант держит <c>SummonMetricsTests</c>.
    /// </remarks>
    internal sealed class BattleReport
    {
        public BattleOutcome Outcome;
        public int DurationTicks;
        public bool TimedOut;
        public readonly List<UnitMetric> Units = new List<UnitMetric>();

        public double Seconds => DurationTicks / (double)SimConstants.TickRate;

        public UnitMetric Find(int id)
        {
            for (int i = 0; i < Units.Count; i++)
                if (Units[i].Id == id) return Units[i];
            return null;
        }

        /// <summary>Вся армия призывателя, свёрнутая в одну строку. Не призывал — все нули.</summary>
        public SummonRollup Summons(int ownerId)
        {
            double dealt = 0.0, taken = 0.0, healed = 0.0;
            int spawned = 0, deaths = 0, aliveTicks = 0, first = -1;

            for (int i = 0; i < Units.Count; i++)
            {
                UnitMetric m = Units[i];
                if (m.OwnerId != ownerId) continue;

                dealt += m.DamageDealt;
                taken += m.DamageTaken;
                healed += m.HealingDone;
                spawned++;
                if (m.Died) deaths++;
                aliveTicks += m.AliveTicks;
                if (first < 0 || m.SpawnTick < first) first = m.SpawnTick;
            }

            return new SummonRollup(dealt, taken, healed, spawned, deaths, aliveTicks, first);
        }
    }

    /// <summary>
    /// Собирает боевые метрики, подписавшись на outward C#-события сима (<see cref="CombatSimulation.OnDamageDealt"/>
    /// и др.) — тот самый развязанный от презентации шов. НЕ пересчитывает формулы урона (значения берём
    /// из фактических <see cref="DamageResult"/>), чтобы «таблица не врала». Живёт ровно на время боя вместе с симом.
    /// </summary>
    internal sealed class MetricCollector
    {
        private readonly CombatSimulation _sim;
        private readonly Dictionary<int, UnitMetric> _byId = new Dictionary<int, UnitMetric>();
        private readonly Dictionary<int, RuntimeUnit> _unitById = new Dictionary<int, RuntimeUnit>();

        /// <summary>Теги, по которым эффект считается контролем (отнимает у цели действия).</summary>
        private const EffectTag ControlTags =
            EffectTag.Control | EffectTag.Frozen | EffectTag.KnockUp | EffectTag.Sleep;

        /// <summary>Теги «химии» — яд и горение: их считаем отдельно от прочей порчи.</summary>
        private const EffectTag DotTags = EffectTag.DoT | EffectTag.Poison | EffectTag.Burn;

        public MetricCollector(CombatSimulation sim, IReadOnlyList<TrackedUnit> tracked, EffectSystem effects = null)
        {
            _sim = sim;
            for (int i = 0; i < tracked.Count; i++)
            {
                RuntimeUnit u = tracked[i].Unit;
                _unitById[u.Id] = u;
                _byId[u.Id] = new UnitMetric
                {
                    Id = u.Id,
                    Label = tracked[i].Label,
                    Archetype = tracked[i].Archetype,
                    Team = u.Team,
                };
            }

            sim.OnDamageDealt += HandleDamage;
            sim.OnHealed += HandleHeal;
            sim.OnUnitDied += HandleDeath;
            sim.OnAttackEvaded += HandleEvaded;
            sim.OnShieldAbsorbed += HandleShieldAbsorbed;
            sim.OnUnitSpawned += HandleSpawned;

            // Контроль, проклятия и выданные бафы видны только на шве наложения эффекта. Он необязателен:
            // часть бенчей строит окружение без общего EffectSystem, и тогда эти корзины просто пусты.
            if (effects != null)
            {
                effects.OnEffectApplied += HandleEffectApplied;
                effects.OnEffectDispelled += HandleDispelled;
            }
        }

        /// <summary>
        /// Эффект сорван диспелом. В утилиту идёт только настоящая очистка: снятый со СВОЕГО ЧУЖОЙ дебафф.
        /// Съеденный собственной ульткой свой же эффект (крио конвертирует «Заморозку» в стан) — механика
        /// кита, а не помощь команде, и в счёт не попадает.
        /// </summary>
        private void HandleDispelled(RuntimeUnit target, EffectData def, RuntimeUnit dispeller, RuntimeUnit caster)
        {
            if (def == null || target == null || dispeller == null) return;
            if (!_byId.TryGetValue(dispeller.Id, out UnitMetric dm)) return;

            bool onAlly = target.Team == dispeller.Team;
            bool foreignEffect = caster == null || caster.Team != dispeller.Team;

            if (onAlly && foreignEffect && def.Polarity == EffectPolarity.Debuff) dm.CleansesDone++;
        }

        /// <summary>
        /// Эффект лёг на цель: раскладываем по корзинам «контроль», «проклятия» и «утилита».
        /// </summary>
        /// <remarks>
        /// Длительность берём через <see cref="EffectSystem.ResolveDurationTicks"/> — ту же функцию, что
        /// считает её в бою, а не по <c>BaseDuration</c> из ассета: сопротивление контролю и эффективности
        /// сокращают реальную длительность, и без них стенд рапортовал бы задуманное, а не случившееся.
        /// Постоянные эффекты (−1) в секунды не идут: у пассивки нет длительности, которую можно сложить.
        /// </remarks>
        private void HandleEffectApplied(RuntimeUnit target, EffectData def, RuntimeUnit source)
        {
            if (def == null || target == null || source == null) return;
            if (!_byId.TryGetValue(source.Id, out UnitMetric sm)) return;

            int ticks = EffectSystem.ResolveDurationTicks(def, source, target);
            double seconds = ticks > 0 ? ticks / (double)SimConstants.TickRate : 0.0;
            bool onEnemy = target.Team != source.Team;
            bool onSelf = ReferenceEquals(target, source);

            if ((def.Tags & ControlTags) != 0)
            {
                if (onEnemy)
                {
                    sm.ControlSecondsDealt += seconds;
                    sm.ControlAppliedCount++;
                }

                if (_byId.TryGetValue(target.Id, out UnitMetric tmc)) tmc.ControlSecondsTaken += seconds;
            }

            // Счёт контроля идёт по ВЕСУ, а не по тегам: замедление тега контроля не несёт (оно живёт
            // модификатором скорости), но стоит своей доли — иначе кит, тормозящий врага весь бой,
            // числился бы вовсе не контроллером.
            if (onEnemy && def.ControlWeight > 0f) sm.ControlScore += seconds * def.ControlWeight;

            if (onEnemy && def.Polarity == EffectPolarity.Debuff)
            {
                sm.DebuffsApplied++;
                sm.DebuffSecondsDealt += seconds;
                if ((def.Tags & DotTags) != 0) sm.DotsApplied++;
            }

            // Баф себе — это своя пассивка, а не помощь команде: в утилиту идёт только выданное ДРУГИМ.
            if (!onEnemy && !onSelf && def.Polarity == EffectPolarity.Buff)
            {
                sm.BuffsGranted++;
                sm.BuffSecondsGranted += seconds;
            }
        }

        /// <summary>
        /// Родилось призванное тело — заводим ему СВОЮ строку метрик. Дальше его считают те же
        /// обработчики, что и всех: отдельного пути для призывов нет, есть только пометка хозяина.
        /// </summary>
        /// <remarks>
        /// Хозяин ищется по цепочке <see cref="RuntimeUnit.Summoner"/> до первого отслеживаемого: призыв
        /// призыва работает на того же кита, и его вклад должен доехать до корня, а не потеряться на
        /// безымянном посреднике. Тело без отслеживаемого хозяина (вражеский костолом в энкаунтере)
        /// строку не получает — приписывать его некому, а в отряд врага стенд и так не смотрит.
        /// </remarks>
        private void HandleSpawned(RuntimeUnit unit)
        {
            if (unit == null || !unit.IsSummon || _byId.ContainsKey(unit.Id)) return;

            int ownerId = RootOwnerId(unit);
            if (ownerId < 0) return;

            _unitById[unit.Id] = unit;
            _byId[unit.Id] = new UnitMetric
            {
                Id = unit.Id,
                Label = unit.Unit != null ? unit.Unit.name : "summon",
                Archetype = _byId[ownerId].Archetype,
                Team = unit.Team,
                OwnerId = ownerId,
                SpawnTick = _sim.CurrentTick,
            };
        }

        /// <summary>Id первого ОТСЛЕЖИВАЕМОГО хозяина в цепочке призывов; −1, если такого нет.</summary>
        private int RootOwnerId(RuntimeUnit summon)
        {
            RuntimeUnit owner = summon.Summoner;
            for (int guard = 0; owner != null && guard < 8; guard++)
            {
                if (_byId.TryGetValue(owner.Id, out UnitMetric m))
                    return m.IsSummon ? m.OwnerId : m.Id;   // призыв призыва сворачиваем до корня
                owner = owner.Summoner;
            }
            return -1;
        }

        /// <summary>
        /// Щит поглотил урон. Автору идёт работа щита, носителю — уже посчитанный в
        /// <see cref="HandleDamage"/> приём урона; двойного счёта нет, это разные корзины.
        /// </summary>
        private void HandleShieldAbsorbed(RuntimeUnit author, RuntimeUnit target, float amount)
        {
            if (author != null && _byId.TryGetValue(author.Id, out UnitMetric m)) m.ShieldGranted += amount;
        }

        private void HandleEvaded(RuntimeUnit attacker, RuntimeUnit target)
        {
            if (target != null && _byId.TryGetValue(target.Id, out UnitMetric m)) m.HitsEvaded++;
        }

        private void HandleDamage(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            // Самоурон уходит в свою графу и не разбивается по источникам: иначе плата за разгон
            // («Пылающие клинки» жгут своего носителя) читалась бы стендом как нанесённый по врагу урон.
            if (source != null && ReferenceEquals(source, target))
            {
                if (_byId.TryGetValue(source.Id, out UnitMetric self)) self.SelfDamage += result.TotalDamage;
            }
            else if (source != null && _byId.TryGetValue(source.Id, out UnitMetric sm))
            {
                sm.DamageDealt += result.TotalDamage;
                sm.DamageFromVulnerability += result.VulnerabilityBonus;
                switch (result.SourceKind)
                {
                    case DamageSourceKind.AutoAttack:
                        sm.DamageAuto += result.TotalDamage;
                        if (result.School == DamageSchool.Magical) sm.DamageAutoMagical += result.TotalDamage;
                        else sm.DamageAutoPhysical += result.TotalDamage;
                        break;
                    case DamageSourceKind.Ability:    sm.DamageAbility += result.TotalDamage; break;
                    case DamageSourceKind.Periodic:   sm.DamagePeriodic += result.TotalDamage; break;
                    case DamageSourceKind.Reactive:   sm.DamageReactive += result.TotalDamage; break;
                }

                // Маска тегов читается у ЦЕЛИ на момент удара, а не история контроля: вопрос «попал ли
                // удар в окно» решается состоянием цели, и только оно.
                if (target != null && (target.EffectTagMask & ControlTags) != 0)
                    sm.DamageOnControlled += result.TotalDamage;
            }

            if (target != null && _byId.TryGetValue(target.Id, out UnitMetric tm))
            {
                tm.DamageTaken += result.TotalDamage;
                tm.ShieldAbsorbed += result.ShieldDamage;
                tm.DamageMitigated += result.Mitigated;   // сколько не пустила защита — часть ответа «чем не умер»
                // Оверкилл — урон сверх остатка HP: после смертельного удара CurrentHP уходит в минус.
                if (result.KilledTarget && target.CurrentHP < 0f)
                    tm.Overkill += -target.CurrentHP;
            }
        }

        private void HandleHeal(RuntimeUnit source, RuntimeUnit target, float amount)
        {
            if (source != null && _byId.TryGetValue(source.Id, out UnitMetric sm))
                sm.HealingDone += amount;

            // Полученное лечение — часть ответа «чем он не умер»: танк на броне и танк под хилером
            // живут одинаково долго, но чинить их надо разное.
            if (target != null && _byId.TryGetValue(target.Id, out UnitMetric tm))
                tm.HealingReceived += amount;
        }

        private void HandleDeath(RuntimeUnit unit)
        {
            if (unit != null && _byId.TryGetValue(unit.Id, out UnitMetric m) && !m.Died)
            {
                m.Died = true;
                m.DeathTick = _sim.CurrentTick;
            }
        }

        public BattleReport Build(BattleOutcome outcome, int durationTicks, bool timedOut)
        {
            var report = new BattleReport
            {
                Outcome = outcome,
                DurationTicks = durationTicks,
                TimedOut = timedOut,
            };
            foreach (UnitMetric m in _byId.Values)
            {
                // Остаток HP снимается ЗДЕСЬ, в конце боя: событий «HP изменилось» сим не шлёт, а
                // держать зеркало HP по урону и хилу — второй источник правды на ровном месте.
                if (_unitById.TryGetValue(m.Id, out RuntimeUnit u))
                {
                    m.MaxHp = u.Stats.Get(StatType.MaxHP);
                    m.HpLeft = m.Died ? 0.0 : Mathf.Max(0f, u.CurrentHP);
                }

                // Прожитое считается ЗДЕСЬ по двум уже известным тикам, а не накапливается по ходу боя:
                // счётчик времени жизни был бы вторым источником правды рядом с тиком смерти.
                int endTick = m.Died ? m.DeathTick : durationTicks;
                m.AliveTicks = Mathf.Max(0, endTick - m.SpawnTick);

                report.Units.Add(m);
            }
            report.Units.Sort((a, b) => a.Id.CompareTo(b.Id));
            return report;
        }
    }
}
