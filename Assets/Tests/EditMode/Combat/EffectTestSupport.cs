using System;
using System.Collections.Generic;
using System.Reflection;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Хелперы для headless-тестов эффектов: мок-контекст, билдер <see cref="EffectData"/> и
    /// тест-компоненты. Компоненты сюда кладутся напрямую (без сериализации) — кросс-сборочный
    /// SerializeReference проверяет отдельный спайк S1.
    /// </summary>
    internal static class TestEffect
    {
        /// <summary>Собрать <see cref="EffectData"/> с заданными полями (приватные поля через рефлексию).</summary>
        public static EffectData Make(
            float baseDuration = 1f,
            EffectPolarity polarity = EffectPolarity.Neutral,
            EffectTag tags = EffectTag.None,
            StackRule stacking = StackRule.None,
            int maxStacks = 1,
            int cleanseTier = 0,
            bool unremovable = false,
            params IEffectComponent[] components)
        {
            var data = ScriptableObject.CreateInstance<EffectData>();
            Set(data, "_id", "test");
            Set(data, "_baseDuration", baseDuration);
            Set(data, "_polarity", polarity);
            Set(data, "_tags", tags);
            Set(data, "_stacking", stacking);
            Set(data, "_maxStacks", maxStacks);
            Set(data, "_cleanseTier", cleanseTier);
            Set(data, "_unremovable", unremovable);
            Set(data, "_components", components ?? Array.Empty<IEffectComponent>());
            return data;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo fi = typeof(EffectData).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            fi.SetValue(target, value);
        }
    }

    /// <summary>Билдер <see cref="AbilityData"/> для тестов (приватные поля через рефлексию).</summary>
    internal static class TestAbility
    {
        public static AbilityData Make(
            EffectData[] effects = null,
            float cooldown = 5f,
            float cost = 0f,
            AbilityTargetMode mode = AbilityTargetMode.Self,
            float damageMultiplier = 0f,
            AreaShape areaShape = AreaShape.None,
            float areaRadius = 0f,
            float healFlat = 0f,
            float healPctTargetMissingHp = 0f,
            CastCondition castCondition = CastCondition.Immediately,
            int castConditionCount = 1,
            float castConditionRadius = 0f,
            float castConditionHpPct = 0.5f,
            float castOverrideSelfHpPct = 0f,
            EffectTag triggerTag = EffectTag.None,
            bool consumesTriggerTag = false)
        {
            var a = new AbilityData();
            Set(a, "_effects", effects ?? System.Array.Empty<EffectData>());
            Set(a, "_baseCooldown", cooldown);
            Set(a, "_resourceCost", cost);
            Set(a, "_targetMode", mode);
            Set(a, "_damageMultiplier", damageMultiplier);
            Set(a, "_areaShape", areaShape);
            Set(a, "_areaRadius", areaRadius);
            Set(a, "_healFlat", healFlat);
            Set(a, "_healPctTargetMissingHp", healPctTargetMissingHp);
            Set(a, "_castCondition", castCondition);
            Set(a, "_castConditionCount", castConditionCount);
            Set(a, "_castConditionRadius", castConditionRadius);
            Set(a, "_castConditionHpPct", castConditionHpPct);
            Set(a, "_castOverrideSelfHpPct", castOverrideSelfHpPct);
            Set(a, "_triggerTag", triggerTag);
            Set(a, "_consumesTriggerTag", consumesTriggerTag);
            return a;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo fi = typeof(AbilityData).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            fi.SetValue(target, value);
        }
    }

    /// <summary>Билдер <see cref="RelicData"/> для тестов интеграции фабрики (приватные поля через рефлексию).</summary>
    internal static class TestRelic
    {
        public static RelicData Make(
            StatModifier[] stats = null,
            EffectData[] grantedEffects = null,
            AbilityData[] abilities = null,
            AttackType attackType = AttackType.Melee,
            DamageType damageType = DamageType.Physical,
            AreaShape autoAttackShape = AreaShape.None,
            float autoAttackWidth = 1f,
            float resourceOnHit = 0f,
            UnitVisual visual = null,
            AIProfile ai = null,
            EffectData[] autoAttackEffects = null,
            bool canAttackWhileMoving = false,
            float movingAttackSpeedPenaltyPct = 0.5f)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            Set(r, "_stats", stats ?? Array.Empty<StatModifier>());
            Set(r, "_grantedEffects", grantedEffects ?? Array.Empty<EffectData>());
            Set(r, "_abilities", abilities ?? Array.Empty<AbilityData>());
            Set(r, "_attackType", attackType);
            Set(r, "_damageType", damageType);
            Set(r, "_autoAttackShape", autoAttackShape);
            Set(r, "_autoAttackWidth", autoAttackWidth);
            Set(r, "_resourceOnHit", resourceOnHit);
            Set(r, "_visual", visual);
            Set(r, "_ai", ai ?? new AIProfile());
            Set(r, "_autoAttackEffects", autoAttackEffects ?? Array.Empty<EffectData>());
            Set(r, "_canAttackWhileMoving", canAttackWhileMoving);
            Set(r, "_movingAttackSpeedPenaltyPct", movingAttackSpeedPenaltyPct);
            return r;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo fi = typeof(RelicData).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            fi.SetValue(target, value);
        }
    }

    /// <summary>Билдер <see cref="UnitVisual"/> для тестов windup: задаёт число кадров атаки и кадр контакта.</summary>
    internal static class TestVisual
    {
        public static UnitVisual Make(int frameCount, int hitFrame)
        {
            var v = ScriptableObject.CreateInstance<UnitVisual>();
            FieldInfo attack = typeof(UnitVisual).GetField("_attack", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo hits   = typeof(UnitVisual).GetField("_attackHitFrames", BindingFlags.Instance | BindingFlags.NonPublic);
            attack.SetValue(v, new Sprite[frameCount < 0 ? 0 : frameCount]);
            hits.SetValue(v, new[] { hitFrame });
            return v;
        }
    }

    /// <summary>Юнит-фабрика для тестов эффектов.</summary>
    internal static class TestUnit
    {
        public static RuntimeUnit Make(int team = 0, float maxHp = 1000f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, maxHp) });
            return new RuntimeUnit { Team = team, Stats = stats, CurrentHP = maxHp };
        }
    }

    /// <summary>Мок шва <see cref="ICombatContext"/>: пишет урон/хил, диспел делегирует в EffectSystem.</summary>
    internal sealed class MockCombatContext : ICombatContext
    {
        private readonly IRngService _rng;
        private readonly EffectSystem _effects;

        public readonly List<DamageRequest> DamageCalls = new List<DamageRequest>();
        public float TotalRawDamage;
        public float TotalHealed;

        public MockCombatContext(IRngService rng = null, EffectSystem effects = null)
        {
            _rng = rng ?? new XorShiftRng(1UL);
            _effects = effects;
        }

        public void DealDamage(in DamageRequest req)
        {
            DamageCalls.Add(req);
            TotalRawDamage += req.RawDamage;
        }

        public void Heal(RuntimeUnit target, float amount, RuntimeUnit source) => TotalHealed += amount;
        public void SpawnProjectile(in ProjectileSpawn spawn) { }

        public int QueryUnitsInRadius(
            Vector2 center, float radius, List<RuntimeUnit> results, TargetFilter filter, int requestingTeam) => 0;

        public int QueryUnitsInLine(
            Vector2 origin, Vector2 direction, float length, float width,
            List<RuntimeUnit> results, TargetFilter filter, int requestingTeam) => 0;

        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source)
            => _effects?.Apply(target, def, source, this);

        public void ReportAreaHit(in AreaHit hit) { }

        public void Dispel(in DispelRequest req) => _effects?.Dispel(in req, this);
        public void Displace(in DisplaceRequest req) { }

        public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) { }
        public void NotifyAttackInterrupted(RuntimeUnit unit) { }

        public IRngService Rng => _rng;
        public int CurrentTick => 0;
        public float ArmorK => 100f;
    }

    /// <summary>Установка приватных <c>[SerializeField]</c> компонентов в тестах (без сериализации).</summary>
    internal static class Reflect
    {
        public static T With<T>(this T obj, string field, object value)
        {
            FieldInfo fi = typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) throw new ArgumentException($"Нет поля {field} в {typeof(T).Name}");
            fi.SetValue(obj, value);
            return obj;
        }
    }

    /// <summary>Считает вызовы жизненного цикла.</summary>
    internal sealed class CountingComponent : IRuntimeEffectComponent
    {
        public int Applied;
        public int Expired;

        public void OnApply(in EffectContext ctx)  => Applied++;
        public void OnExpire(in EffectContext ctx) => Expired++;
    }

    /// <summary>Периодический компонент: считает срабатывания и накапливает <c>Potency × Dt × Stacks</c>.</summary>
    internal sealed class CountingPeriodicComponent : IPeriodicComponent, IScalablePotency
    {
        private readonly float _interval;
        private readonly ScalableValue _potency;

        public int Ticks;
        public float TotalApplied;

        public CountingPeriodicComponent(float interval, float potencyPerSecond)
        {
            _interval = interval;
            _potency = new ScalableValue(potencyPerSecond);
        }

        public float Interval => _interval;
        public ScalableValue Potency => _potency;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            Ticks++;
            TotalApplied += ctx.Potency * ctx.Dt * ctx.Stacks;
        }
    }
}
