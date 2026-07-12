using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Simulation;
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

                // Прерывание при потере дееспособности (стан/сон) или в полёте (§9.9). CanAct посчитан
                // на прошлом тике (Effects идёт ПОСЛЕ AutoAttack) — окно в 1 тик (вики «14»). Замах →
                // сброс+рефанд; восстановление → отмена хвоста (урон уже нанесён, рефанда нет), Idle.
                if (!unit.CanAct || unit.DisplacedTicksRemaining > 0)
                {
                    if (unit.Phase == AttackPhase.Windup) Interrupt(unit, ctx);
                    else if (unit.Phase == AttackPhase.Recovery) { unit.Phase = AttackPhase.Idle; unit.RecoveryRemaining = 0; }
                    continue; // оглушён/в полёте — кулдаун не тикает (как было)
                }

                // Якорный кулдаун тикает КАЖДЫЙ тик, в т.ч. во время замаха: период damage→damage = интервал,
                // windup не добавляется к интервалу (вики «14»). Замах всегда короче интервала (кламп),
                // поэтому кулдаун не успевает обнулиться до резолва.
                if (unit.AttackCooldownTicks > 0) unit.AttackCooldownTicks--;

                // Фаза замаха: досчитываем до кадра контакта.
                if (unit.Phase == AttackPhase.Windup)
                {
                    unit.WindupRemaining--;
                    if (unit.WindupRemaining <= 0) Resolve(unit, ctx);
                    continue;
                }

                // Фаза восстановления: досчитываем хвост, новый замах начать нельзя, пока не истечёт.
                if (unit.Phase == AttackPhase.Recovery)
                {
                    unit.RecoveryRemaining--;
                    if (unit.RecoveryRemaining <= 0) unit.Phase = AttackPhase.Idle;
                    continue;
                }

                // Ещё на кулдауне — ждём.
                if (unit.AttackCooldownTicks > 0) continue;

                // Готов к атаке: нужна валидная цель в радиусе. Для хил-режима «цель авто-атаки» —
                // раненый союзник (AutoAttackTarget, пишет мозг), не враг: гейтим/снапшотим замах по нему,
                // тогда Resolve лечит именно его (§9.2). CurrentTarget (враг) остаётся движению/отступлению.
                RuntimeUnit target = IsHealMode(unit) ? unit.AutoAttackTarget : unit.CurrentTarget;
                if (target == null || target.IsDead) continue;

                // Досягаемость — с учётом тел обоих юнитов (§9, зазор поверхностей). Метрика едина с
                // движением и сепарацией: см. CombatPositioning.AttackReachCenter.
                if (!CombatPositioning.InAttackRange(unit, target, ctx.Tuning)) continue;

                EnterWindup(unit, target, ctx);
            }
        }

        /// <summary>Вход в замах: рестарт кулдауна (якорь), расчёт windupTicks, снапшот цели, событие старта.</summary>
        private void EnterWindup(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx)
        {
            float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
            int intervalTicks = AttackTiming.IntervalTicks(attackSpeed);
            unit.AttackCooldownTicks = intervalTicks;

            UnitVisual visual = unit.Unit != null ? unit.Unit.Visual : null;
            int frameCount = visual != null ? visual.AttackFrameCount : 0;
            int hitFrame   = visual != null ? visual.AttackHitFrame  : 0;

            unit.WindupTicks = unit.WindupRemaining = AttackTiming.WindupTicks(hitFrame, frameCount, intervalTicks);
            unit.Phase = AttackPhase.Windup;
            unit.WindupTarget = target;

            ctx.NotifyAttackStarted(unit, target);

            // Краевой случай hitFrame=0 / интервал=1 → windup 0 → удар в тот же тик.
            if (unit.WindupRemaining <= 0) Resolve(unit, ctx);
        }

        /// <summary>Конец замаха: нанести урон по снапшот-цели, если она жива и в радиусе; иначе вхолостую.</summary>
        private void Resolve(RuntimeUnit unit, ICombatContext ctx)
        {
            // Замах кончился → хвост-восстановление (или сразу Idle, если восстановления нет). Переход
            // выполняем ДО расчёта урона: юнит «занят» бэксвингом независимо от того, попал он или вхолостую.
            unit.WindupRemaining = 0;
            EnterRecovery(unit);
            RuntimeUnit target = unit.WindupTarget;
            unit.WindupTarget = null;

            // Цель пропала к удару (мертва / вне радиуса) → вхолостую, кулдаун уже потрачен на старте.
            if (target == null || target.IsDead) return;

            // Досягаемость с учётом тел (см. EnterWindup). reach также = длина линейной АА ниже.
            float reach = CombatPositioning.AttackReachCenter(unit, target, ctx.Tuning);
            if ((target.Position - unit.Position).sqrMagnitude > reach * reach) return;

            // Прирост ресурса — на момент реального удара (мана-реликвии).
            GainResourceOnHit(unit);

            AttackType attackType = unit.Unit != null ? unit.Unit.AttackType : AttackType.Melee;
            float raw = unit.Stats.Get(StatType.AutoAttackDamage);
            DamageType dmgType = unit.Unit != null ? unit.Unit.DamageType : DamageType.Physical;
            AreaShape shape = unit.Unit != null ? unit.Unit.AutoAttackShape : AreaShape.None;

            // Хил-режим (Светлый пастырь): вместо урона — tracking-хил-снаряд в снапшот-союзника.
            // amount = AutoAttackDamage (сырое; HealShieldDealt/TakenEff применяет ctx.Heal при попадании).
            if (IsHealMode(unit))
            {
                float healSpeed  = unit.Stats.Get(StatType.ProjectileSpeed);
                float healRadius = unit.Stats.Get(StatType.Size) * ctx.Tuning.ProjectileHitRadiusFactor;
                ctx.SpawnProjectile(new ProjectileSpawn(
                    unit, unit.Position, target,
                    healSpeed, healRadius, raw, dmgType, ctx.ArmorK, maxPierces: 0, isHeal: true));
                return;
            }

            // §9.6 усиление следующей атаки («Скрытность»): множим урон разово и снимаем баф стелса.
            if (unit.EmpowerDamageMult > 0f)
            {
                raw *= unit.EmpowerDamageMult;
                unit.EmpowerDamageMult = 0f;
                ctx.Dispel(new DispelRequest(unit, DispelTargetPolarity.Any, EffectTag.Stealth, int.MaxValue, 0));
            }

            // §10.5 блинк убийцы: удар из скрытности телепортирует его за спину цели (в момент удара, до урона).
            if (unit.BlinkBehindOnNextAttack)
            {
                unit.BlinkBehindOnNextAttack = false;
                CombatPositioning.TeleportBehind(unit, target);
            }

            if (attackType == AttackType.Melee)
            {
                if (shape == AreaShape.Line)
                {
                    DealLineDamage(unit, target, reach, raw, dmgType, ctx);
                }
                else
                {
                    ctx.DealDamage(new DamageRequest(unit, target, raw, dmgType, ctx.ArmorK, isAutoAttack: true));
                    ApplyAutoAttackOnHit(unit, target, ctx); // §9.1 (мили single)
                }
            }
            else
            {
                float speed = unit.Stats.Get(StatType.ProjectileSpeed);
                int   pierces = (int)unit.Stats.Get(StatType.ProjectilePierce);
                float collRadius = unit.Stats.Get(StatType.Size) * ctx.Tuning.ProjectileHitRadiusFactor;

                // On-hit эффекты (§9.1) едут на снаряде — накладываются в ProjectileSystem при попадании.
                ctx.SpawnProjectile(new ProjectileSpawn(
                    unit, unit.Position, target,
                    speed, collRadius, raw, dmgType, ctx.ArmorK, pierces,
                    onHitEffects: unit.Unit != null ? unit.Unit.AutoAttackEffects : null,
                    isAutoAttack: true));
            }
        }

        /// <summary>Наложить on-hit эффекты авто-атаки реликвии на задетую цель (§9.1, мили-путь).</summary>
        private static void ApplyAutoAttackOnHit(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx)
        {
            EffectData[] effects = unit.Unit != null ? unit.Unit.AutoAttackEffects : null;
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null) ctx.ApplyEffect(target, effects[i], unit);
        }

        /// <summary>Хил-автоатака (Светлый пастырь): авто-атака лечит союзника вместо урона по врагу (§9.2).</summary>
        private static bool IsHealMode(RuntimeUnit unit) =>
            unit.Unit?.Ai != null && unit.Unit.Ai.AutoAttackMode == AutoAttackMode.Heal;

        /// <summary>Хвост-восстановление после удара: Recovery на N тиков, либо сразу Idle, если восстановления нет (0 сек).</summary>
        private static void EnterRecovery(RuntimeUnit unit)
        {
            int ticks = unit.Unit != null ? AttackTiming.RecoveryTicks(unit.Unit.AttackRecoverySeconds) : 0;
            if (ticks <= 0)
            {
                unit.Phase = AttackPhase.Idle;
                unit.RecoveryRemaining = 0;
            }
            else
            {
                unit.Phase = AttackPhase.Recovery;
                unit.RecoveryRemaining = ticks;
            }
        }

        /// <summary>Прерывание замаха: сброс + рефанд кулдауна (бьёт снова, как только сможет) + событие.</summary>
        private static void Interrupt(RuntimeUnit unit, ICombatContext ctx)
        {
            unit.Phase = AttackPhase.Idle;
            unit.WindupRemaining = 0;
            unit.RecoveryRemaining = 0;
            unit.WindupTarget = null;
            unit.AttackCooldownTicks = 0;
            ctx.NotifyAttackInterrupted(unit);
        }

        /// <summary>Линейная авто-атака «Размашистый выпад»: полоса к цели, урон по всем врагам в ней.</summary>
        private void DealLineDamage(RuntimeUnit unit, RuntimeUnit target, float length, float raw, DamageType dmgType, ICombatContext ctx)
        {
            float width = unit.Unit.AutoAttackWidth;
            Vector2 dir = target.Position - unit.Position;

            // Dev-оверлей зоны (показываем полосу даже если никого не задели).
            ctx.ReportAreaHit(AreaHit.Line(unit.Position, dir, length, width, unit.Team));

            ctx.QueryUnitsInLine(unit.Position, dir, length, width, _lineTargets, TargetFilter.Enemies, unit.Team);

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итоговое состояние.
            for (int t = 0; t < _lineTargets.Count; t++)
            {
                ctx.DealDamage(new DamageRequest(unit, _lineTargets[t], raw, dmgType, ctx.ArmorK, isAutoAttack: true));
                ApplyAutoAttackOnHit(unit, _lineTargets[t], ctx); // §9.1 (мили Line — по каждой задетой)
            }
        }

        /// <summary>Начислить ресурс за удар (× ResourceGainEff), клампить к MaxResource.</summary>
        private static void GainResourceOnHit(RuntimeUnit unit)
        {
            float onHit = unit.Unit != null ? unit.Unit.ResourceOnHit : 0f;
            if (onHit <= 0f) return;

            float gain = onHit * unit.Stats.Get(StatType.ResourceGainEff);
            unit.CurrentResource += gain;

            float maxRes = unit.Stats.Get(StatType.MaxResource);
            if (maxRes > 0f && unit.CurrentResource > maxRes) unit.CurrentResource = maxRes;
        }
    }
}
