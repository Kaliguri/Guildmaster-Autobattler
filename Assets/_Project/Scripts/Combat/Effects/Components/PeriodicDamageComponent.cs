using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;
using UnityEngine.Serialization;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Периодический урон (DoT). Потенция задаётся как <b>урон в секунду</b> (per-second rate) и
    /// масштабируется статами источника через <see cref="ScalableValue"/>; за один тик применяется
    /// <c>Potency × Interval × Stacks</c>. Total масштабируется числом тиков (длительностью), а не
    /// запекается — вики «11» §5.1.
    /// </summary>
    [Serializable]
    public sealed class PeriodicDamageComponent : IPeriodicComponent, IScalablePotency
    {
        [Tooltip("Интервал между тиками, сек.")]
        [SerializeField] private float _interval = 1f;

        [Tooltip("Урон В СЕКУНДУ (per-second rate). Скейлится статами источника.")]
        [SerializeField] private ScalableValue _damagePerSecond;

        [Tooltip("Школа урона DoT (гасится соответствующей бронёй).")]
        [FormerlySerializedAs("_damageType")]
        [SerializeField] private DamageSchool _damageSchool = DamageSchool.Magical;

        [Tooltip("Сродство урона DoT: Яд для отравления (иммунна Нежить/Конструкты), Тьма/Свет — по типу существа цели.")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

        [Tooltip("Доля от МАКСИМАЛЬНОГО HP цели в секунду (0.03 = 3%/сек, «Поджог» Огненного мечника). " +
                 "Складывается с плоским уроном выше; так DoT одинаково жалит и толстых, и тонких.")]
        [SerializeField] private float _damagePctTargetMaxHp;

        public float Interval => _interval;
        public ScalableValue Potency => _damagePerSecond;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            float damage = DamagePerSecond(ctx.Potency, ctx.Target) * ctx.Dt * ctx.Stacks;
            if (damage <= 0f) return;

            // Periodic: тик DoT не будит реактивы «на удар» — горение и яд не должны запускать шипы и щиты.
            ctx.Combat.DealDamage(new DamageRequest(ctx.Source, ctx.Target, damage, _damageSchool, ctx.Combat.ArmorK,
                sourceKind: DamageSourceKind.Periodic, affinity: _affinity));
        }

        /// <summary>
        /// Урон в секунду одного стака по этой цели: плоская потенция (уже отскейленная статами источника)
        /// плюс доля от максимального HP цели. Публичен, потому что детонация («Воспламенение») должна
        /// досчитать НЕНАНЕСЁННЫЙ остаток DoT — а для этого ей нужен тот же самый расчёт, что и в тике.
        /// </summary>
        public float DamagePerSecond(float scaledPotency, RuntimeUnit target)
        {
            float dps = scaledPotency;
            if (_damagePctTargetMaxHp > 0f && target != null)
                dps += target.Stats.Get(StatType.MaxHP) * _damagePctTargetMaxHp;
            return dps;
        }
    }
}
