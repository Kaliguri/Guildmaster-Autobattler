using System;
using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Воспламенение» (Огненный мечник): сжигает накопленные на цели «Угли» — наносит
    /// <see cref="_damagePerStack"/> за каждый стак и сбрасывает их. Если это добивает цель, мечник
    /// получает награду: баф темпа и лечение от недостающего HP (карточка ГДД).
    /// <para><b>Числа:</b> <c>_detonateTag</c> — какой эффект считаем топливом (по умолчанию «Угли»);
    /// <c>_damagePerStack</c> — урон за каждый сожжённый стак (15 → при 20 стаках взрыв на 300);
    /// <c>_school</c>/<c>_physicalSubtype</c>/<c>_magicElement</c> — тип урона взрыва (Огонь, поэтому
    /// сами «Угли» его и усиливают).</para>
    /// <para><b>Когда срабатывает:</b> в момент наложения (эффект мгновенный, длительность 0) — то
    /// есть по касту способности.</para>
    /// </summary>
    /// <remarks>
    /// Урон считается ДО сброса стаков намеренно: взрыв — огненный урон, значит сами «Угли» его и
    /// усиливают (+1% за стак), а снимать их раньше расчёта значило бы обокрасть собственную петлю
    /// «копи → трать». Модель принята 2026-07-26/4: детонация читает число стаков, а не остаток DoT.
    /// <para>Вешается мгновенным эффектом (длительность 0): вся работа в <see cref="OnApply"/>.</para>
    /// </remarks>
    [Serializable]
    public sealed class IgnitionComponent : IRuntimeEffectComponent
    {
        [Tooltip("Тег детонируемых стаков («Угли» = Ember).")]
        [SerializeField] private EffectTag _detonateTag = EffectTag.Ember;

        [Tooltip("Урон за каждый сожжённый стак «Углей». Мечник = 15.")]
        [SerializeField] private float _damagePerStack = 15f;

        [Tooltip("Школа урона детонации.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Magical;

        [Tooltip("Физ-подтип урона детонации (при школе Physical). Питает тег; None = не задан.")]
        [SerializeField] private PhysicalSubtype _physicalSubtype = PhysicalSubtype.None;

        [Tooltip("Магический элемент урона детонации (при школе Magical): Огонь для «Воспламенения». Питает тег; None = не задан.")]
        [SerializeField] private MagicElement _magicElement = MagicElement.None;

        [Tooltip("Сродство урона детонации.")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

        /// <summary>Тип урона детонации (прямые поля источника) — для агрегации тегов «быстрого чтения».</summary>
        public DamageType DamageType => new DamageType(_school, _physicalSubtype, _magicElement, _affinity);

        [Tooltip("Награда за добивание: баф на самого мечника (+скорость атаки/бега). Пусто = без бафа.")]
        [SerializeField] private EffectData _onKillBuff;

        [Tooltip("Награда за добивание: лечение мечника, доля от его НЕДОСТАЮЩЕГО HP (0.25 = 25%).")]
        [SerializeField] private float _onKillHealPctMissingHp = 0.25f;

        public void OnExpire(in EffectContext ctx) { }

        public void OnApply(in EffectContext ctx)
        {
            RuntimeUnit target = ctx.Target;
            RuntimeUnit caster = ctx.Source;
            if (target == null || target.IsDead) return;

            int stacks = 0;
            for (int i = 0; i < target.ActiveEffects.Count; i++)
            {
                RuntimeEffect eff = target.ActiveEffects[i];
                if (eff.Def == null || (eff.Def.Tags & _detonateTag) == 0) continue;
                stacks += eff.Stacks;
            }

            if (stacks <= 0) return;

            float damage = _damagePerStack * stacks;
            if (damage > 0f)
            {
                // Урон летит ДО сброса: «Угли» усиливают и сам взрыв (+1% за стак), как любой огонь.
                ctx.Combat.DealDamage(new DamageRequest(caster, target, damage, _school, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability, affinity: _affinity, element: _magicElement));
            }

            // Угли израсходованы взрывом — снимаем их целиком.
            ctx.Combat.Dispel(new DispelRequest(target, DispelTargetPolarity.Any, _detonateTag,
                dispelPower: int.MaxValue, maxCount: 0));

            if (target.IsDead) RewardKill(caster, ctx);
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
