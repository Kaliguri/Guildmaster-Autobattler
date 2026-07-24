using System;
using System.Collections.Generic;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Целебный свет» (Светлый пастырь): при нанесении урона автоатакой носитель лечит самого
    /// раненого союзника (по HP%) в радиусе вокруг СЕБЯ на долю нанесённого урона. Реактивный —
    /// слушает <see cref="CombatEvent.DamageDealt"/> своего носителя. Воплощает идентичность «Света»
    /// (ГДД «8»): чистый (True) урон + лечение союзнику. Аналог <see cref="LifestealComponent"/>, но
    /// исцеляет не себя, а раненого союзника.
    /// </summary>
    /// <remarks>
    /// Stateless: буфер запроса переиспользуется (как в системах и <see cref="ArmorThornsComponent"/>).
    /// Тай-брейк выбора цели — по <c>Id</c>, чтобы выбор был детерминированным при равном HP%.
    /// </remarks>
    [Serializable]
    public sealed class AllyMendComponent : IReactiveComponent
    {
        [Tooltip("Доля нанесённого урона, уходящая в лечение союзника (1 = 100%).")]
        [Range(0f, 4f)]
        [SerializeField] private float _fraction = 1f;

        [Tooltip("Радиус поиска раненого союзника вокруг носителя (мировые единицы).")]
        [SerializeField] private float _radius = 5f;

        [Tooltip("Реагировать только на автоатаку. Иначе любой нанесённый носителем урон лечил бы союзника.")]
        [SerializeField] private bool _autoAttackOnly = true;

        // Буфер запроса — компонент stateless по игровому состоянию, буфер переиспользуется.
        [NonSerialized] private readonly List<RuntimeUnit> _allies = new List<RuntimeUnit>();

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_autoAttackOnly && !e.IsAutoAttack) return;

            float heal = e.Amount * _fraction * ctx.Stacks;
            if (heal <= 0f) return;

            RuntimeUnit self = ctx.Target; // носитель события DamageDealt = тот, кто нанёс урон
            if (self == null || self.IsDead) return;

            ctx.Combat.QueryUnitsInRadius(self.Position, _radius, _allies, TargetFilter.Allies, self.Team);

            RuntimeUnit best = null;
            float bestPct = float.MaxValue;
            for (int i = 0; i < _allies.Count; i++)
            {
                RuntimeUnit u = _allies[i];
                if (u.IsDead) continue;

                float maxHp = u.Stats.Get(StatType.MaxHP);
                if (maxHp <= 0f) continue;

                float pct = u.CurrentHP / maxHp;
                // Самый раненый (min HP%); при равенстве — меньший Id для детерминизма.
                if (pct < bestPct || (pct == bestPct && (best == null || u.Id < best.Id)))
                {
                    bestPct = pct;
                    best = u;
                }
            }

            if (best == null) return;
            ctx.Combat.Heal(best, heal, self);
        }
    }
}
