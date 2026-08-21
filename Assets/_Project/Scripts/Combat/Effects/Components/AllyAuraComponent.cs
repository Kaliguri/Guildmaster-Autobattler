using System;
using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Аура-раздатчик:</b> пока носитель жив, держит заданный эффект на живых союзниках в радиусе.
    /// Первый носитель — «Порыв» мага ветра ([[the-zephyr]]): сама реакция живёт на союзнике, и повесить
    /// её на него должен кто-то ещё.
    /// <para><b>Числа:</b> <c>_effect</c> — что раздавать; <c>_radius</c> — кому (0 = всей своей стороне);
    /// <c>_includeSelf</c> — считать ли носителя своим же союзником.</para>
    /// <para><b>Когда срабатывает:</b> раз в <see cref="Interval"/> секунду. Погиб носитель — эффект
    /// уходит, а с ним и раздача: висящие копии досидят свой срок и сойдут.</para>
    /// </summary>
    /// <remarks>
    /// <b>Уже висящий эффект аура НЕ ТРОГАЕТ — это главное в ней.</b> Наложение поверх взводит заряды
    /// заново (<c>ArmCharges</c> в <c>OnApply</c> компонента), поэтому раздача раз в секунду обнулила бы
    /// любую перезарядку: «Порыв» с кулдауном в восемь секунд срабатывал бы на каждый удар. Проверка
    /// «уже есть» и есть цена этой ауры, и снимать её нельзя.
    /// <para><b>Почему аура, а не наложение на старте боя:</b> состав рядом меняется — союзники ходят,
    /// умирают, призываются. Разовая раздача обошла бы призванных и не сняла бы эффект с тех, кто ушёл
    /// из радиуса, а значит радиус перестал бы что-либо значить.</para>
    /// <para><b>Срок копии — свой у эффекта.</b> Аура его не продлевает и не укорачивает: истёкшую копию
    /// она просто выдаст заново на следующем тике, если союзник всё ещё в радиусе.</para>
    /// </remarks>
    [Serializable]
    public sealed class AllyAuraComponent : IPeriodicComponent
    {
        private static readonly List<RuntimeUnit> Allies = new List<RuntimeUnit>(16);

        [Tooltip("Какой эффект держать на союзниках.")]
        [SerializeField] private EffectData _effect;

        [Tooltip("Радиус раздачи, мировых единиц. 0 = вся своя сторона, без ограничения по дистанции.")]
        [Min(0f)]
        [SerializeField] private float _radius = 4f;

        [Tooltip("Раздавать ли эффект и самому носителю.")]
        [SerializeField] private bool _includeSelf;

        [Tooltip("Как часто аура проверяет, кому чего не хватает, сек.")]
        [Min(0.1f)]
        [SerializeField] private float _interval = 1f;

        public float Interval => _interval;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            if (_effect == null) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            float searchRadius = _radius > 0f ? _radius : ctx.Combat.Tuning.GlobalSearchRadius;
            ctx.Combat.QueryUnitsInRadius(self.Position, searchRadius, Allies, TargetFilter.Allies, self.Team);

            for (int i = 0; i < Allies.Count; i++)
            {
                RuntimeUnit ally = Allies[i];
                if (ally == null || ally.IsDead) continue;
                if (!_includeSelf && ReferenceEquals(ally, self)) continue;
                if (HasEffect(ally, _effect)) continue;   // не перевзводим чужие заряды

                ctx.Combat.ApplyEffect(ally, _effect, self);
            }
        }

        /// <summary>Висит ли уже эта копия на юните (сравниваем по определению, а не по тегу).</summary>
        private static bool HasEffect(RuntimeUnit unit, EffectData def)
        {
            var active = unit.ActiveEffects;
            for (int i = 0; i < active.Count; i++)
                if (ReferenceEquals(active[i].Def, def)) return true;
            return false;
        }
    }
}
