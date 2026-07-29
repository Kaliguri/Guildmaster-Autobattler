using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Владелец жизненного цикла эффектов: наложение (длительность, стакинг, потенция, маска тегов),
    /// тик таймеров и периодики, истечение с корректным teardown. Вызывается из
    /// <see cref="CombatSimulation"/> (вики «12» §3.3, §3.5, §6).
    /// </summary>
    public sealed class EffectSystem
    {
        // Переиспользуемые буферы — итерируем по копии refs, чтобы Apply/Dispel во время
        // тика не ломали коллекцию (вики «6» §7: добавления/удаления вне итерации).
        private readonly List<RuntimeEffect> _tickBuffer = new List<RuntimeEffect>();
        private readonly List<RuntimeEffect> _dispatchBuffer = new List<RuntimeEffect>();
        private readonly List<RuntimeEffect> _dispelBuffer = new List<RuntimeEffect>();
        private readonly List<RuntimeEffect> _preDamageBuffer = new List<RuntimeEffect>();
        private readonly List<RuntimeEffect> _removeByTagBuffer = new List<RuntimeEffect>();
        private readonly PreDamageResult     _preDamageResult = new PreDamageResult();

        /// <summary>
        /// Эффект закончился на юните: (юнит, источник эффекта, теги эффекта). Единый шов «эффект истёк/снят»
        /// (вики «12» §3.4). CombatSimulation ретранслирует в <see cref="Effects.CombatEvent.EffectExpired"/>
        /// (носитель = источник). На нём завязаны реактивы монаха (§10.6). Мгновенные эффекты (ticks==0) сюда
        /// НЕ попадают — они не персистятся, их OnExpire зовётся прямо в Apply.
        /// </summary>
        public event System.Action<RuntimeUnit, RuntimeUnit, Data.Definitions.EffectTag> OnEffectExpired;

        /// <summary>
        /// Эффект наложен на юнита: (цель, определение, источник). В отличие от <see cref="OnEffectExpired"/>
        /// несёт САМО определение — по нему презентация строит ключ вида <c>effect.frozen.apply</c>.
        /// Стрельнёт и на первом наложении, и на стаке/рефреше, и на мгновенном эффекте: игроку одинаково
        /// важно услышать, что статус лёг. Sim на это событие не завязана — только наблюдатели.
        /// </summary>
        public event System.Action<RuntimeUnit, EffectData, RuntimeUnit> OnEffectApplied;

        /// <summary>
        /// Эффект закончился: (цель, определение, источник). Пара к <see cref="OnEffectApplied"/> — то же
        /// событие, что <see cref="OnEffectExpired"/>, но с определением вместо одних тегов. Заведено
        /// отдельным швом, чтобы не менять сигнатуру, на которой висят боевые реактивы.
        /// </summary>
        public event System.Action<RuntimeUnit, EffectData, RuntimeUnit> OnEffectEnded;

        /// <summary>
        /// Эффект СНЯТ диспелом: (цель, определение, кто снял, кто накладывал). Отдельно от
        /// <see cref="OnEffectEnded"/>, потому что по нему нельзя отличить «истёк сам» от «сорвали», а
        /// разница смысловая: сорванная с союзника чужая порча — заслуга снявшего.
        /// </summary>
        /// <remarks>
        /// Четвёртый аргумент (автор снятого эффекта) нужен, чтобы не записать в очистку собственную
        /// механику: криомант съедает ульткой свою же «Заморозку», и снявший там совпадает с наложившим.
        /// </remarks>
        public event System.Action<RuntimeUnit, EffectData, RuntimeUnit, RuntimeUnit> OnEffectDispelled;

        /// <summary>
        /// Шаг всех эффектов на всех юнитах: периодика → countdown длительности → истечение.
        /// Вставляется в тик-цикл перед DeathSystem (DoT может добить).
        /// </summary>
        public void Tick(IReadOnlyList<RuntimeUnit> units, ICombatContext combat, float dt)
        {
            for (int u = 0; u < units.Count; u++)
            {
                RuntimeUnit unit = units[u];
                if (unit.IsDead || unit.ActiveEffects.Count == 0) continue;

                _tickBuffer.Clear();
                _tickBuffer.AddRange(unit.ActiveEffects);

                for (int e = 0; e < _tickBuffer.Count; e++)
                {
                    RuntimeEffect eff = _tickBuffer[e];
                    // Мог быть снят диспелом/реапплаем в этом же тике.
                    if (!unit.ActiveEffects.Contains(eff)) continue;

                    TickPeriodic(unit, eff, combat);
                    if (unit.IsDead) break;

                    if (!eff.IsPermanent)
                    {
                        eff.RemainingTicks--;
                        if (eff.RemainingTicks <= 0) Expire(unit, eff, combat);
                    }
                }

                if (!unit.IsDead) RecomputeControl(unit);
            }
        }

        /// <summary>
        /// Вставить эффект так, чтобы список оставался упорядоченным по <see cref="EffectData.Id"/>.
        /// </summary>
        /// <remarks>
        /// Порядок в <see cref="RuntimeUnit.ActiveEffects"/> — не косметика: по нему идут pre-damage
        /// реакции, тик периодики и чек-сумма. Пока он был порядком НАЛОЖЕНИЯ, он хранил историю, а не
        /// состояние: каждый боец вешал своё раньше чужого, поэтому у зеркальных монахов набор эффектов
        /// совпадал, а очередь была разной («sys.airborne» против «effect.vortex_hold» на втором месте).
        /// Идентификатор годится в ключ, потому что на цели эффект живёт в ОДНОМ экземпляре на определение
        /// (см. <see cref="FindEffect"/>), — значит порядок получается полным и одинаковым с обеих сторон.
        /// Вставка, а не сортировка на коммите: наложение случается несравнимо реже, чем тик.
        /// </remarks>
        private static void Insert(List<RuntimeEffect> effects, RuntimeEffect effect)
        {
            string id = effect.Def != null ? effect.Def.Id : string.Empty;

            for (int i = 0; i < effects.Count; i++)
            {
                string other = effects[i].Def != null ? effects[i].Def.Id : string.Empty;
                if (string.CompareOrdinal(id, other) < 0)
                {
                    effects.Insert(i, effect);
                    return;
                }
            }

            effects.Add(effect);
        }

        /// <summary>Пересобрать флаги контроля из активных <see cref="ControlComponent"/> (перекрытие без счётчиков).</summary>
        private static void RecomputeControl(RuntimeUnit unit)
        {
            bool canAct = true, canMove = true, canCast = true;

            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int e = 0; e < effects.Count; e++)
            {
                IEffectComponent[] comps = effects[e].Def.Components;
                if (comps == null) continue;

                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is ControlComponent control)
                    {
                        if (control.PreventAct)  canAct  = false;
                        if (control.PreventMove) canMove = false;
                        if (control.PreventCast) canCast = false;
                    }
                }
            }

            unit.CanAct  = canAct;
            unit.CanMove = canMove;
            unit.CanCast = canCast;
        }

        /// <summary>
        /// Наложить эффект на цель. Резолвит длительность (эфф-эффективности), потенцию (снимок
        /// статов источника), обрабатывает стакинг, добавляет <see cref="RuntimeEffect"/> и зовёт
        /// <c>OnApply</c> компонентов. Мгновенный эффект (BaseDuration = 0) не персистится.
        /// </summary>
        public void Apply(RuntimeUnit target, EffectData def, RuntimeUnit source, ICombatContext combat)
        {
            if (def == null || target == null || target.IsDead) return;

            RuntimeEffect existing = FindEffect(target, def);
            if (existing != null)
            {
                ApplyStacking(existing, def, source, target, combat);
                OnEffectApplied?.Invoke(target, def, source);
                return;
            }

            int componentCount = def.Components?.Length ?? 0;
            var effect = new RuntimeEffect
            {
                Def           = def,
                Source        = source,
                ScaledPotency = new float[componentCount],
                PeriodicTicks = new int[componentCount],
                // Тик появления: по нему снятие отличает «висело до этого тика» от «легло только что».
                AppliedTick   = combat?.CurrentTick ?? 0,
            };

            effect.AddContribution(source);   // первый вкладчик — тот, кто наложил

            int ticks = ResolveDurationTicks(def, source, target);
            effect.RemainingTicks    = ticks;
            effect.FullDurationTicks = ticks;

            // Снимок потенции на компонент из статов источника на момент наложения.
            for (int i = 0; i < componentCount; i++)
            {
                if (def.Components[i] is IScalablePotency scalable && source != null)
                {
                    effect.ScaledPotency[i] = scalable.Potency.Resolve(source.Stats);
                }
            }

            bool instant = ticks == 0;
            // Эффект встаёт в список сразу (иначе второе наложение этим же тиком завело бы дубль вместо
            // стака), но ВИДИМЫМ — в маске тегов — становится на коммите в конце тика. Закон видимости.
            if (!instant) Insert(target.ActiveEffects, effect);

            for (int i = 0; i < componentCount; i++)
            {
                if (def.Components[i] is IRuntimeEffectComponent rc)
                {
                    rc.OnApply(MakeContext(target, source, combat, effect, i, 0f));
                }
            }

            // Мгновенный эффект (ticks == 0) не персистится в ActiveEffects, значит его OnExpire
            // иначе не вызовется никогда. Зовём сразу — чтобы stateful-компоненты (StatModifier,
            // Shield) не «утекли» вечным баффом. Чисто-мгновенные компоненты делают работу в
            // OnApply, а их OnExpire — no-op, так что двойного эффекта нет.
            if (instant)
            {
                for (int i = 0; i < componentCount; i++)
                {
                    if (def.Components[i] is IRuntimeEffectComponent rc)
                    {
                        rc.OnExpire(MakeContext(target, source, combat, effect, i, 0f));
                    }
                }
            }

            OnEffectApplied?.Invoke(target, def, source);
        }

        /// <summary>
        /// Принудительно снять эффект с корректным <c>OnExpire</c> и пересборкой маски тегов.
        /// Используется диспелом (Stage 7).
        /// </summary>
        public void Remove(RuntimeUnit unit, RuntimeEffect effect, ICombatContext combat)
        {
            Expire(unit, effect, combat);
            // Снятый эффект мог нести контроль. Пересчитать флаги здесь, иначе при удалении
            // ПОСЛЕДНЕГО эффекта юнит больше не попадёт в EffectSystem.Tick (гард Count==0)
            // и останется навсегда с замороженными CanAct/CanMove/CanCast.
            RecomputeControl(unit);
        }

        /// <summary>
        /// Синхронный pre-damage проход (§9.3): до <see cref="DamagePipeline.Execute"/> опросить
        /// <see cref="IPreDamageComponent"/> цели — «Оплот» успевает поднять щит, поглощающий сам
        /// триггер-удар. Итерация по копии (реакция может наложить эффект), порядок по индексу
        /// <see cref="RuntimeUnit.ActiveEffects"/> → детерминизм (гейт S5 влит в срез-тесты).
        /// </summary>
        public bool RunPreDamage(RuntimeUnit target, in DamageRequest req, ICombatContext combat)
        {
            _preDamageResult.Reset();
            if (target == null || target.IsDead || target.ActiveEffects.Count == 0) return false;

            _preDamageBuffer.Clear();
            _preDamageBuffer.AddRange(target.ActiveEffects);

            for (int e = 0; e < _preDamageBuffer.Count; e++)
            {
                RuntimeEffect eff = _preDamageBuffer[e];
                if (!target.ActiveEffects.Contains(eff)) continue;

                IEffectComponent[] comps = eff.Def.Components;
                if (comps == null) continue;

                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is IPreDamageComponent pre)
                    {
                        // Выведенный контролем щита не поднимает и в кувырок не уходит: это ДЕЙСТВИЯ, и
                        // маркер на компоненте говорит, что они таковы (см. IRequiresAgencyComponent).
                        // Читается СНИМОК на начало тика, а не живой флаг: живой меняется посреди тика, и
                        // реакция стала бы зависеть от порядка юнитов в списке.
                        if (!target.CanActAtTickStart && comps[i] is IRequiresAgencyComponent) continue;

                        EffectContext ctx = MakeContext(target, eff.Source, combat, eff, i, 0f);
                        pre.OnPreDamage(in req, _preDamageResult, in ctx);
                    }
                }
            }

            return _preDamageResult.Negated;
        }

        /// <summary>
        /// Множитель входящего урона, накопленный компонентами цели в последнем
        /// <see cref="RunPreDamage"/> (1 = без изменений). Читается сразу после вызова — состояние
        /// живёт до следующего прохода, как и <c>Negated</c>.
        /// </summary>
        public float PreDamageMultiplier => _preDamageResult.DamageMultiplier;

        /// <summary>
        /// Расщепляет ли какой-нибудь эффект АТАКУЮЩЕГО его авто-атаку по школам (The Pyre: по
        /// горящей цели половина клинка уходит Огнём). Побеждает первый сработавший — порядок по
        /// индексу активных эффектов, как и в pre-damage проходе.
        /// </summary>
        public bool TryResolveAttackSplit(RuntimeUnit attacker, RuntimeUnit target, ICombatContext combat,
                                          out AttackSplit split)
        {
            split = default;
            if (attacker == null || target == null || attacker.ActiveEffects.Count == 0) return false;

            List<RuntimeEffect> effects = attacker.ActiveEffects;
            for (int e = 0; e < effects.Count; e++)
            {
                RuntimeEffect eff = effects[e];
                IEffectComponent[] comps = eff.Def.Components;
                if (comps == null) continue;

                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is not IAttackSplitComponent splitter) continue;

                    EffectContext ctx = MakeContext(attacker, eff.Source, combat, eff, i, 0f);
                    if (splitter.TrySplit(attacker, target, in ctx, out split)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Сколько НАКИДЫВАЕТ атакующий за то, каким стало состояние цели (Криомант по замороженным).
        /// Возвращает долю: 0 = обычный удар, 0.25 = +25%. Вклады разных компонентов складываются —
        /// это усиление источника, и ведёт себя как плоские прибавки в статах.
        /// </summary>
        public float ResolveOutgoingDamageBonus(
            RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, ICombatContext combat)
        {
            if (attacker == null || target == null || attacker.ActiveEffects.Count == 0) return 0f;

            float bonus = 0f;
            List<RuntimeEffect> effects = attacker.ActiveEffects;
            for (int e = 0; e < effects.Count; e++)
            {
                RuntimeEffect eff = effects[e];
                IEffectComponent[] comps = eff.Def.Components;
                if (comps == null) continue;

                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is not IOutgoingDamageBonusComponent booster) continue;

                    EffectContext ctx = MakeContext(attacker, eff.Source, combat, eff, i, 0f);
                    bonus += booster.BonusAgainst(attacker, target, isAutoAttack, in ctx);
                }
            }
            return bonus;
        }

        /// <summary>
        /// Доставить боевое событие реактивным компонентам носителя (вампиризм/шипы). Вызывается
        /// из <see cref="CombatSimulation"/> при дренаже event-queue. Итерация по копии — реакция
        /// может добавить/снять эффекты (вики «12» §3.4).
        /// </summary>
        public void Dispatch(RuntimeUnit carrier, in CombatEventData ev, ICombatContext combat)
        {
            if (carrier == null || carrier.ActiveEffects.Count == 0) return;

            _dispatchBuffer.Clear();
            _dispatchBuffer.AddRange(carrier.ActiveEffects);

            for (int e = 0; e < _dispatchBuffer.Count; e++)
            {
                RuntimeEffect eff = _dispatchBuffer[e];
                if (!carrier.ActiveEffects.Contains(eff)) continue;

                IEffectComponent[] comps = eff.Def.Components;
                if (comps == null) continue;

                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is IReactiveComponent reactive && (reactive.Events & ev.Type) != 0)
                    {
                        // Реакция, требующая ДЕЙСТВИЯ (рывок, кувырок, подъём щита), не проходит у
                        // выведенного контролем — в отличие от шипов, которые колют бронёй сами. Снимок на
                        // начало тика, по той же причине, что и в pre-damage.
                        if (!carrier.CanActAtTickStart && comps[i] is IRequiresAgencyComponent) continue;

                        EffectContext ctx = MakeContext(carrier, eff.Source, combat, eff, i, 0f);
                        reactive.OnEvent(in ctx, in ev);
                    }
                }
            }
        }

        /// <summary>
        /// Снять с цели подходящие эффекты: полярность ∧ теги ∧ <c>CleanseTier ≤ DispelPower</c> ∧
        /// <c>!Unremovable</c>. Порядок выбора при <c>MaxCount</c> — insertion order (детерминированно;
        /// тонкая настройка приоритета — при балансе, вики «6» §5.4, «12» §9).
        /// </summary>
        public void Dispel(in DispelRequest req, ICombatContext combat)
        {
            RuntimeUnit target = req.Target;
            if (target == null || target.ActiveEffects.Count == 0) return;

            // Снятие судит по состоянию НАЧАЛА ТИКА: эффект, легший в этом же тике, снятию не подлежит.
            //
            // Без этого диспел был единственным местом, читавшим список эффектов «как есть». Список
            // меняется немедленно (отложены только статы и маска тегов), поэтому клинс видел наложения,
            // случившиеся раньше него В ЭТОМ ЖЕ обходе, — и исход начинал зависеть от места юнита в
            // списке. Зеркало ловило это на тике 181: метку, только что наложенную левым Рейнджером,
            // клинс правого Пастыря снимал, а левый Пастырь свою метку снять не успевал — она ложилась
            // после него. Одна и та же пара бойцов расходилась на один эффект, дальше на урон и HP.
            //
            // Лечится не отложенным снятием (оно бы читало тот же свежий список), а тем же приёмом
            // двухфазности, что уже стоит в MovementSystem и AbilitySystem: решение принимается по
            // снимку начала тика. Побочно это ещё и правило дизайна — «сорвать то, что только что
            // наложили» перестало быть гонкой обхода.
            // Расход собственного триггера — исключение: там порядок задан внутри одного вызова
            // способности, а не обходом списка (см. DispelRequest.ConsumesOwnTrigger).
            int currentTick = req.ConsumesOwnTrigger ? int.MinValue : (combat?.CurrentTick ?? 0);

            _dispelBuffer.Clear();
            List<RuntimeEffect> effects = target.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].AppliedTick == currentTick) continue;
                if (MatchesDispel(effects[i].Def, in req)) _dispelBuffer.Add(effects[i]);
            }

            int removed = 0;
            for (int i = 0; i < _dispelBuffer.Count; i++)
            {
                if (req.MaxCount > 0 && removed >= req.MaxCount) break;

                RuntimeEffect eff = _dispelBuffer[i];
                removed++;

                // Цена очистки в стаках (решение 2026-07-27/5): эффект может отдать лишь часть накопленного,
                // а не исчезнуть целиком. Иначе одна очистка стирала «Угли» без потолка — ставку «долгий бой
                // окупается» гасило одно нажатие. Ноль стаков после списания = эффект уходит, как раньше.
                int toRemove = eff.Def.CleanseStacks(eff.Stacks, req.DispelPower);
                if (toRemove < eff.Stacks)
                {
                    int before = eff.Stacks;
                    eff.RemoveStacks(toRemove);
                    // Тем же путём, что и при наборе стака: компоненты со своим состоянием (щиты, заряды)
                    // правят вклад дельтой сами, остальным хватает переприменения.
                    Reapply(eff, before, target, combat);
                    continue;
                }

                EffectData def = eff.Def;
                RuntimeUnit caster = eff.Source;
                Expire(target, eff, combat);
                OnEffectDispelled?.Invoke(target, def, req.Source, caster);
            }

            // Среди снятого мог быть контроль-эффект. Пересчитать флаги (см. Remove): без этого
            // диспел последнего эффекта оставил бы юнита навсегда оглушённым/обездвиженным.
            if (removed > 0) RecomputeControl(target);
        }

        /// <summary>Длительность эффекта в тиках. -1 = постоянный, 0 = мгновенный, иначе с учётом эфф-эффективностей.</summary>
        public static int ResolveDurationTicks(EffectData def, RuntimeUnit source, RuntimeUnit target)
        {
            float seconds = def.BaseDuration;
            if (seconds < 0f)  return -1;
            if (seconds == 0f) return 0;

            float mult = DurationMultiplier(def.Polarity, source, target);
            int ticks = Mathf.RoundToInt(seconds * mult * SimConstants.TickRate);
            return Mathf.Max(1, ticks);
        }

        // --- Приватные ---

        private static void TickPeriodic(RuntimeUnit unit, RuntimeEffect eff, ICombatContext combat)
        {
            IEffectComponent[] comps = eff.Def.Components;
            if (comps == null) return;

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is IPeriodicComponent periodic && periodic.Interval > 0f)
                {
                    int intervalTicks = Mathf.Max(1, Mathf.RoundToInt(periodic.Interval * SimConstants.TickRate));
                    if (++eff.PeriodicTicks[i] >= intervalTicks)
                    {
                        eff.PeriodicTicks[i] = 0;
                        // Dt = Interval: компонент считает применяемое как Potency × Dt × Stacks
                        // (per-second rate → за период; total масштабируется числом тиков, вики «11» §5.1).
                        // Проходов столько, сколько вкладчиков: сумма долей = 1, поэтому суммарный урон
                        // тот же, но каждый кусок засчитывается ТОМУ, кто его поддерживает (реш. Макса).
                        // Делим тик по вкладчикам ТОЛЬКО у компонентов, чья величина за тик скейлится
                        // потенцией источника (урон/хил): именно её и надо засчитать тому, кто держит
                        // эффект. Компоненты состояния (например «Угли», снимающие стак по таймеру)
                        // обязаны отработать РОВНО ОДИН раз — иначе несколько вкладчиков ускорили бы
                        // сход стаков (реш. Макса 2026-07-26: делить пропорционально вкладу).
                        int total = periodic is IScalablePotency ? eff.TotalContribution : 0;
                        if (total <= 1)
                        {
                            periodic.OnTick(MakeContext(unit, eff.Source, combat, eff, i, periodic.Interval));
                            if (unit.IsDead) return;
                        }
                        else
                        {
                            for (int c = 0; c < eff.ContributorSources.Count; c++)
                            {
                                float share = eff.ContributorWeights[c] / (float)total;
                                periodic.OnTick(MakeContext(unit, eff.ContributorSources[c], combat, eff, i,
                                                            periodic.Interval, share));
                                if (unit.IsDead) return;
                            }
                        }
                    }
                }
            }
        }

        private void Expire(RuntimeUnit unit, RuntimeEffect eff, ICombatContext combat)
        {
            IEffectComponent[] comps = eff.Def.Components;
            if (comps != null)
            {
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is IRuntimeEffectComponent rc)
                    {
                        rc.OnExpire(MakeContext(unit, eff.Source, combat, eff, i, 0f));
                    }
                }
            }

            unit.ActiveEffects.Remove(eff);

            // Единый сигнал «эффект закончился» (носитель-получатель = источник эффекта, ретрансляция в
            // CombatSimulation). Реактивы фильтруют по тегам эффекта + команде юнита. Смещение (KnockUp)
            // именно так и разводит «конец отбрасывания врага → телепорт» vs «конец рывка себя → толчок».
            OnEffectExpired?.Invoke(unit, eff.Source, eff.Def.Tags);
            OnEffectEnded?.Invoke(unit, eff.Def, eff.Source);
        }

        /// <summary>
        /// Принудительно снять с юнита ВСЕ эффекты, несущие любой из <paramref name="tag"/> (игнорируя
        /// <see cref="EffectData.Unremovable"/> — это не диспел, а системное завершение, напр. конец полёта
        /// смещения). Каждый снятый прогоняется через <see cref="Expire"/> (корректный teardown + OnEffectExpired).
        /// </summary>
        public void RemoveByTag(RuntimeUnit unit, Data.Definitions.EffectTag tag, ICombatContext combat)
        {
            if (unit == null || tag == Data.Definitions.EffectTag.None || unit.ActiveEffects.Count == 0) return;

            _removeByTagBuffer.Clear();
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
                if ((effects[i].Def.Tags & tag) != 0) _removeByTagBuffer.Add(effects[i]);

            if (_removeByTagBuffer.Count == 0) return;
            for (int i = 0; i < _removeByTagBuffer.Count; i++) Expire(unit, _removeByTagBuffer[i], combat);
            RecomputeControl(unit);
        }

        /// <summary>
        /// ЗАКОН ВИДИМОСТИ ЭФФЕКТОВ: проявить всё, что эффекты наложили или сняли за этот тик — статы и
        /// маску тегов. Зовётся ровно раз, в конце <c>CombatSimulation.Tick</c>.
        /// </summary>
        /// <remarks>
        /// Смысл закона: наложенный эффект меняет статы и маску носителя не раньше конца тика — так же, как
        /// это давно сделано для флагов контроля (<c>CanAct</c>, вики «14»). Пока правка стата ложилась
        /// мгновенно, ослабление, наложенное ранним ударом, успевало срезать удар того, кто в обходе списка
        /// позже; у зеркальных сторон порядок обратный, и «место в списке» становилось игровым
        /// преимуществом. Единственное исключение — pre-damage реактивы (<see cref="RunPreDamage"/>): они по
        /// определению отвечают на конкретный удар («Оплот» поднимает щит на тот же удар, §9.3) и остаются
        /// синхронными. Это исключение сознательное — не «доисправлять» его до единообразия.
        /// </remarks>
        public void CommitTickChanges(IReadOnlyList<RuntimeUnit> units)
        {
            for (int u = 0; u < units.Count; u++)
            {
                RuntimeUnit unit = units[u];
                if (unit.IsDead) continue;
                CommitPending(unit);
            }
        }

        /// <summary>
        /// Проявить отложенное на одном юните. Отдельный вход нужен ВНЕ боевого тика: юнит, которого
        /// только что собрала фабрика, обязан родиться с уже действующими пассивками — иначе он выйдет
        /// на арену с недобранным запасом HP и погашенными метками (стелс, ауры), а ждать первого тика
        /// тут некому.
        /// </summary>
        public static void CommitPending(RuntimeUnit unit)
        {
            if (unit == null) return;
            unit.Stats?.Commit();
            RebuildTagMask(unit);

            // Стаки — часть закона видимости: набранное и срезанное за этот тик начинает влиять на исход
            // со следующего. Иначе очищение, срезавшее «Угли» ценой, обкрадывает чужую детонацию тем же
            // тиком, и результат зависит от места юнита в обходе (см. RuntimeEffect.StacksAtTickStart).
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++) effects[i].CommitStackSnapshot();
        }

        private static void RebuildTagMask(RuntimeUnit unit)
        {
            EffectTag mask = EffectTag.None;
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++) mask |= effects[i].Def.Tags;
            unit.EffectTagMask = mask;
        }

        private static EffectContext MakeContext(
            RuntimeUnit target, RuntimeUnit source, ICombatContext combat, RuntimeEffect effect, int componentIndex,
            float dt, float share = 1f, bool liveStacks = false)
        {
            float potency = effect.ScaledPotency != null && componentIndex < effect.ScaledPotency.Length
                ? effect.ScaledPotency[componentIndex]
                : 0f;
            return new EffectContext(target, source, combat, effect, potency, dt, share, liveStacks);
        }

        /// <summary>
        /// Найти на цели эффект, с которым сливается новое наложение. Ключ — <b>определение</b>:
        /// эффект живёт НА ЦЕЛИ в одном экземпляре, а <see cref="EffectData.MaxStacks"/> — потолок стаков
        /// на цели, общий для всех наложивших (правило Макса 2026-07-26). Два поджигателя догоняют один
        /// и тот же костёр до общего потолка, а не заводят по своему.
        /// <para>Ненадолго ключ был «определение + источник» — так расщеплялись стаки, и потолок
        /// становился персональным у каждого кастера. Это противоречит правилу и откачено; исходная
        /// находка (атрибуция урона DoT достаётся первому наложившему) лечится внутри экземпляра,
        /// а не разведением экземпляров — см. журнал аудита.</para>
        /// </summary>
        private static RuntimeEffect FindEffect(RuntimeUnit target, EffectData def)
        {
            List<RuntimeEffect> effects = target.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Def == def) return effects[i];
            }
            return null;
        }

        private void ApplyStacking(
            RuntimeEffect existing, EffectData def, RuntimeUnit source, RuntimeUnit target, ICombatContext combat)
        {
            int previousStacks = existing.Stacks;
            bool stacksChanged = false;

            switch (def.Stacking)
            {
                case StackRule.None:
                    return;   // повтор игнорируется целиком — вклад тоже не растёт

                case StackRule.Stack:
                    stacksChanged = TryAddStack(existing, def);
                    break;

                case StackRule.Refresh:
                    RefreshDuration(existing, def, source, target);
                    RearmOneShotComponents(existing, target, source, combat);
                    break;

                case StackRule.StackAndRefresh:
                    stacksChanged = TryAddStack(existing, def);
                    RefreshDuration(existing, def, source, target);
                    RearmOneShotComponents(existing, target, source, combat);
                    break;
            }

            // Подкрепление засчитано вкладчику: по этим весам делится атрибуция периодики (реш. Макса).
            // Для Stack-правил вес == вклад в стаки; для Refresh стаков нет, и весом становится само
            // подкрепление — иначе горение, которое двое держат по очереди, целиком висело бы на первом.
            existing.AddContribution(source);

            // Стак изменил число — переоценить stateful-вклад компонентов под новый Stacks.
            if (stacksChanged) Reapply(existing, previousStacks, target, combat);
        }

        private static bool TryAddStack(RuntimeEffect effect, EffectData def)
        {
            if (effect.Stacks >= def.MaxStacks) return false;
            effect.AddStacks(1);
            return true;
        }

        private static void RefreshDuration(RuntimeEffect effect, EffectData def, RuntimeUnit source, RuntimeUnit target)
        {
            int ticks = ResolveDurationTicks(def, source, target);
            effect.RemainingTicks    = ticks;
            effect.FullDurationTicks = ticks;
        }

        /// <summary>
        /// Подкрепление висящего эффекта: взвести заново одноразовые заряды
        /// (<see cref="IRearmOnRefreshComponent"/>). Прочие компоненты не трогаем — для них Refresh
        /// значит только «длительность с начала».
        /// </summary>
        private void RearmOneShotComponents(
            RuntimeEffect effect, RuntimeUnit target, RuntimeUnit source, ICombatContext combat)
        {
            IEffectComponent[] components = effect.Def.Components;
            if (components == null) return;

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IRearmOnRefreshComponent rearm)
                {
                    rearm.OnApply(MakeContext(target, source, combat, effect, i, 0f));
                }
            }
        }

        private void Reapply(RuntimeEffect effect, int previousStacks, RuntimeUnit target, ICombatContext combat)
        {
            // ПРАВИЛО ПОТЕНЦИИ (осознанно, не баг): снимок ScaledPotency берётся ОДИН раз при
            // наложении (Apply) и здесь НЕ пересчитывается. Потенция «заморожена» по статам
            // источника на момент первого каста (вики «11» §5.1). При добавлении стака меняется
            // только stateful-вклад компонентов под новый Stacks.
            IEffectComponent[] components = effect.Def.Components;
            if (components == null) return;

            for (int i = 0; i < components.Length; i++)
            {
                // Живые стаки: пересчёт вклада обязан видеть ТОЛЬКО ЧТО набранное число, иначе прибавка
                // не случится вовсе — второго вызова под это изменение не будет (см. EffectContext).
                EffectContext ctx = MakeContext(target, effect.Source, combat, effect, i, 0f, liveStacks: true);

                // Компонент с накопленным внешним состоянием (щит/заряды) правит вклад дельтой сам.
                // Слепой OnExpire→OnApply для него неверен (пере-вычет щита / бесплатный рефилл
                // зарядов) — 07 §3.8 B1–B3. Прочим (keyed-снятие, напр. StatModifier) — дефолт.
                if (components[i] is IStackableComponent stackable)
                {
                    stackable.OnStacksChanged(previousStacks, in ctx);
                }
                else if (components[i] is IRuntimeEffectComponent rc)
                {
                    rc.OnExpire(in ctx);
                    rc.OnApply(in ctx);
                }
            }
        }

        private static bool MatchesDispel(EffectData def, in DispelRequest req)
        {
            if (def.Unremovable) return false;
            if (def.CleanseTier > req.DispelPower) return false;

            bool polarityOk =
                req.Polarity == DispelTargetPolarity.Any ||
                (req.Polarity == DispelTargetPolarity.Buff   && def.Polarity == EffectPolarity.Buff) ||
                (req.Polarity == DispelTargetPolarity.Debuff && def.Polarity == EffectPolarity.Debuff);
            if (!polarityOk) return false;

            if (req.Tags != EffectTag.None && (def.Tags & req.Tags) == 0) return false;

            return true;
        }

        private static float DurationMultiplier(EffectPolarity polarity, RuntimeUnit source, RuntimeUnit target)
        {
            if (source == null || target == null) return 1f;

            switch (polarity)
            {
                case EffectPolarity.Buff:
                    return source.Stats.Get(StatType.ApplyBuffEff) * target.Stats.Get(StatType.ReceiveBuffEff);
                case EffectPolarity.Debuff:
                    return source.Stats.Get(StatType.ApplyDebuffEff) * target.Stats.Get(StatType.ReceiveDebuffEff);
                default:
                    return 1f; // Neutral — длительность не скейлится (вики «12» §9)
            }
        }
    }
}
