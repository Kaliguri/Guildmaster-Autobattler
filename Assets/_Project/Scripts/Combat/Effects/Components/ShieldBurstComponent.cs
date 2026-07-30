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
    /// <para><b>Размер щита</b> берётся из <see cref="RuntimeEffect.HeldShield"/> — туда его пишет тот
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

        [Tooltip("Взрываться ТАКЖЕ по истечении срока щита («Водяной щит» Монаха воды: через 5 сек или при " +
                 "уничтожении). Выкл = только от пробития, как у «Собирателя костей».")]
        [SerializeField] private bool _burstOnExpire;

        [Tooltip("Считать урон от ОСТАВШЕЙСЯ прочности, а не от полного размера щита. Тогда взрыв от " +
                 "пробития урона не наносит вовсе — платой был удар, а не размер.")]
        [SerializeField] private bool _useRemainingShield;

        [Tooltip("На сколько взрыв отбрасывает задетых, мировых единиц. 0 = только урон.")]
        [SerializeField] private float _knockbackDistance;

        [Tooltip("Эффект на каждого задетого взрывом (замедление у «Водяного щита»). Пусто = без эффекта.")]
        [SerializeField] private EffectData _victimEffect;

        public CombatEvent Events => CombatEvent.DamageTaken;

        public void OnApply(in EffectContext ctx) { }

        /// <summary>
        /// Истечение щита. Взрыв здесь — не то же, что взрыв от пробития: щит дожил, значит в нём осталась
        /// прочность, и при <see cref="_useRemainingShield"/> именно она превращается в урон.
        /// </summary>
        /// <remarks>
        /// Система эффектов зовёт этот хук и на снятии диспелом, причину она не различает. Для щита это
        /// приемлемо и даже честно (снял чужой щит — получил волну в лицо), в отличие от отложенной бомбы,
        /// которую диспел не должен детонировать.
        /// </remarks>
        public void OnExpire(in EffectContext ctx)
        {
            if (!_burstOnExpire) return;
            Burst(in ctx);
        }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            RuntimeUnit carrier = ctx.Target;
            if (carrier == null || carrier.IsDead) return;

            // Взрыв только от ПРОБИТИЯ: щит ещё держит — ждём.
            if (carrier.CurrentShield > 0f) return;

            Burst(in ctx);
        }

        /// <summary>
        /// Разлёт: урон долей щита по всем врагам в радиусе, плюс — если заданы — толчок и эффект.
        /// Одно место на оба входа (пробитие и истечение), чтобы они не могли разойтись поведением.
        /// </summary>
        private void Burst(in EffectContext ctx)
        {
            RuntimeUnit carrier = ctx.Target;
            if (carrier == null || carrier.IsDead) return;

            // Размер щита пишет тот компонент, который его поднял (все щиты это делают). Читать его из
            // потенции нельзя: в реактивном пути снимка статов нет, и потенция приходит нулём.
            // Остаток берём ДО освобождения: после него в эффекте уже ноль.
            float remaining = carrier.CurrentShield;
            float size = ctx.Effect.ReleaseHeldShield();
            if (size <= 0f) return;

            // Остаток может быть больше того, что держал ЭТОТ щит (на носителе висит второй) — тогда
            // берём своё: взрыв платит за свою прочность, а не за чужую.
            float basis = _useRemainingShield ? Mathf.Min(size, remaining) : size;
            float damage = basis * _fractionOfShield;

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

                if (damage > 0f)
                    ctx.Combat.DealDamage(new DamageRequest(
                        carrier, victim, damage, _school, ctx.Combat.ArmorK,
                        affinity: carrier.Affinity));

                if (_victimEffect != null) ctx.Combat.ApplyEffect(victim, _victimEffect, carrier);

                if (_knockbackDistance > 0f)
                {
                    Vector2 away = victim.Position - carrier.Position;
                    if (away.sqrMagnitude < 1e-6f) continue; // стоит в центре — направления нет

                    // Не ядром: волна расталкивает, а не запускает телами друг в друга. Иначе один взрыв
                    // в свалке бил бы дважды каждым отброшенным.
                    ctx.Combat.Displace(new DisplaceRequest(
                        victim, carrier, away.normalized, _knockbackDistance,
                        cannonball: false, damage: 0f, school: _school, width: 0f));
                }
            }
        }
    }
}
