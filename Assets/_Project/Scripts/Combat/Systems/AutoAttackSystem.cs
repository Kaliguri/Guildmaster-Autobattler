using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Двухфазная авто-атака (вики «14»): кулдаун → <b>замах (windup)</b> → удар на кадре контакта.
    /// Урон наносится, когда windup-таймер истёк, а не в начале замаха. Всё на int-тиках —
    /// детерминизм сохраняется (урон по счётчику тиков), пауза работает автоматически.
    /// <para>
    /// Якорь кулдауна — старт замаха (период damage→damage = интервал). Прерывание (стан/смерть себя)
    /// сбрасывает замах и <b>рефандит</b> кулдаун. Цель пропала к удару (мертва/вне радиуса) → удар вхолостую,
    /// кулдаун потрачен. Тип атаки (<see cref="AttackType"/>) определяет резолв: мили single/Line или снаряд.
    /// </para>
    /// </summary>
    public sealed class AutoAttackSystem
    {
        // Переиспользуемый буфер целей линейной авто-атаки — без аллокаций на горячем пути.
        private readonly List<RuntimeUnit> _lineTargets = new List<RuntimeUnit>();

        /// <summary>Обработать автоатаки всех живых юнитов за один тик. <paramref name="dt"/> не используется (тайминг на тиках).</summary>
        public void Tick(List<RuntimeUnit> units, ICombatContext ctx, float dt)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;

                // Прерывание замаха при потере дееспособности (стан/сон). CanAct посчитан на прошлом тике
                // (Effects идёт ПОСЛЕ AutoAttack) — это ожидаемое окно в 1 тик (вики «14»).
                if (!unit.CanAct)
                {
                    if (unit.IsWindingUp) Interrupt(unit, ctx);
                    continue; // оглушён и не в замахе — кулдаун не тикает (как было)
                }

                // Якорный кулдаун тикает КАЖДЫЙ тик, в т.ч. во время замаха: период damage→damage = интервал,
                // windup не добавляется к интервалу (вики «14»). Замах всегда короче интервала (кламп),
                // поэтому кулдаун не успевает обнулиться до резолва.
                if (unit.AttackCooldownTicks > 0) unit.AttackCooldownTicks--;

                // Фаза замаха: досчитываем до кадра контакта.
                if (unit.IsWindingUp)
                {
                    unit.WindupRemaining--;
                    if (unit.WindupRemaining <= 0) Resolve(unit, ctx);
                    continue;
                }

                // Ещё на кулдауне — ждём.
                if (unit.AttackCooldownTicks > 0) continue;

                // Готов к атаке: нужна валидная цель в радиусе. Для хил-режима «цель авто-атаки» —
                // раненый союзник (AutoAttackTarget, пишет мозг), не враг: гейтим/снапшотим замах по нему,
                // тогда Resolve лечит именно его (§9.2). CurrentTarget (враг) остаётся движению/отступлению.
                RuntimeUnit target = IsHealMode(unit) ? unit.AutoAttackTarget : unit.CurrentTarget;
                if (target == null || target.IsDead) continue;

                float range = unit.Stats.Get(StatType.AttackRange);
                if ((target.Position - unit.Position).sqrMagnitude > range * range) continue;

                EnterWindup(unit, target, ctx);
            }
        }

        /// <summary>Вход в замах: рестарт кулдауна (якорь), расчёт windupTicks, снапшот цели, событие старта.</summary>
        private void EnterWindup(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx)
        {
            float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
            int intervalTicks = AttackTiming.IntervalTicks(attackSpeed);
            unit.AttackCooldownTicks = intervalTicks;

            UnitVisual visual = unit.Relic != null ? unit.Relic.Visual : null;
            int frameCount = visual != null ? visual.AttackFrameCount : 0;
            int hitFrame   = visual != null ? visual.AttackHitFrame  : 0;

            unit.WindupTicks = unit.WindupRemaining = AttackTiming.WindupTicks(hitFrame, frameCount, intervalTicks);
            unit.IsWindingUp = true;
            unit.WindupTarget = target;

            ctx.NotifyAttackStarted(unit, target);

            // Краевой случай hitFrame=0 / интервал=1 → windup 0 → удар в тот же тик.
            if (unit.WindupRemaining <= 0) Resolve(unit, ctx);
        }

        /// <summary>Конец замаха: нанести урон по снапшот-цели, если она жива и в радиусе; иначе вхолостую.</summary>
        private void Resolve(RuntimeUnit unit, ICombatContext ctx)
        {
            unit.IsWindingUp = false;
            unit.WindupRemaining = 0;
            RuntimeUnit target = unit.WindupTarget;
            unit.WindupTarget = null;

            // Цель пропала к удару (мертва / вне радиуса) → вхолостую, кулдаун уже потрачен на старте.
            if (target == null || target.IsDead) return;

            float range = unit.Stats.Get(StatType.AttackRange);
            if ((target.Position - unit.Position).sqrMagnitude > range * range) return;

            // Прирост ресурса — на момент реального удара (мана-реликвии).
            GainResourceOnHit(unit);

            AttackType attackType = unit.Relic != null ? unit.Relic.AttackType : AttackType.Melee;
            float raw = unit.Stats.Get(StatType.AutoAttackDamage);
            DamageType dmgType = unit.Relic != null ? unit.Relic.DamageType : DamageType.Physical;
            AreaShape shape = unit.Relic != null ? unit.Relic.AutoAttackShape : AreaShape.None;

            // Хил-режим (Светлый пастырь): вместо урона — tracking-хил-снаряд в снапшот-союзника.
            // amount = AutoAttackDamage (сырое; HealShieldDealt/TakenEff применяет ctx.Heal при попадании).
            if (IsHealMode(unit))
            {
                float healSpeed  = unit.Stats.Get(StatType.ProjectileSpeed);
                float healRadius = unit.Stats.Get(StatType.Size) * 0.25f;
                ctx.SpawnProjectile(new ProjectileSpawn(
                    unit, unit.Position, target,
                    healSpeed, healRadius, raw, dmgType, ctx.ArmorK, maxPierces: 0, isHeal: true));
                return;
            }

            if (attackType == AttackType.Melee)
            {
                if (shape == AreaShape.Line)
                {
                    DealLineDamage(unit, target, range, raw, dmgType, ctx);
                }
                else
                {
                    ctx.DealDamage(new DamageRequest(unit, target, raw, dmgType, ctx.ArmorK));
                    ApplyAutoAttackOnHit(unit, target, ctx); // §9.1 (мили single)
                }
            }
            else
            {
                float speed = unit.Stats.Get(StatType.ProjectileSpeed);
                int   pierces = (int)unit.Stats.Get(StatType.ProjectilePierce);
                float collRadius = unit.Stats.Get(StatType.Size) * 0.25f;

                // On-hit эффекты (§9.1) едут на снаряде — накладываются в ProjectileSystem при попадании.
                ctx.SpawnProjectile(new ProjectileSpawn(
                    unit, unit.Position, target,
                    speed, collRadius, raw, dmgType, ctx.ArmorK, pierces,
                    onHitEffects: unit.Relic != null ? unit.Relic.AutoAttackEffects : null));
            }
        }

        /// <summary>Наложить on-hit эффекты авто-атаки реликвии на задетую цель (§9.1, мили-путь).</summary>
        private static void ApplyAutoAttackOnHit(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx)
        {
            EffectData[] effects = unit.Relic != null ? unit.Relic.AutoAttackEffects : null;
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null) ctx.ApplyEffect(target, effects[i], unit);
        }

        /// <summary>Хил-автоатака (Светлый пастырь): авто-атака лечит союзника вместо урона по врагу (§9.2).</summary>
        private static bool IsHealMode(RuntimeUnit unit) =>
            unit.Relic?.Ai != null && unit.Relic.Ai.AutoAttackMode == AutoAttackMode.Heal;

        /// <summary>Прерывание замаха: сброс + рефанд кулдауна (бьёт снова, как только сможет) + событие.</summary>
        private static void Interrupt(RuntimeUnit unit, ICombatContext ctx)
        {
            unit.IsWindingUp = false;
            unit.WindupRemaining = 0;
            unit.WindupTarget = null;
            unit.AttackCooldownTicks = 0;
            ctx.NotifyAttackInterrupted(unit);
        }

        /// <summary>Линейная авто-атака «Размашистый выпад»: полоса к цели, урон по всем врагам в ней.</summary>
        private void DealLineDamage(RuntimeUnit unit, RuntimeUnit target, float length, float raw, DamageType dmgType, ICombatContext ctx)
        {
            float width = unit.Relic.AutoAttackWidth;
            Vector2 dir = target.Position - unit.Position;

            // Dev-оверлей зоны (показываем полосу даже если никого не задели).
            ctx.ReportAreaHit(AreaHit.Line(unit.Position, dir, length, width, unit.Team));

            ctx.QueryUnitsInLine(unit.Position, dir, length, width, _lineTargets, TargetFilter.Enemies, unit.Team);

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итоговое состояние.
            for (int t = 0; t < _lineTargets.Count; t++)
            {
                ctx.DealDamage(new DamageRequest(unit, _lineTargets[t], raw, dmgType, ctx.ArmorK));
                ApplyAutoAttackOnHit(unit, _lineTargets[t], ctx); // §9.1 (мили Line — по каждой задетой)
            }
        }

        /// <summary>Начислить ресурс за удар (× ResourceGainEff), клампить к MaxResource.</summary>
        private static void GainResourceOnHit(RuntimeUnit unit)
        {
            float onHit = unit.Relic != null ? unit.Relic.ResourceOnHit : 0f;
            if (onHit <= 0f) return;

            float gain = onHit * unit.Stats.Get(StatType.ResourceGainEff);
            unit.CurrentResource += gain;

            float maxRes = unit.Stats.Get(StatType.MaxResource);
            if (maxRes > 0f && unit.CurrentResource > maxRes) unit.CurrentResource = maxRes;
        }
    }
}
