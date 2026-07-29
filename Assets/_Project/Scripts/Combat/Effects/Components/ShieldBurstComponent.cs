using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Взрыв щита: когда щит носителя ПРОБИТ в ноль, он разлетается вокруг, нанося урон долей от своего
    /// размера («Собиратель костей» Некроманта — 100% размера щита; «Водяной щит» — та же модель).
    /// <para><b>Числа:</b> <c>_fractionOfShield</c> — доля РАЗМЕРА щита, уходящая в урон (1 = весь щит);
    /// <c>_radius</c> — радиус вокруг носителя. Своего числа урона у взрыва нет намеренно (пункт M17):
    /// он целиком функция того, сколько щита успели поднять, — иначе появился бы второй владелец силы
    /// кита, и щит с взрывом пришлось бы балансировать двумя ручками вместо одной.</para>
    /// <para><b>Размер щита</b> берётся из <see cref="RuntimeEffect.PendingShield"/> — туда его пишет тот
    /// компонент, который щит поднял (это делают все щиты). Своего числа взрыв не заводит — иначе у силы
    /// кита стало бы два владельца. Берётся ПОЛНЫЙ размер, а не остаток: взрыв платит за щит, который
    /// враг был вынужден пробить.</para>
    /// <para><b>Когда срабатывает:</b> на уроне по носителю, если после него щита не осталось. Один раз:
    /// размер обнуляется вместе со взрывом, поэтому повторный урон в том же тике не даёт второго взрыва.
    /// Щит, доживший до истечения эффекта, НЕ взрывается — платой был удар, а не время.</para>
    /// <para><b>Готча:</b> пул щита общий. Если на носителе висит второй щит, «пробит в ноль» наступит
    /// только когда кончатся оба, и взрыв случится позже, чем кажется по этому эффекту.</para>
    /// </summary>
    [Serializable]
    public sealed class ShieldBurstComponent : IReactiveComponent
    {
        [Tooltip("Доля размера щита, уходящая в урон взрыва (1 = 100%, как у «Собирателя костей»).")]
        [Range(0f, 2f)]
        [SerializeField] private float _fractionOfShield = 1f;

        [Tooltip("Радиус взрыва вокруг носителя, мировые единицы.")]
        [SerializeField] private float _radius = 2f;

        [Tooltip("Школа урона взрыва. Физика по умолчанию: подтип наследуется от носителя.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Physical;

        public CombatEvent Events => CombatEvent.DamageTaken;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            RuntimeUnit carrier = ctx.Target;
            if (carrier == null || carrier.IsDead) return;

            // Взрыв только от ПРОБИТИЯ: щит ещё держит — ждём.
            if (carrier.CurrentShield > 0f) return;

            // Размер щита пишет тот компонент, который его поднял (все щиты это делают). Читать его из
            // потенции нельзя: в реактивном пути снимка статов нет, и потенция приходит нулём.
            float size = ctx.Effect.PendingShield;
            if (size <= 0f) return;

            float damage = size * _fractionOfShield;
            ctx.Effect.PendingShield = 0f;   // один взрыв на один щит
            if (damage <= 0f) return;

            // Список локальный, а не переиспользуемый: взрыв наносит урон, урон будит реактивы, среди
            // них может оказаться ЭТОТ ЖЕ компонент на другом носителе — общий буфер перезаписался бы
            // прямо посреди обхода. Взрыв — событие редкое, аллокация тут дешевле такого дефекта.
            var targets = new System.Collections.Generic.List<RuntimeUnit>();

            ctx.Combat.ReportAreaHit(AreaHit.Circle(carrier.Position, _radius, carrier.Team));
            ctx.Combat.QueryUnitsInRadius(
                carrier.Position, _radius, targets, TargetFilter.Enemies, carrier.Team);

            for (int i = 0; i < targets.Count; i++)
            {
                RuntimeUnit victim = targets[i];
                if (victim.IsDead) continue;

                ctx.Combat.DealDamage(new DamageRequest(
                    carrier, victim, damage, _school, ctx.Combat.ArmorK,
                    affinity: carrier.Affinity));
            }
        }
    }
}
