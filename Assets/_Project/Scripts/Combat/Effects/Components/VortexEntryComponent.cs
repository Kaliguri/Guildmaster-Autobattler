using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Вихревой заход» (§9.6, §10.6): реактив на конец эффекта смещения
    /// (<see cref="CombatEvent.EffectExpired"/> с тегом <see cref="EffectTag.KnockUp"/>, доставляется
    /// источнику смещения). Срабатывает, когда закончилось отбрасывание ВРАГА (по команде смещённого) —
    /// монах телепортируется ему в спину и взводит усиление следующей авто-атаки (×2). Конец собственного
    /// рывка монаха (смещённый = свой) сюда НЕ проходит (та же ветка обрабатывается приземлением рывка).
    /// Без кулдауна — комбо-механика. Направление «в спину» приближено (садимся вплотную со стороны монаха).
    /// </summary>
    [Serializable]
    public sealed class VortexEntryComponent : IReactiveComponent
    {
        [Tooltip("Множитель урона усиленной атаки после телепорта.")]
        [SerializeField] private float _empowerMult = 2f;

        public CombatEvent Events => CombatEvent.EffectExpired;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Только конец смещения (KnockUp). Прочие истёкшие эффекты — не наше дело.
            if (e.Type != CombatEvent.EffectExpired || (e.Tags & EffectTag.KnockUp) == 0) return;

            RuntimeUnit monk   = ctx.Target; // носитель пассива = источник смещения (carrier = Source)
            RuntimeUnit victim = e.Target;   // юнит, на котором закончилось смещение
            if (monk == null || monk.IsDead || victim == null || victim.IsDead) return;

            // Телепорт — только к ВРАГУ (конец отбрасывания). Конец собственного рывка (victim == monk,
            // своя команда) отсекаем — иначе монах «телепортнулся бы к себе» на приземлении рывка.
            if (victim.Team == monk.Team) return;

            // Телепорт вплотную к цели со стороны монаха (приближение «в спину»); фокус + усиление.
            Vector2 toMonk = monk.Position - victim.Position;
            Vector2 dir = toMonk.sqrMagnitude > 1e-4f ? toMonk.normalized : Vector2.right;
            float range = monk.Stats.Get(StatType.AttackRange);

            monk.PreviousPosition = monk.Position;
            monk.Position = victim.Position + dir * (range * 0.5f);
            monk.CurrentTarget = victim;
            monk.EmpowerDamageMult = _empowerMult;
        }
    }
}
