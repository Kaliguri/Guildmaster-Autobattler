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
                Stacks        = 1,
                ScaledPotency = new float[componentCount],
                PeriodicTicks = new int[componentCount],
            };

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
            if (!instant)
            {
                target.ActiveEffects.Add(effect);
                target.EffectTagMask |= def.Tags;
            }

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

            _dispelBuffer.Clear();
            List<RuntimeEffect> effects = target.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (MatchesDispel(effects[i].Def, in req)) _dispelBuffer.Add(effects[i]);
            }

            int removed = 0;
            for (int i = 0; i < _dispelBuffer.Count; i++)
            {
                if (req.MaxCount > 0 && removed >= req.MaxCount) break;
                Expire(target, _dispelBuffer[i], combat);
                removed++;
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
                        periodic.OnTick(MakeContext(unit, eff.Source, combat, eff, i, periodic.Interval));
                        if (unit.IsDead) return;
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
            RebuildTagMask(unit);

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

        private static void RebuildTagMask(RuntimeUnit unit)
        {
            EffectTag mask = EffectTag.None;
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++) mask |= effects[i].Def.Tags;
            unit.EffectTagMask = mask;
        }

        private static EffectContext MakeContext(
            RuntimeUnit target, RuntimeUnit source, ICombatContext combat, RuntimeEffect effect, int componentIndex, float dt)
        {
            float potency = effect.ScaledPotency != null && componentIndex < effect.ScaledPotency.Length
                ? effect.ScaledPotency[componentIndex]
                : 0f;
            return new EffectContext(target, source, combat, effect, potency, dt);
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
                    return;

                case StackRule.Stack:
                    stacksChanged = TryAddStack(existing, def);
                    break;

                case StackRule.Refresh:
                    RefreshDuration(existing, def, source, target);
                    break;

                case StackRule.StackAndRefresh:
                    stacksChanged = TryAddStack(existing, def);
                    RefreshDuration(existing, def, source, target);
                    break;
            }

            // Стак изменил число — переоценить stateful-вклад компонентов под новый Stacks.
            if (stacksChanged) Reapply(existing, previousStacks, target, combat);
        }

        private static bool TryAddStack(RuntimeEffect effect, EffectData def)
        {
            if (effect.Stacks >= def.MaxStacks) return false;
            effect.Stacks++;
            return true;
        }

        private static void RefreshDuration(RuntimeEffect effect, EffectData def, RuntimeUnit source, RuntimeUnit target)
        {
            int ticks = ResolveDurationTicks(def, source, target);
            effect.RemainingTicks    = ticks;
            effect.FullDurationTicks = ticks;
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
                EffectContext ctx = MakeContext(target, effect.Source, combat, effect, i, 0f);

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
