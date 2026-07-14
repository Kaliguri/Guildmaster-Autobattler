using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Шипастое древо» (Древень): получив АВТОАТАКУ, носитель бьёт шипами ВСЕХ врагов вокруг себя.
    /// Урон шипов масштабируется от БРОНИ носителя (а не от доли полученного урона, как
    /// <see cref="ThornsComponent"/>) — по карточке ГДД «100% статы брони».
    /// </summary>
    /// <remarks>
    /// Гейт по <see cref="CombatEventData.IsAutoAttack"/> — принципиальный: урон самих шипов автоатакой
    /// не является, поэтому встречные шипы не устраивают бесконечный пинг-понг.
    /// </remarks>
    [Serializable]
    public sealed class ArmorThornsComponent : IReactiveComponent
    {
        [Tooltip("Доля брони носителя, уходящая в урон шипов (1 = 100% статы брони).")]
        [SerializeField] private float _armorRatio = 1f;

        [Tooltip("Радиус ответного удара вокруг носителя (мировые единицы).")]
        [SerializeField] private float _radius = 3f;

        [Tooltip("Школа урона шипов.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Physical;

        [Tooltip("Сродство урона шипов (Древень с апгрейдом «Ядовитые шипы» — Яд).")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

        // Буфер запроса — компонент stateless по игровому состоянию, буфер переиспользуется как в системах.
        [NonSerialized] private readonly List<RuntimeUnit> _hits = new List<RuntimeUnit>();

        public CombatEvent Events => CombatEvent.DamageTaken;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (!e.IsAutoAttack) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            float damage = self.Stats.Get(StatType.PhysArmor) * _armorRatio * ctx.Stacks;
            if (damage <= 0f) return;

            ctx.Combat.ReportAreaHit(AreaHit.Circle(self.Position, _radius, self.Team));
            ctx.Combat.QueryUnitsInRadius(self.Position, _radius, _hits, TargetFilter.Enemies, self.Team);

            for (int i = 0; i < _hits.Count; i++)
            {
                RuntimeUnit victim = _hits[i];
                if (victim.IsDead) continue;
                ctx.Combat.DealDamage(new DamageRequest(self, victim, damage, _school, ctx.Combat.ArmorK, affinity: _affinity));
            }
        }
    }
}
