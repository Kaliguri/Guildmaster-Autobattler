using System;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Щит-половинщик против ДАЛЬНЕГО боя</b> («Встречный ветер» мага ветра,
    /// [[the-zephyr]]): каждый дальний удар по носителю режется на заданную долю, а срезанное
    /// списывается с запаса потока. Кончился запас — эффект снимается сам, не дожидаясь срока.
    /// <para><b>Числа:</b> <c>_amount</c> — запас потока в единицах урона, скейлится статами источника;
    /// <c>_cutShare</c> — какую долю дальнего удара срезать (0.5 = половину). Ближний бой поток не
    /// трогает вовсе.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, на прямом ударе от дальнего юнита.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему щит именно тратится, а не просто висит срок.</b> Формулировка Макса была «щит,
    /// уменьшающий урон дальнего боя на 50%… когда этот щит сломан — эффект пропадает»
    /// (`2026-08-21/4`). Редукция со сроком ломаться не умеет — ей нечем; поэтому поток съедает ровно
    /// то, что не долетело, и этим же исчерпывается. Отвергнут вариант «пул отдельно, аура отдельно»:
    /// два счётчика на одну фантазию, и непонятно, чем тратится пул.
    /// <para><b>Дальность читается по АТАКУЮЩЕМУ</b> (<see cref="UnitData.AttackType"/>), а не по удару:
    /// в <see cref="DamageRequest"/> признака дальности нет вовсе. Для авто-атак это точно, для
    /// способности ближнего юнита, дотянувшейся издалека, — приближение, и оно намеренное: заводить
    /// дальность в каждом источнике урона дороже, чем спросить того, кто бьёт.</para>
    /// <para><b>Запас — per-effect</b> (<c>RuntimeEffect.HeldShield</c>), как у
    /// <see cref="SchoolShieldComponent"/>: два потока на одном бойце держат каждый своё.</para>
    /// </remarks>
    [Serializable]
    public sealed class RangedWardComponent : IPreDamageComponent, IStackableComponent, IScalablePotency
    {
        [Tooltip("Запас потока в единицах урона: сколько СРЕЗАННОГО он способен впитать. Скейлится статами источника.")]
        [SerializeField] private ScalableValue _amount;

        [Tooltip("Какую долю дальнего удара срезать: 0.5 = половину.")]
        [Range(0f, 1f)]
        [SerializeField] private float _cutShare = 0.5f;

        public ScalableValue Potency => _amount;

        /// <summary>Поглощение: съесть долетевшее — после всех, кто мог удар отменить.</summary>
        public int Priority => ReactionPriority.Absorb;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Effect.HoldShield(ctx.Potency * ctx.Stacks);
        }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Effect.ReleaseHeldShield();
        }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Только прибавка новых стаков: уже поглощённую часть пула рестак не возвращает.
            float delta = ctx.Potency * (ctx.Stacks - previousStacks);
            if (delta > 0f) ctx.Effect.AddHeldShield(delta);
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;
            if (!incoming.IsDirectHit) return;   // тики DoT и ответка шипов ветром не сбиваются
            if (!IsRanged(incoming.Source)) return;
            if (_cutShare <= 0f) return;

            float pool = ctx.Effect.HeldShield;
            if (pool <= 0f) return;

            float damage = incoming.RawDamage;
            if (damage <= 0f) return;

            // Срезаем долю, но не больше, чем осталось в запасе: последний удар поток гасит частично.
            float cut = damage * _cutShare;
            if (cut > pool) cut = pool;

            ctx.Effect.HoldShield(pool - cut);

            RuntimeUnit carrier = ctx.Target;
            if (carrier != null) carrier.AbsorbedByWard += cut;

            result.AddMultiplier(1f - cut / damage);

            // Запас исчерпан — поток «сломан» и уходит сразу, а не досиживает срок. Снятие изнутри
            // pre-damage безопасно: EffectSystem собирает реакции заранее и на каждом шаге проверяет,
            // что эффект ещё на носителе.
            if (ctx.Effect.HeldShield <= 0f && carrier != null)
                ctx.Combat.RemoveEffect(carrier, ctx.Effect.Def);
        }

        /// <summary>Бьёт ли этот юнит с дистанции. Источника нет (эффект арены, DoT) — считаем ближним.</summary>
        private static bool IsRanged(RuntimeUnit attacker)
            => attacker?.Unit != null && attacker.Unit.AttackType == AttackType.Ranged;
    }
}
