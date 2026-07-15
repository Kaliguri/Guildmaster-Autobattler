using System;
using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Воспламенение» (Огненный мечник): снимает с цели все «Поджоги» и заставляет каждый немедленно
    /// нанести свой НЕДОНЕСЁННЫЙ урон — то есть DoT не пропадает, а сгорает разом. Если это добивает цель,
    /// мечник получает награду: баф темпа и лечение от недостающего HP (карточка ГДД).
    /// </summary>
    /// <remarks>
    /// Считаем ровно то, что DoT не успел натикать: <c>урон/сек × оставшиеся секунды × стаки</c>, причём
    /// «урон/сек» берётся у самого <see cref="PeriodicDamageComponent"/> — тем же методом, что и в тике,
    /// поэтому доля от макс. HP цели учитывается автоматически и формула не разъезжается с DoT.
    /// <para>Вешается мгновенным эффектом (длительность 0): вся работа в <see cref="OnApply"/>.</para>
    /// </remarks>
    [Serializable]
    public sealed class IgnitionComponent : IRuntimeEffectComponent
    {
        [Tooltip("Категория детонируемых эффектов («Поджог» = Burn).")]
        [SerializeField] private EffectTag _detonateTag = EffectTag.Burn;

        [Tooltip("Школа урона детонации.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Elemental;

        [Tooltip("Сродство урона детонации.")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

        [Tooltip("Награда за добивание: баф на самого мечника (+скорость атаки/бега). Пусто = без бафа.")]
        [SerializeField] private EffectData _onKillBuff;

        [Tooltip("Награда за добивание: лечение мечника, доля от его НЕДОСТАЮЩЕГО HP (0.25 = 25%).")]
        [SerializeField] private float _onKillHealPctMissingHp = 0.25f;

        // Снимаем эффекты после подсчёта — не мутируем список, пока по нему идём.
        [NonSerialized] private readonly List<RuntimeEffect> _detonated = new List<RuntimeEffect>();

        public void OnExpire(in EffectContext ctx) { }

        public void OnApply(in EffectContext ctx)
        {
            RuntimeUnit target = ctx.Target;
            RuntimeUnit caster = ctx.Source;
            if (target == null || target.IsDead) return;

            _detonated.Clear();
            float damage = 0f;

            for (int i = 0; i < target.ActiveEffects.Count; i++)
            {
                RuntimeEffect eff = target.ActiveEffects[i];
                if (eff.Def == null || (eff.Def.Tags & _detonateTag) == 0) continue;

                damage += RemainingDamage(eff, target);
                _detonated.Add(eff);
            }

            if (_detonated.Count == 0) return;

            if (damage > 0f)
            {
                ctx.Combat.DealDamage(new DamageRequest(caster, target, damage, _school, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability, affinity: _affinity));
            }

            // Поджоги израсходованы взрывом — снимаем их (в т.ч. если урон почему-то вышел нулевым).
            ctx.Combat.Dispel(new DispelRequest(target, DispelTargetPolarity.Any, _detonateTag,
                dispelPower: int.MaxValue, maxCount: 0));

            if (target.IsDead) RewardKill(caster, ctx);
        }

        /// <summary>Недонесённый урон одного DoT: то, что он натикал бы за оставшуюся длительность.</summary>
        private static float RemainingDamage(RuntimeEffect eff, RuntimeUnit target)
        {
            IEffectComponent[] comps = eff.Def.Components;
            if (comps == null || eff.IsPermanent) return 0f;

            float remainingSeconds = eff.RemainingTicks * SimConstants.TickDelta;
            if (remainingSeconds <= 0f) return 0f;

            float total = 0f;
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is not PeriodicDamageComponent dot) continue;

                float scaledPotency = eff.ScaledPotency != null && i < eff.ScaledPotency.Length
                    ? eff.ScaledPotency[i]
                    : 0f;

                total += dot.DamagePerSecond(scaledPotency, target) * remainingSeconds * eff.Stacks;
            }
            return total;
        }

        /// <summary>Добивание взрывом: мечник разгоняется и лечится — карточка ГДД, награда за риск само-урона.</summary>
        private void RewardKill(RuntimeUnit caster, in EffectContext ctx)
        {
            if (caster == null || caster.IsDead) return;

            if (_onKillBuff != null) ctx.Combat.ApplyEffect(caster, _onKillBuff, caster);

            if (_onKillHealPctMissingHp > 0f)
            {
                float missing = caster.Stats.Get(StatType.MaxHP) - caster.CurrentHP;
                if (missing > 0f) ctx.Combat.Heal(caster, missing * _onKillHealPctMissingHp, caster);
            }
        }
    }
}
