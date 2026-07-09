using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Вихревой заход» (§9.6, §10.6): реактив на завершение смещения
    /// (<see cref="CombatEvent.UnitDisplaced"/>, доставляется источнику толчка). В конце полёта
    /// монах телепортируется к смещённой цели и взводит усиление следующей авто-атаки (×2). Без
    /// кулдауна — комбо-механика. Направление «в спину» приближено (садимся вплотную со стороны монаха).
    /// </summary>
    [Serializable]
    public sealed class VortexEntryComponent : IReactiveComponent
    {
        [Tooltip("Множитель урона усиленной атаки после телепорта.")]
        [SerializeField] private float _empowerMult = 2f;

        public CombatEvent Events => CombatEvent.UnitDisplaced;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (e.Type != CombatEvent.UnitDisplaced) return;

            RuntimeUnit monk   = ctx.Target; // носитель пассива = источник толчка (carrier = Source)
            RuntimeUnit victim = e.Target;   // смещённая (только что приземлившаяся) цель
            if (monk == null || monk.IsDead || victim == null || victim.IsDead) return;

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
