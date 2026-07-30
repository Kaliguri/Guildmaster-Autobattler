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
            int cleanseStacksFlat = 0,
            float cleanseStacksPct = 0f,
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
            // Цена очистки лестницей: одна и та же пара на все три ступени, если тест не задаёт иного.
            var price = new EffectData.CleansePrice { Flat = cleanseStacksFlat, Pct = cleanseStacksPct };
            Set(data, "_cleansePrice", new[] { price, price, price });
            Set(data, "_components", components ?? Array.Empty<IEffectComponent>());
            return data;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo fi = Reflect.FindField(target.GetType(), field);
            if (fi == null) throw new ArgumentException($"Нет поля {field} в {target.GetType().Name} (или базах)");
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
            bool consumesTriggerTag = false,
            // Дефолт задан намеренно: тестовая способность — ВАЛИДНАЯ способность, а Undefined в игре
            // означает «автор забыл» и роняет запрос урона в консоль. Тест, которому важен другой тип,
            // передаёт его явно.
            DamageType damageType = DamageType.Slash,
            float castSeconds = 0f,
            float channelSeconds = 0f,
            float channelTickSeconds = 1f,
            bool canMoveWhileCasting = false,
            EffectData[] selfEffects = null,
            bool displaces = false,
            float displaceDistance = 4f,
            UnitData summonUnit = null,
            int summonCount = 1,
            int summonLimit = 3,
            float summonLifetimeSeconds = 0f,
            bool summonDiesWithSummoner = false,
            string id = "test.ability")
        {
            var a = new AbilityData();
            Set(a, "_id", id);
            Set(a, "_summonUnit", summonUnit);
            Set(a, "_summonCount", summonCount);
            Set(a, "_summonLimit", summonLimit);
            Set(a, "_summonLifetimeSeconds", summonLifetimeSeconds);
            Set(a, "_summonDiesWithSummoner", summonDiesWithSummoner);
            Set(a, "_selfEffects", selfEffects ?? System.Array.Empty<EffectData>());
            Set(a, "_displaces", displaces);
            Set(a, "_displaceDistance", displaceDistance);
            Set(a, "_castSeconds", castSeconds);
            Set(a, "_channelSeconds", channelSeconds);
            Set(a, "_channelTickSeconds", channelTickSeconds);
            Set(a, "_canMoveWhileCasting", canMoveWhileCasting);
            Set(a, "_damageType", damageType);
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
            FieldInfo fi = Reflect.FindField(target.GetType(), field);
            if (fi == null) throw new ArgumentException($"Нет поля {field} в {target.GetType().Name} (или базах)");
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
            DamageType autoAttackDamageType = DamageType.Slash,
            AreaShape autoAttackShape = AreaShape.None,
            float autoAttackWidth = 1f,
            float resourceOnHit = 0f,
            UnitVisual visual = null,
            AIProfile ai = null,
            EffectData[] autoAttackEffects = null,
            bool canAttackWhileMoving = false,
            float movingAttackSpeedPenaltyPct = 0.5f,
            CreatureType creatureType = CreatureType.Living,
            UnitClass combatClass = UnitClass.Bruiser,
            AttackChannel channel = default,
            float attackRecoverySeconds = 0f)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            Set(r, "_combatClass", combatClass);
            Set(r, "_creatureType", creatureType);
            Set(r, "_stats", stats ?? Array.Empty<StatModifier>());
            Set(r, "_grantedEffects", grantedEffects ?? Array.Empty<EffectData>());
            Set(r, "_abilities", abilities ?? Array.Empty<AbilityData>());
            Set(r, "_attackType", attackType);
            Set(r, "_autoAttackDamageType", autoAttackDamageType);
            Set(r, "_autoAttackShape", autoAttackShape);
            Set(r, "_autoAttackWidth", autoAttackWidth);
            Set(r, "_resourceOnHit", resourceOnHit);
            Set(r, "_visual", visual);
            Set(r, "_ai", ai ?? new AIProfile());
            Set(r, "_autoAttackEffects", autoAttackEffects ?? Array.Empty<EffectData>());
            Set(r, "_canAttackWhileMoving", canAttackWhileMoving);
            Set(r, "_movingAttackSpeedPenaltyPct", movingAttackSpeedPenaltyPct);
            Set(r, "_channel", channel);
            Set(r, "_attackRecoverySeconds", attackRecoverySeconds);
            return r;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo fi = Reflect.FindField(target.GetType(), field);
            if (fi == null) throw new ArgumentException($"Нет поля {field} в {target.GetType().Name} (или базах)");
            fi.SetValue(target, value);
        }
    }

    /// <summary>
    /// Билдер <see cref="UnitVisual"/> для тестов windup: собирает Attack-<see cref="AnimationClip"/> с
    /// заданным числом кадров (длина = frameCount/fps) и маркером контакта на hitFrame — сим выводит
    /// windup из клипа (<see cref="ClipMarkers"/>), как в проде. Спрайты не нужны: длину задаёт float-кривая.
    /// </summary>
    internal static class TestVisual
    {
        private const float Fps = 10f;

        public static UnitVisual Make(int frameCount, int hitFrame)
        {
            var v = ScriptableObject.CreateInstance<UnitVisual>();
            FieldInfo attackClip = typeof(UnitVisual).GetField("_attackClip", BindingFlags.Instance | BindingFlags.NonPublic);
            attackClip.SetValue(v, BuildAttackClip(frameCount, hitFrame));
            return v;
        }

        private static AnimationClip BuildAttackClip(int frameCount, int hitFrame)
        {
            var clip = new AnimationClip { frameRate = Fps };
            if (frameCount > 0)
            {
                // Длина клипа = frameCount/Fps → round(length*frameRate) == frameCount (см. ClipMarkers).
                clip.SetCurve("", typeof(Transform), "localPosition.x",
                    AnimationCurve.Linear(0f, 0f, frameCount / Fps, 0f));
            }
            if (hitFrame > 0)
            {
                var ev = new AnimationEvent { functionName = ClipMarkers.MarkerFunction, time = hitFrame / Fps };
                UnityEditor.AnimationUtility.SetAnimationEvents(clip, new[] { ev });
            }
            return clip;
        }
    }

    /// <summary>Юнит-фабрика для тестов эффектов.</summary>
    internal static class TestUnit
    {
        public static RuntimeUnit Make(int team = 0, float maxHp = 1000f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, maxHp) });
            return new RuntimeUnit { Team = team, Stats = stats, CurrentHP = maxHp, AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash };
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

        /// <summary>Каждый вызов <see cref="Heal"/> — кому и сколько (для проверки адресата лечения).</summary>
        public readonly List<(RuntimeUnit Target, float Amount)> Heals = new List<(RuntimeUnit, float)>();

        /// <summary>Юниты, которые вернёт <see cref="QueryUnitsInRadius"/> (фильтр по команде применяется). Пусто = запрос пустой.</summary>
        public readonly List<RuntimeUnit> UnitsInWorld = new List<RuntimeUnit>();

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

        public void Heal(RuntimeUnit target, float amount, RuntimeUnit source)
        {
            TotalHealed += amount;
            Heals.Add((target, amount));
        }
        /// <summary>Выпущенные снаряды: по ним отличается «ударил мгновенно» от «послал снаряд».</summary>
        public readonly List<ProjectileSpawn> Projectiles = new List<ProjectileSpawn>();

        public void SpawnProjectile(in ProjectileSpawn spawn) => Projectiles.Add(spawn);

        public int QueryUnitsInRadius(
            Vector2 center, float radius, List<RuntimeUnit> results, TargetFilter filter, int requestingTeam)
        {
            results.Clear();
            for (int i = 0; i < UnitsInWorld.Count; i++)
            {
                RuntimeUnit u = UnitsInWorld[i];
                if (u.IsDead) continue;
                if ((u.Position - center).sqrMagnitude > radius * radius) continue;
                bool enemy = u.Team != requestingTeam;
                if (filter == TargetFilter.Enemies && !enemy) continue;
                if (filter == TargetFilter.Allies && enemy) continue;
                results.Add(u);
            }
            return results.Count;
        }

        public int QueryUnitsInLine(
            Vector2 origin, Vector2 direction, float length, float width,
            List<RuntimeUnit> results, TargetFilter filter, int requestingTeam) => 0;

        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source)
            => _effects?.Apply(target, def, source, this);

        /// <summary>Наложение со сроком, посчитанным по ходу боя (обездвиживание холодной линии).</summary>
        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds)
            => _effects?.Apply(target, def, source, this, durationSeconds);

        public void ReportAreaHit(in AreaHit hit) { }

        public void Dispel(in DispelRequest req) => _effects?.Dispel(in req, this);
        // Каст никто не слушает: реакцию на чужое заклинание проверяют бои, а не заглушка.
        public void ReportAbilityCast(RuntimeUnit caster) { }
        /// <summary>Заявки на смещение: заглушка их не исполняет, но помнит — по ним и проверяется толчок.</summary>
        public readonly List<DisplaceRequest> Displaces = new List<DisplaceRequest>();

        public void Displace(in DisplaceRequest req) => Displaces.Add(req);

        /// <summary>Призванные тела: мок собирает их сам, чтобы срез призывов не тянул фабрику и SO.</summary>
        public readonly List<RuntimeUnit> Summons = new List<RuntimeUnit>();

        /// <summary>Кит, который мок выдаёт за призыв. null = «призывать нечем» (проверка деградации).</summary>
        public System.Func<UnitData, int, Vector2, RuntimeUnit, RuntimeUnit> SummonFactory;

        public RuntimeUnit Summon(UnitData data, int team, Vector2 position, RuntimeUnit summoner)
        {
            RuntimeUnit summon = SummonFactory?.Invoke(data, team, position, summoner);
            if (summon == null) return null;

            summon.Summoner = summoner;
            summon.Position = position;
            summon.PreviousPosition = position;
            Summons.Add(summon);
            UnitsInWorld.Add(summon);
            return summon;
        }

        // Заглушке нечего откладывать: раундов тут нет, поэтому переход отыгрывается сразу.
        public void TeleportBehind(RuntimeUnit unit, RuntimeUnit target)
            => CombatPositioning.TeleportBehind(unit, target);

        public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) => AttackStarted++;

        /// <summary>Сколько раз замах прерывался: по нему отличается «удар сорван» от «удар доигран».</summary>
        public int AttackInterrupted;
        public int AttackStarted;

        public void NotifyAttackInterrupted(RuntimeUnit unit) => AttackInterrupted++;

        public IRngService Rng => _rng;
        /// <summary>
        /// Тик боя. Подвижный, потому что снятие эффектов судит по состоянию НАЧАЛА тика: тест, который
        /// кладёт эффект и снимает его при том же значении, выражает не игру, а гонку обхода — ровно то,
        /// что запрещено. Двигать через <see cref="AdvanceTick"/>.
        /// </summary>
        public int CurrentTick { get; private set; }

        /// <summary>
        /// Перейти на следующий тик: всё наложенное до этого становится «висевшим ранее». Переданным
        /// юнитам проявляется отложенное — статы, маска тегов, стаки — ровно как это делает
        /// <c>CombatSimulation.Tick</c> в конце тика. Без юнитов двигается только счётчик.
        /// </summary>
        public void AdvanceTick(params RuntimeUnit[] units)
        {
            CurrentTick++;
            if (units == null) return;
            for (int i = 0; i < units.Length; i++) EffectSystem.CommitPending(units[i]);
        }
        public float ArmorK => 100f;
        public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;
    }

    /// <summary>Установка приватных <c>[SerializeField]</c> компонентов в тестах (без сериализации).</summary>
    internal static class Reflect
    {
        public static T With<T>(this T obj, string field, object value)
        {
            FieldInfo fi = FindField(obj.GetType(), field);
            if (fi == null) throw new ArgumentException($"Нет поля {field} в {obj.GetType().Name} (или базах)");
            fi.SetValue(obj, value);
            return obj;
        }

        /// <summary>
        /// Найти приватное/публичное instance-поле по всей иерархии типа. <c>Type.GetField</c> не видит
        /// приватные поля БАЗ — а поля контента переезжают в базы (ContentDefinition._id, UnitData-кит Ф4).
        /// </summary>
        public static FieldInfo FindField(Type type, string field)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo fi = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null) return fi;
            }
            return null;
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
