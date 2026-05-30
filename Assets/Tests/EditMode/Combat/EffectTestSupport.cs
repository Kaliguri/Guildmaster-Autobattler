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

    /// <summary>Мок шва <see cref="ICombatContext"/>: пишет урон/хил, остальное — no-op.</summary>
    internal sealed class MockCombatContext : ICombatContext
    {
        private readonly IRngService _rng;

        public readonly List<DamageRequest> DamageCalls = new List<DamageRequest>();
        public float TotalRawDamage;
        public float TotalHealed;

        public MockCombatContext(IRngService rng = null) => _rng = rng ?? new XorShiftRng(1UL);

        public void DealDamage(in DamageRequest req)
        {
            DamageCalls.Add(req);
            TotalRawDamage += req.RawDamage;
        }

        public void Heal(RuntimeUnit target, float amount, RuntimeUnit source) => TotalHealed += amount;
        public void SpawnProjectile(in ProjectileSpawn spawn) { }

        public int QueryUnitsInRadius(
            Vector2 center, float radius, List<RuntimeUnit> results, TargetFilter filter, int requestingTeam) => 0;

        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source) { }

        public IRngService Rng => _rng;
        public int CurrentTick => 0;
        public float ArmorK => 100f;
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
