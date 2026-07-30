using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Изморозь» — холодная линия целиком (карточка [[frost]]). Стак-эффект на цели: сами стаки урона не
    /// наносят, они **открывают ступени** и **задают силу** уже открытых состояний.
    /// <list type="bullet">
    /// <item><b>Замёрз</b> (1–9): замедление передвижения, растёт непрерывно до 19-го стака.</item>
    /// <item><b>Примёрз</b> (10–19): обездвиживание раз в <see cref="_rootPeriodSeconds"/> сек и потеря
    /// скорости атаки; замедление при этом продолжает расти — оно не обрывается на границе ступени.</item>
    /// <item><b>Обратился в лёд</b> (20): статуя — оглушение и хрупкость к дробящему; по её окончании
    /// «Изморозь» обнуляется.</item>
    /// </list>
    /// <para><b>Числа:</b> границы интерполяции заданы парами «на первом стаке / на девятнадцатом»
    /// (<c>_slow*</c>, <c>_rootSeconds*</c>, <c>_attackSpeedLoss*</c>) — линейно между ними, как велит
    /// карточка: так числа сохраняются буквально на концах шкалы. <c>_iceVulnMid</c> — прибавка к урону
    /// льдом со второй ступени. Сход стаков — <c>_graceSeconds</c> без подпитки, затем по одному с
    /// ускорением (<c>_firstDecaySeconds</c> × <c>_decayFalloff</c>, но не быстрее <c>_minDecaySeconds</c>);
    /// огонь на цели ускоряет сход в <c>_fireDecayMult</c> раз.</para>
    /// <para><b>Когда срабатывает:</b> периодически (сход, обездвиживание, вход в статую) и в pre-damage
    /// (уязвимость к льду). Хрупкость к дробящему живёт на самой статуе.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему один компонент, а не три по ступеням.</b> Ступень — не отдельное состояние, а ворота:
    /// «что включено» решают пороги, «насколько сильно» — то же самое число стаков. Разложив это по трём
    /// эффектам, мы получили бы три владельца одного счётчика и обязанность синхронизировать их сход.
    /// <para><b>Силу держат стат-модификаторы по ключу эффекта</b> (<c>AddModifiersFrom</c>): при каждой
    /// смене стаков вклад переписывается целиком, поэтому непрерывная кривая не требует ни своего
    /// состояния, ни дробления на ассеты.</para>
    /// <para><b>Обездвиживание считается от тика наложения</b>, а не от входа на ступень: у компонента нет
    /// места под второй таймер (таймер эффекта занят сходом), а привязка к <c>AppliedTick</c>
    /// детерминирована и одинакова у отражённых сторон. Цена: войдя на вторую ступень, цель ждёт до
    /// ближайшей отметки периода, а не рутится немедленно.</para>
    /// <para><b>Статуя обнуляет «Изморозь» не сама</b> — это делает эффект статуи в своём
    /// <c>OnExpire</c> (<see cref="FrostStatueComponent"/>). Иначе обнуление пришлось бы ждать здесь,
    /// зная чужую длительность.</para>
    /// </remarks>
    [Serializable]
    public sealed class FrostComponent : IStackableComponent, IPeriodicComponent, IPreDamageComponent
    {
        [Header("Ступени")]
        [Tooltip("Со какого стака включается обездвиживание (вторая ступень). Карточка: 10.")]
        [SerializeField] private int _rootThreshold = 10;

        [Tooltip("Стак, на котором цель обращается в лёд (кап «Изморози»). Карточка: 20.")]
        [SerializeField] private int _statueThreshold = 20;

        [Header("Замедление (ступень 1, растёт до 19 стаков)")]
        [Tooltip("Замедление передвижения на ПЕРВОМ стаке (0.2 = −20%).")]
        [SerializeField] private float _slowAtFirstStack = 0.2f;

        [Tooltip("Замедление передвижения на ДЕВЯТНАДЦАТОМ стаке (0.6 = −60%).")]
        [SerializeField] private float _slowAtLastStack = 0.6f;

        [Header("Обездвиживание (ступень 2)")]
        [Tooltip("Как часто цель обездвиживается, сек. Карточка: 4.")]
        [SerializeField] private float _rootPeriodSeconds = 4f;

        [Tooltip("Длительность обездвиживания на 10-м стаке, сек. Карточка: 0.5.")]
        [SerializeField] private float _rootSecondsAtThreshold = 0.5f;

        [Tooltip("Длительность обездвиживания на 19-м стаке, сек. Карточка: 1.5.")]
        [SerializeField] private float _rootSecondsAtLastStack = 1.5f;

        [Tooltip("Эффект обездвиживания (срок задаётся при наложении, поэтому в ассете он опорный).")]
        [SerializeField] private EffectData _rootEffect;

        [Tooltip("Потеря скорости атаки на 10-м стаке (0.2 = −20%).")]
        [SerializeField] private float _attackSpeedLossAtThreshold = 0.2f;

        [Tooltip("Потеря скорости атаки на 19-м стаке (0.6 = −60%).")]
        [SerializeField] private float _attackSpeedLossAtLastStack = 0.6f;

        [Tooltip("Прибавка к получаемому урону льдом со второй ступени (0.1 = +10%).")]
        [SerializeField] private float _iceVulnMid = 0.1f;

        [Header("Статуя (ступень 3)")]
        [Tooltip("Эффект статуи: оглушение, хрупкость к дробящему и обнуление «Изморози» по истечении.")]
        [SerializeField] private EffectData _statueEffect;

        [Header("Сход стаков")]
        [Tooltip("Сколько стаки держатся без подпитки, сек. Карточка: 5.")]
        [SerializeField] private float _graceSeconds = 5f;

        [Tooltip("Через сколько сходит ПЕРВЫЙ стак после grace, сек.")]
        [SerializeField] private float _firstDecaySeconds = 1f;

        [Tooltip("Во сколько раз укорачивается интервал схода каждый раз (0.75 = на четверть быстрее).")]
        [SerializeField] private float _decayFalloff = 0.75f;

        [Tooltip("Быстрее этого стаки не сходят, сек.")]
        [SerializeField] private float _minDecaySeconds = 0.25f;

        [Tooltip("Во сколько раз быстрее сходит «Изморозь», пока на цели горит огонь. Карточка: 2. " +
                 "Огонь её НЕ сбрасывает: сброс сделал бы одного огненного врага полным ластиком крио-кита.")]
        [SerializeField] private float _fireDecayMult = 2f;

        public float Interval => 1f / SimConstants.TickRate;   // проверяем состояние каждый тик

        public void OnApply(in EffectContext ctx)
        {
            ArmDecay(in ctx);
            ApplyStatModifiers(in ctx);
        }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);
        }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Кривая непрерывная, поэтому вклад переписывается целиком на каждый стак, а не дельтой:
            // замедление на 12 стаках — не «сумма двенадцати шагов», а точка на прямой.
            ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);
            ApplyStatModifiers(in ctx);
            ArmDecay(in ctx);   // подпитка сдвигает grace: стаки живут от ПОСЛЕДНЕГО попадания
        }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit target = ctx.Target;
            if (target == null || target.IsDead) return;

            int stacks = ctx.Effect.Stacks;

            // Кап достигнут — цель обращается в лёд. Статуя сама снимет «Изморозь», когда отпустит.
            if (stacks >= _statueThreshold)
            {
                if (_statueEffect != null && !HasEffect(target, _statueEffect))
                    ctx.Combat.ApplyEffect(target, _statueEffect, ctx.Source);
                return;   // пока цель статуя, стаки не сходят: окно принадлежит атакующему
            }

            if (stacks >= _rootThreshold) TickRoot(in ctx, stacks);

            TickDecay(in ctx);
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;
            if (incoming.School != DamageSchool.Magical || incoming.Element != MagicElement.Ice) return;

            // Со второй ступени промороженная цель хуже держит сам лёд. Верхнюю ступень (и её +40%
            // под оглушением) считает статуя — там же, где живёт её оглушение.
            if (ctx.Effect.Stacks >= _rootThreshold && _iceVulnMid > 0f)
                result.AddMultiplier(1f + _iceVulnMid);
        }

        // --- Ступени ---

        /// <summary>
        /// Доля пути от первого стака к девятнадцатому: 0 на 1 стаке, 1 на <see cref="_statueThreshold"/>−1.
        /// Шкала сквозная и НЕ обрывается на границе ступени — так велит карточка (замедление растёт всю
        /// дорогу, даже когда сверху уже включился рут).
        /// </summary>
        private float Ramp(int stacks)
        {
            int last = Mathf.Max(2, _statueThreshold - 1);
            return Mathf.Clamp01((stacks - 1) / (float)(last - 1));
        }

        private void ApplyStatModifiers(in EffectContext ctx)
        {
            Stats stats = ctx.Target?.Stats;
            if (stats == null) return;

            int stacks = ctx.Stacks;
            float ramp = Ramp(stacks);

            float slow = Mathf.Lerp(_slowAtFirstStack, _slowAtLastStack, ramp);
            bool rooted = stacks >= _rootThreshold;

            // Потеря скорости атаки принадлежит ВТОРОЙ ступени: до неё цель бьёт в своём темпе, просто
            // медленно ходит. Поэтому список модификаторов разной длины, а не нулевая прибавка.
            StatModifier[] mods = rooted
                ? new[]
                {
                    new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -slow),
                    new StatModifier(StatType.AttackSpeed, ModifierOp.PercentMult,
                        -Mathf.Lerp(_attackSpeedLossAtThreshold, _attackSpeedLossAtLastStack, ramp)),
                }
                : new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -slow) };

            stats.AddModifiersFrom(ctx.Effect, mods, deferred: true);
        }

        /// <summary>
        /// Обездвиживание второй ступени: раз в период, отсчитываемый от тика наложения «Изморози».
        /// Длительность растёт со стаками, поэтому едет параметром наложения, а не полем ассета.
        /// </summary>
        private void TickRoot(in EffectContext ctx, int stacks)
        {
            if (_rootEffect == null || _rootPeriodSeconds <= 0f) return;

            int periodTicks = Mathf.Max(1, Mathf.RoundToInt(_rootPeriodSeconds * SimConstants.TickRate));
            int elapsed = ctx.Combat.CurrentTick - ctx.Effect.AppliedTick;
            if (elapsed <= 0 || elapsed % periodTicks != 0) return;

            float seconds = Mathf.Lerp(_rootSecondsAtThreshold, _rootSecondsAtLastStack, Ramp(stacks));
            ctx.Combat.ApplyEffect(ctx.Target, _rootEffect, ctx.Source, seconds);
        }

        // --- Сход стаков ---

        /// <summary>Взвести grace: стаки держатся, пока цель греется свежими попаданиями.</summary>
        private void ArmDecay(in EffectContext ctx)
        {
            int graceTicks = Mathf.Max(1, Mathf.RoundToInt(_graceSeconds * SimConstants.TickRate));
            int firstTicks = Mathf.Max(1, Mathf.RoundToInt(_firstDecaySeconds * SimConstants.TickRate));
            ctx.Effect.ScheduleTimer(ctx.Combat.CurrentTick + graceTicks, firstTicks);
        }

        private void TickDecay(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            int now = ctx.Combat.CurrentTick;
            if (!eff.IsTimerDue(now)) return;

            eff.RemoveStacks(1);
            if (eff.Stacks <= 0)
            {
                eff.EndDuration();
                return;
            }

            // Огонь на цели ускоряет сход, но не сбрасывает: контрслой обоюдный и одинаковый по силе с
            // тем, как «Мокрый» гасит «Угли».
            float falloff = _decayFalloff;
            int minTicks = Mathf.Max(1, Mathf.RoundToInt(_minDecaySeconds * SimConstants.TickRate));
            int next = Mathf.RoundToInt(eff.TimerIntervalTicks * falloff);

            bool burning = (ctx.Target.EffectTagMask & (EffectTag.Burn | EffectTag.Ember)) != 0;
            if (burning && _fireDecayMult > 1f) next = Mathf.RoundToInt(next / _fireDecayMult);

            eff.RescheduleTimer(now, Mathf.Max(minTicks, next));
        }

        private static bool HasEffect(RuntimeUnit unit, EffectData def)
        {
            var effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
                if (ReferenceEquals(effects[i].Def, def)) return true;

            return false;
        }
    }
}
