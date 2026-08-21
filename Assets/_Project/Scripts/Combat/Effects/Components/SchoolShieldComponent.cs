using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Щит С ФИЛЬТРОМ ПО ШКОЛЕ: поглощает только урон своей школы (и, если задана, стихии), ведёт свой
    /// запас и копит поглощённое в <see cref="RuntimeUnit.AbsorbedByWard"/>. «Отражающий налёт» Антимага
    /// (карточка [[the-aegis]]) выдаёт такой щит за каждое вражеское заклинание.
    /// <para><b>Числа:</b> <c>_amount</c> — запас поглощения, скейлится статами источника; <c>_school</c>
    /// и <c>_wholeSchool</c> — что именно он держит (флаг = вся школа этого типа). Длительность и стакинг
    /// решает сам эффект: короткий щит гасит один залп, длинный копит.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, до брони — как и любая стойкость самой цели.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему не общий пул <c>CurrentShield</c>:</b> тот один на юнита и школы не различает — щит от
    /// магии съедал бы физические удары. Заводить второй и третий пул в <c>RuntimeUnit</c> тоже отвергнуто:
    /// это плодит поля под каждую будущую школу, а пайплайн урона пришлось бы учить порядку их трат.
    /// <para><b>Как поглощение выражается через pre-damage:</b> хватает запаса — удар отменяется целиком
    /// (<c>Negated</c>), не хватает — множителем срезается ровно поглощённая доля. Арифметически это тот же
    /// вычет, но без нового пула в ядре. Цена названа: удар, съеденный таким щитом полностью, для реактивов
    /// «на удар» выглядит отменённым (шипы о него не колются) — как и у любого негейта.</para>
    /// <para><b>Запас — per-effect</b> (<c>RuntimeEffect.HeldShield</c>), поэтому два налёта держат каждый
    /// своё: копятся щиты, а не одно число, и «Перегрузка» считает по поглощённому, а не по остатку.</para>
    /// </remarks>
    [Serializable]
    public sealed class SchoolShieldComponent : IPreDamageComponent, IStackableComponent, IScalablePotency
    {
        [Tooltip("Запас поглощения, в единицах урона. Скейлится статами источника.")]
        [SerializeField] private ScalableValue _amount;

        [Tooltip("Тип урона, который щит держит.")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        [Tooltip("Держать всю школу этого типа, а не только сам тип: Аркановый щит гасит любую магию, " +
                 "Огненный вард — только Огонь.")]
        [SerializeField] private bool _wholeSchool = true;

        public ScalableValue Potency => _amount;

        public void OnApply(in EffectContext ctx)
        {
            // Запас держит сам эффект: это его личный пул, а не вклад в общий щит носителя.
            ctx.Effect.HoldShield(ctx.Potency * ctx.Stacks);
        }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Effect.ReleaseHeldShield();   // недоеденный запас просто уходит вместе с эффектом
        }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Только прибавка новых стаков: уже поглощённую часть пула рестак не возвращает.
            float delta = ctx.Potency * (ctx.Stacks - previousStacks);
            if (delta > 0f) ctx.Effect.AddHeldShield(delta);
        }

        /// <summary>Щит съедает то, что долетело, — после всех, кто мог удар отменить.</summary>

        public int Priority => ReactionPriority.Absorb;


        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;
            if (!DamageTypes.Matches(_damageType, _wholeSchool, incoming.Type)) return;

            float pool = ctx.Effect.HeldShield;
            if (pool <= 0f) return;

            float damage = incoming.RawDamage;
            if (damage <= 0f) return;

            RuntimeUnit carrier = ctx.Target;

            if (pool >= damage)
            {
                // Съели удар целиком: запас уменьшаем на его величину, урон отменяем.
                ctx.Effect.HoldShield(pool - damage);
                if (carrier != null) carrier.AbsorbedByWard += damage;
                result.Negated = true;
                return;
            }

            // Запаса не хватило: гасим ровно ту долю, что покрыл щит, и пул опустошаем.
            ctx.Effect.HoldShield(0f);
            if (carrier != null) carrier.AbsorbedByWard += pool;
            result.AddMultiplier(1f - pool / damage);
        }
    }
}
