using System;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>«Порыв»</b> (пассивка мага ветра, [[the-zephyr]]): когда по носителю проходит прямой удар,
    /// автор эффекта выдёргивает его <b>до попадания</b> — удар не наносит ничего, а носителя уносит
    /// в сторону. Следом на него ложится <see cref="_afterEffect"/> (Уклонение).
    /// <para><b>Направление:</b> от ближней атаки — прочь от бьющего; от снаряда — вбок, под прямым
    /// углом к линии «стрелок → цель». Первое выводит из зоны контакта, второе — из линии огня.</para>
    /// <para><b>Числа:</b> <c>_radius</c> — насколько далеко автор может дотянуться (проверяется в момент
    /// удара, а не при наложении); <c>_distance</c> и <c>_speedPerSecond</c> — куда и как быстро уносит;
    /// <c>_cooldownSeconds</c> — перезарядка реакции; <c>_afterEffect</c> — что ложится следом.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, на прямом ударе, если автор жив, в радиусе и
    /// дееспособен.</para>
    /// </summary>
    /// <remarks>
    /// <b>Реакция бьёт ДО удара — это решение, а не удобство.</b> Скрайб предлагал ставить её после
    /// урона, чтобы кит не превращался в стену негейтов поверх собственного щита; отвергнуто словом
    /// Макса: «Реакция - именно ДО удара. В этом и суть» (`2026-08-21/4`). Выдернуть союзника из-под
    /// удара и есть фантазия ветра, после удара она читалась бы как утешение. Цена вынесена в
    /// перезарядку, а не в момент срабатывания.
    /// <para><b>Дееспособность спрашивается у АВТОРА, а не у носителя</b> — поэтому
    /// <see cref="IRequiresAgencyComponent"/> здесь не годится: он смотрит на того, на ком висит эффект.
    /// Двигают союзника, а машет руками маг: оглушённый маг не дёргает, но оглушённого союзника
    /// выдернуть можно — его согласия никто не спрашивает.</para>
    /// <para><b>Заряд живёт per-effect, то есть у каждого союзника свой.</b> Общий счётчик на авторе
    /// пришлось бы держать состоянием вне эффекта, а эффектная модель такого места не даёт. Следствие
    /// названо честно: четверо под «Порывом» дают вчетверо больше срабатываний, чем одна перезарядка —
    /// число ждёт приёмки в open-forks.</para>
    /// <para><b>Направление снаряда берётся как линия «источник → цель»:</b> вектора полёта в
    /// <see cref="DamageRequest"/> нет, а снаряд летит по прямой от стрелка, так что приближение точное
    /// для авто-атак и грубеет только на кривых траекториях.</para>
    /// </remarks>
    [Serializable]
    public sealed class AllyGustComponent : IPreDamageComponent
    {
        [Tooltip("Радиус, в котором автор эффекта дотягивается до носителя. Проверяется в момент удара.")]
        [Min(0f)]
        [SerializeField] private float _radius = 4f;

        [Tooltip("На сколько уносит носителя, мировых единиц. 1.6 = дистанция контакта ближнего боя.")]
        [Min(0f)]
        [SerializeField] private float _distance = 1.6f;

        [Tooltip("Скорость рывка, ед/сек: с дистанцией задаёт его длительность. 0 = общий дефолт смещения.")]
        [Min(0f)]
        [SerializeField] private float _speedPerSecond = 14f;

        [Tooltip("Перезарядка реакции, сек.")]
        [Min(0f)]
        [SerializeField] private float _cooldownSeconds = 8f;

        [Tooltip("Что ложится на носителя после рывка (Уклонение). Не задан — только рывок.")]
        [SerializeField] private EffectData _afterEffect;

        /// <summary>Уход: носитель покидает точку удара — раньше щитов, чтобы те не тратили запас впустую.</summary>
        public int Priority => ReactionPriority.Evade;

        public void OnApply(in EffectContext ctx)
        {
            // Перезарядка выражена ОДНИМ зарядом: «Порыв» не копится впрок, он просто бывает готов или
            // нет. Заряд стартует готовым, поэтому первый же удар по союзнику ветер перехватывает.
            ctx.Effect.ArmCharges(1);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;
            if (!incoming.IsDirectHit) return;   // из-под яда ветром не выдернешь

            RuntimeUnit carrier = ctx.Target;
            RuntimeUnit author = ctx.Source;
            if (carrier == null || carrier.IsDead) return;
            if (author == null || author.IsDead) return;
            if (ReferenceEquals(author, carrier)) return;   // себя за волосы не тянут

            // Дееспособность и дистанция — у автора: это ОН машет руками, и ему до союзника дотягиваться.
            if (!author.CanActAtTickStart) return;
            if (_radius > 0f && (carrier.Position - author.Position).sqrMagnitude > _radius * _radius) return;

            Vector2 direction = PushDirection(carrier, in incoming);
            if (direction.sqrMagnitude <= 0f) return;

            int cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(_cooldownSeconds * SimConstants.TickRate));
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, cooldownTicks)) return;

            // Сначала уносим, потом гасим урон: удар приходит в точку, из которой цель уже ушла.
            ctx.Combat.Displace(new DisplaceRequest(
                carrier, author, direction, _distance,
                cannonball: false, damage: 0f, damageType: DamageType.Undefined, width: 0f,
                speedPerSecond: _speedPerSecond));

            if (_afterEffect != null) ctx.Combat.ApplyEffect(carrier, _afterEffect, author);

            result.Negated = true;
        }

        /// <summary>
        /// Куда толкать: от ближнего бьющего — прочь по линии от него, от дальнего — вбок, под прямым
        /// углом к линии полёта. Источника нет — толкать не от чего.
        /// </summary>
        private static Vector2 PushDirection(RuntimeUnit carrier, in DamageRequest incoming)
        {
            RuntimeUnit attacker = incoming.Source;
            if (attacker == null) return Vector2.zero;

            Vector2 fromAttacker = carrier.Position - attacker.Position;
            if (fromAttacker.sqrMagnitude <= 0f) return Vector2.zero;

            fromAttacker = fromAttacker.normalized;

            bool ranged = attacker.Unit != null && attacker.Unit.AttackType == AttackType.Ranged;
            if (!ranged) return fromAttacker;

            // Перпендикуляр к линии полёта. Сторону выбираем детерминированно — ту, куда носитель уже
            // движется; стоит на месте — левую по конвенции. Бросок монеты здесь означал бы выходной
            // рандом, которого в бою нет. Направление берём из смещения за прошлый тик: своего поля
            // скорости у юнита нет, позиции достаточно.
            Vector2 side = new Vector2(-fromAttacker.y, fromAttacker.x);
            Vector2 intent = carrier.Position - carrier.PreviousPosition;
            if (intent.sqrMagnitude > 0f && Vector2.Dot(side, intent) < 0f) side = -side;
            return side;
        }
    }
}
