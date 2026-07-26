using System;
using System.Collections.Generic;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>«Целебный свет»</b> — пассивка Светлого пастыря (карточка [[the-shepherd]]).
    /// <para><b>Что делает:</b> каждая его автоатака не только бьёт врага, но и лечит самого раненого
    /// союзника рядом. Некого лечить — свет достаётся самому носителю, но меньше: отдавать выгоднее,
    /// чем лечиться самому.</para>
    /// <para><b>Числа:</b>
    /// <list type="bullet">
    /// <item><c>_fraction</c> — доля нанесённого урона, уходящая в лечение СЕБЯ (1 = 100% удара).</item>
    /// <item><c>_allyBonus</c> — насколько лечение союзника выгоднее (0.5 = +50%, итого 150% удара).</item>
    /// <item><c>_radius</c> — радиус поиска раненого союзника вокруг носителя, мировые единицы.</item>
    /// <item><c>_autoAttackOnly</c> — реагировать только на автоатаку (иначе лечил бы и с ульты).</item>
    /// </list>
    /// При ударе на 100 это 150 союзнику или 100 себе.</para>
    /// <para><b>Когда срабатывает:</b> реактив на <see cref="CombatEvent.DamageDealt"/> носителя —
    /// то есть в момент попадания, а не по таймеру.</para>
    /// </summary>
    /// <remarks>
    /// Готчи: цель — самый раненый по ДОЛЕ HP, а не по абсолюту (иначе свет всегда уходил бы танку);
    /// тай-брейк по <c>Id</c> — для детерминизма при равном проценте. Stateless: буфер переиспользуется.
    /// Воплощает идентичность «Света» (ГДД «8»): чистый урон + лечение. Аналог
    /// <see cref="LifestealComponent"/>, но приоритет отдан не себе.
    /// </remarks>
    [Serializable]
    public sealed class AllyMendComponent : IReactiveComponent
    {
        [Tooltip("Доля нанесённого урона, уходящая в лечение СЕБЯ (1 = 100%). База: свет всегда что-то даёт носителю.")]
        [Range(0f, 4f)]
        [SerializeField] private float _fraction = 1f;

        [Tooltip("Насколько лечение СОЮЗНИКА выгоднее само-лечения (0.5 = +50%, итого 150% нанесённого).")]
        [Range(0f, 3f)]
        [SerializeField] private float _allyBonus = 0.5f;

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
                if (u.IsDead || ReferenceEquals(u, self)) continue;

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

            // Нет раненого рядом — свет достаётся носителю, но по базовой доле: отдавать выгоднее,
            // чем лечиться самому (решение 2026-07-27/3). Прежний полный запрет само-лечения снят —
            // неубиваемым Пастыря делала ульта с процентом от недостающего HP, а не этот свет.
            if (best == null) ctx.Combat.Heal(self, heal, self);
            else ctx.Combat.Heal(best, heal * (1f + _allyBonus), self);
        }
    }
}
