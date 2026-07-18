using System.Collections.Generic;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Тик способностей: убывание кулдаунов и детерминированный плейсхолдер-каст готовых активок
    /// (полный выбор «когда/что кастовать» — AI Фазы 3, вики «12» §1, §9). Каст списывает ресурс,
    /// ставит кулдаун (× CooldownEff) и накладывает эффекты способности на цель.
    /// </summary>
    public sealed class AbilitySystem
    {
        // Переиспользуемый буфер для радиус-запросов (условие каста / AOE-цели) — без аллокаций.
        private readonly List<RuntimeUnit> _targets = new List<RuntimeUnit>();

        /// <summary>Успешный каст активки кастующим (презентация-сигнал для звука/VFX; симуляцию не трогает).</summary>
        public event System.Action<RuntimeUnit> OnAbilityCast;

        public void Tick(IReadOnlyList<RuntimeUnit> units, ICombatContext ctx, float dt)
        {
            for (int u = 0; u < units.Count; u++)
            {
                RuntimeUnit unit = units[u];
                if (unit.IsDead || unit.Abilities.Count == 0) continue;

                for (int a = 0; a < unit.Abilities.Count; a++)
                {
                    AbilityRuntime ability = unit.Abilities[a];
                    if (ability.CooldownRemaining > 0f) ability.CooldownRemaining -= dt;
                }

                // Плейсхолдер-триггер: кастуем первую готовую активку, если можем (в полёте — нет, §9.9).
                if (unit.CanAct && unit.CanCast && unit.DisplacedTicksRemaining == 0)
                {
                    for (int a = 0; a < unit.Abilities.Count; a++)
                    {
                        if (TryCast(unit, a, units, ctx)) break; // одна способность за тик
                    }
                }
            }
        }

        /// <summary>
        /// Попытаться скастовать способность <paramref name="abilityIndex"/>. Возвращает false, если
        /// не готова / не хватает ресурса / нет валидной цели.
        /// </summary>
        public bool TryCast(RuntimeUnit caster, int abilityIndex, IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            if (caster == null || abilityIndex < 0 || abilityIndex >= caster.Abilities.Count) return false;

            AbilityRuntime ability = caster.Abilities[abilityIndex];
            AbilityData data = ability.Data;
            if (data == null || !ability.IsReady) return false;
            if (caster.CurrentResource < data.ResourceCost) return false;

            // Блок E (паника): при своём низком HP лечащая способность разворачивается на самого
            // кастующего (лечит себя); урон-способность просто кастуется независимо от условия.
            bool panicSelf = data.CastOverrideSelfHpPct > 0f && HpPct(caster) <= data.CastOverrideSelfHpPct;

            bool isMassTag = data.TargetMode == AbilityTargetMode.AllEnemiesWithTag;
            bool isAllyAura = data.TargetMode == AbilityTargetMode.AlliesInRadius;

            // Круговой удар и масс-по-тегу одиночной цели не требуют (центр = кастующий / список).
            RuntimeUnit target = (panicSelf && data.IsHeal) ? caster
                               : isMassTag || isAllyAura    ? null
                               : ResolveTarget(caster, data.TargetMode, units);

            // Требование валидной цели: Circle — центр = кастующий; масс-по-тегу — нужен хотя бы один
            // тегнутый враг (даже под панику масс-стан в пустоту не жжёт КД/ману); иначе — одиночная цель.
            if (isMassTag)
            {
                if (CountEnemiesWithTag(caster, data.TriggerTag, units) == 0) return false;
            }
            else if (isAllyAura)
            {
                // Аура по союзникам всегда валидна: кастующий сам себе союзник.
            }
            else if (data.AreaShape != AreaShape.Circle && target == null)
            {
                return false;
            }

            // Гейт условия каста (блок D): дешёвое решение «кастовать ли» — здесь, не в мозге.
            // Паника (блок E) кастует независимо от условия.
            if (!panicSelf && !CastConditionMet(caster, target, data, ctx, units)) return false;

            caster.CurrentResource -= data.ResourceCost;
            ability.CooldownRemaining = data.BaseCooldown * caster.Stats.Get(StatType.CooldownEff);

            if (data.Displaces)
                ApplyDisplace(caster, target, data, ctx);
            else if (isAllyAura)
                ApplyAllyAura(caster, data, ctx);
            else if (isMassTag)
                ApplyAllWithTag(caster, data, units, ctx);
            else if (data.AreaShape == AreaShape.Circle)
                ApplyCircle(caster, data, ctx);
            else
                ApplyToTarget(caster, target, data, ctx);

            OnAbilityCast?.Invoke(caster); // презентация-сигнал «каст состоялся»
            return true;
        }

        /// <summary>
        /// «Шквальный толчок» (§10.6) — фазы 1–2: РЫВОК монаха вплотную к цели + ФИКСАЦИЯ цели оцепенением.
        /// Смещаем САМОГО кастующего (self-displacement) к точке рядом с целью; эффекты активки (<c>_effects</c>
        /// = оцепенение) вешаем на цель, чтобы за рывок она не уползла. Фазы 3–4 (отбрасывание → телепорт)
        /// поднимаются реактивами: приземление рывка (<c>WhirlDashLandingComponent</c>) → отбрасывание, конец
        /// отбрасывания (<c>VortexEntryComponent</c>) → телепорт в спину. Гейт по дальности — CastCondition.
        /// </summary>
        private static void ApplyDisplace(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx)
        {
            // Запоминаем цель захода: приземление рывка оттолкнёт ИМЕННО её (позицию считаем под неё),
            // а не «ближайшего» — тот мог разъехаться, пока монах облетал.
            caster.PendingEngageTarget = target;

            float adjacency = caster.Stats.Get(StatType.AttackRange) * 0.5f;

            // Монах бьёт ТОЛЬКО прямо от себя, поэтому позицию рывка выбираем так, чтобы линия
            // «монах → цель» смотрела в ближайшего ДРУГОГО врага («наковальню») — тогда толчок отправит
            // цель в него (и «ядро» пройдёт сквозь). Враг один — просто заходим со своей стороны.
            RuntimeUnit anvil = NearestEnemyTo(target.Position, caster.Team, target, ctx);
            Vector2 throwDir = anvil != null
                ? anvil.Position - target.Position
                : target.Position - caster.Position;
            throwDir = throwDir.sqrMagnitude > 1e-6f ? throwDir.normalized : Vector2.right;

            // Приземляемся на сторону цели, ПРОТИВОПОЛОЖНУЮ наковальне (вплотную) → monk→target == throwDir.
            Vector2 dashDest = target.Position - throwDir * adjacency;
            Vector2 toDest   = dashDest - caster.Position;
            float   dashDist = toDest.magnitude;
            Vector2 dashDir  = dashDist > 1e-4f ? toDest / dashDist : Vector2.right;

            // Фиксация цели: оцепенение (эффекты активки) — цель стоит, пока монах облетает её.
            ApplyEffects(target, data, caster, ctx);

            // Dev-оверлей линии рывка (fire-and-forget).
            ctx.ReportAreaHit(AreaHit.Line(caster.Position, dashDir, dashDist, 0.4f, caster.Team));

            // Рывок = смещение самого кастующего, без «ядра». Приземление (EffectExpired на себе) поднимет отбрасывание.
            ctx.Displace(new DisplaceRequest(
                caster, caster, dashDir, dashDist, data.DisplaceTicks,
                cannonball: false, damage: 0f, school: DamageSchool.Physical, width: 0f));
        }

        /// <summary>Ближайший к точке живой враг команды <paramref name="selfTeam"/>, кроме <paramref name="exclude"/> (тай-брейк по Id).</summary>
        private static RuntimeUnit NearestEnemyTo(Vector2 from, int selfTeam, RuntimeUnit exclude, ICombatContext ctx)
        {
            var buffer = new List<RuntimeUnit>();
            ctx.QueryUnitsInRadius(from, ctx.Tuning.GlobalSearchRadius, buffer, TargetFilter.Enemies, selfTeam);

            RuntimeUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < buffer.Count; i++)
            {
                RuntimeUnit o = buffer[i];
                if (o.IsDead || ReferenceEquals(o, exclude)) continue;
                float sq = (o.Position - from).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && (best == null || o.Id < best.Id)))
                {
                    bestSq = sq;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Условие каста (блок D). Отмена по своему HP% (блок E) решается в <see cref="TryCast"/> до вызова.</summary>
        private bool CastConditionMet(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx, IReadOnlyList<RuntimeUnit> units)
        {
            switch (data.CastCondition)
            {
                case CastCondition.EnemiesInRadius:
                    ctx.QueryUnitsInRadius(caster.Position, data.CastConditionRadius, _targets, TargetFilter.Enemies, caster.Team);
                    return _targets.Count >= data.CastConditionCount;

                case CastCondition.AllyTargetHpBelowPct:
                    // Спасаем раненого союзника: кастуем, только если выбранная цель просела до порога.
                    return target != null && HpPct(target) <= data.CastConditionHpPct;

                case CastCondition.EnemiesWithTagCount:
                    // Криомант: кастуем масс-стан, когда замороженных врагов накопилось ≥ X (глобально).
                    return CountEnemiesWithTag(caster, data.TriggerTag, units) >= data.CastConditionCount;

                case CastCondition.Immediately:
                default:
                    return true;
            }
        }

        /// <summary>Число живых врагов кастующего, несущих <paramref name="tag"/> (по маске активных эффектов). Глобально, без дальности (§9.10).</summary>
        private static int CountEnemiesWithTag(RuntimeUnit caster, EffectTag tag, IReadOnlyList<RuntimeUnit> units)
        {
            if (tag == EffectTag.None) return 0;
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead || u.Team == caster.Team) continue;
                if ((u.EffectTagMask & tag) != 0) count++;
            }
            return count;
        }

        /// <summary>
        /// Масс-каст «Ледяные оковы» (§9.10): наложить эффекты активки на всех живых врагов с
        /// <see cref="AbilityData.TriggerTag"/> (глобально), затем — при <see cref="AbilityData.ConsumesTriggerTag"/> —
        /// снять этот тег (конверсия «Заморозки» в стан). Обход по индексу списка — детерминизм.
        /// </summary>
        private void ApplyAllWithTag(RuntimeUnit caster, AbilityData data, IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            EffectTag tag = data.TriggerTag;
            float dmg = AbilityDamage(caster, data);
            DamageSchool school = DamageCategories.Resolve(data.SchoolOverride, caster.DamageSchool);
            DamageAffinity affinity = DamageCategories.Resolve(data.AffinityOverride, caster.Affinity);

            // «Взрыв спор» Друида: помимо урона лечит союзников вокруг КАЖДОЙ детонированной цели за каждый
            // уникальный эффект-триггер на ней. Гейт по IsHeal+радиусу — у крио-«Оков» хила нет, они не лечат.
            bool healsPerUnique = data.IsHeal && data.AreaRadius > 0f;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead || u.Team == caster.Team) continue;
                if ((u.EffectTagMask & tag) == 0) continue;

                // Детонация: урон по каждому тегнутому врагу («Взрыв спор», «Воспламенение»). 0 = только эффекты (крио).
                if (dmg > 0f) ctx.DealDamage(new DamageRequest(caster, u, dmg, school, ctx.ArmorK, affinity: affinity));

                ApplyEffects(u, data, caster, ctx);

                // Хил за уникальные яды считаем ДО расхода тега (иначе Dispel их снимет и уники обнулятся).
                if (healsPerUnique)
                {
                    int uniques = CountUniqueTagged(u, tag);
                    if (uniques > 0) HealAlliesAround(caster, u, data, uniques, ctx);
                }

                // Конверсия: снять тег-триггер (напр. Frozen) после наложения стана — «Заморозка» превращается в стан.
                if (data.ConsumesTriggerTag)
                    ctx.Dispel(new DispelRequest(u, DispelTargetPolarity.Any, tag, dispelPower: int.MaxValue, maxCount: 0));
            }
        }

        /// <summary>Сколько РАЗНЫХ эффектов (по <c>Def</c>) с данным тегом висит на юните. Стаки одного эффекта = 1.</summary>
        private static int CountUniqueTagged(RuntimeUnit unit, EffectTag tag)
        {
            int count = 0;
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
            {
                RuntimeEffect e = unit.ActiveEffects[i];
                if (e.Def == null || (e.Def.Tags & tag) == 0) continue;

                // Дубли по Def не считаем: ищем этот Def среди уже пройденных.
                bool seen = false;
                for (int j = 0; j < i; j++)
                {
                    RuntimeEffect prev = unit.ActiveEffects[j];
                    if (prev.Def == e.Def && (prev.Def.Tags & tag) != 0) { seen = true; break; }
                }
                if (!seen) count++;
            }
            return count;
        }

        /// <summary>
        /// Лечит союзников кастующего в радиусе вокруг <paramref name="epicenter"/> (детонированного врага),
        /// умножая лечение на число уникальных ядов на нём — «Взрыв спор» лечит тем сильнее, чем разнообразнее
        /// отравлена цель. Хил каждому союзнику стакается от каждой взорванной рядом цели (внешний цикл).
        /// </summary>
        private void HealAlliesAround(RuntimeUnit caster, RuntimeUnit epicenter, AbilityData data, int multiplier, ICombatContext ctx)
        {
            ctx.QueryUnitsInRadius(epicenter.Position, data.AreaRadius, _targets, TargetFilter.Allies, caster.Team);
            for (int i = 0; i < _targets.Count; i++)
            {
                RuntimeUnit ally = _targets[i];
                if (ally.IsDead) continue;

                float heal = HealAmount(ally, data) * multiplier;
                if (heal > 0f) ctx.Heal(ally, heal, caster);
            }
        }

        /// <summary>
        /// Групповой баф/хил по союзникам в радиусе («Командный клич» гоблин-командира). Кастующий входит в
        /// список сам — клич бафает и его. Урон здесь не наносится (это опора поддержки, не AOE-удар).
        /// </summary>
        private void ApplyAllyAura(RuntimeUnit caster, AbilityData data, ICombatContext ctx)
        {
            ctx.ReportAreaHit(AreaHit.Circle(caster.Position, data.AreaRadius, caster.Team));
            ctx.QueryUnitsInRadius(caster.Position, data.AreaRadius, _targets, TargetFilter.Allies, caster.Team);

            bool casterIncluded = false;
            for (int i = 0; i < _targets.Count; i++)
            {
                RuntimeUnit t = _targets[i];
                if (t == caster) casterIncluded = true;
                ApplyAura(t, data, caster, ctx);
            }

            if (!casterIncluded) ApplyAura(caster, data, caster, ctx);
        }

        private static void ApplyAura(RuntimeUnit t, AbilityData data, RuntimeUnit caster, ICombatContext ctx)
        {
            if (t.IsDead) return;
            if (data.IsHeal) ctx.Heal(t, HealAmount(t, data), caster);
            ApplyEffects(t, data, caster, ctx);
        }

        /// <summary>Круговой AOE-удар вокруг кастующего («Стальной вихрь»): урон + эффекты по всем врагам в радиусе.</summary>
        private void ApplyCircle(RuntimeUnit caster, AbilityData data, ICombatContext ctx)
        {
            // Dev-оверлей зоны круга.
            ctx.ReportAreaHit(AreaHit.Circle(caster.Position, data.AreaRadius, caster.Team));

            ctx.QueryUnitsInRadius(caster.Position, data.AreaRadius, _targets, TargetFilter.Enemies, caster.Team);

            float dmg = AbilityDamage(caster, data);
            DamageSchool school = DamageCategories.Resolve(data.SchoolOverride, caster.DamageSchool);
            DamageAffinity affinity = DamageCategories.Resolve(data.AffinityOverride, caster.Affinity);

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итог.
            for (int i = 0; i < _targets.Count; i++)
            {
                RuntimeUnit t = _targets[i];
                if (dmg > 0f) ctx.DealDamage(new DamageRequest(caster, t, dmg, school, ctx.ArmorK, affinity: affinity));
                ApplyEffects(t, data, caster, ctx);
            }
        }

        /// <summary>Одиночный каст: хил-нагрузка (Пастырь) ИЛИ прямой урон ×AutoAttackDamage (поведение Ф2) + эффекты.</summary>
        private static void ApplyToTarget(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx)
        {
            if (data.IsHeal)
            {
                // Сырое лечение (dealt/taken eff и кламп к MaxHP применяет ctx.Heal). «Длань жизни» = X + недостающее HP.
                ctx.Heal(target, HealAmount(target, data), caster);
            }
            else
            {
                float dmg = AbilityDamage(caster, data);
                if (dmg > 0f)
                {
                    DamageSchool school = DamageCategories.Resolve(data.SchoolOverride, caster.DamageSchool);
                    DamageAffinity affinity = DamageCategories.Resolve(data.AffinityOverride, caster.Affinity);
                    ctx.DealDamage(new DamageRequest(caster, target, dmg, school, ctx.ArmorK, affinity: affinity));
                }
            }
            ApplyEffects(target, data, caster, ctx);
        }

        /// <summary>Сырое лечение способности = HealFlat + HealPctTargetMissingHp × недостающее HP цели.</summary>
        private static float HealAmount(RuntimeUnit target, AbilityData data)
        {
            float missing = target.Stats.Get(StatType.MaxHP) - target.CurrentHP;
            if (missing < 0f) missing = 0f;
            return data.HealFlat + data.HealPctTargetMissingHp * missing;
        }

        /// <summary>Прямой урон способности = DamageMultiplier × AutoAttackDamage кастующего (0 = только эффекты).</summary>
        private static float AbilityDamage(RuntimeUnit caster, AbilityData data)
        {
            return data.DamageMultiplier > 0f
                ? data.DamageMultiplier * caster.Stats.Get(StatType.AutoAttackDamage)
                : 0f;
        }

        private static void ApplyEffects(RuntimeUnit target, AbilityData data, RuntimeUnit caster, ICombatContext ctx)
        {
            EffectData[] effects = data.Effects;
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
                ctx.ApplyEffect(target, effects[i], caster);
        }

        private static float HpPct(RuntimeUnit u)
        {
            float maxHp = u.Stats.Get(StatType.MaxHP);
            return maxHp > 0f ? u.CurrentHP / maxHp : u.CurrentHP;
        }

        private static RuntimeUnit ResolveTarget(RuntimeUnit caster, AbilityTargetMode mode, IReadOnlyList<RuntimeUnit> units)
        {
            switch (mode)
            {
                case AbilityTargetMode.Self:
                    return caster;

                case AbilityTargetMode.NearestEnemy:
                    return caster.CurrentTarget != null && !caster.CurrentTarget.IsDead ? caster.CurrentTarget : null;

                case AbilityTargetMode.NearestAlly:
                    return NearestAlly(caster, units);

                case AbilityTargetMode.LowestHpAlly:
                    return LowestHpAlly(caster, units);

                default:
                    return null;
            }
        }

        private static RuntimeUnit NearestAlly(RuntimeUnit caster, IReadOnlyList<RuntimeUnit> units)
        {
            RuntimeUnit best = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit other = units[i];
                if (other == caster || other.IsDead || other.Team != caster.Team) continue;

                float sq = (other.Position - caster.Position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = other;
                }
            }

            return best;
        }

        /// <summary>
        /// Союзник с наименьшим HP% — глобально, без ограничения дальности (хилер-ульта «Длань жизни»).
        /// Себя исключаем: свой критический HP покрывает блок E. Тай-брейк — дистанция, затем Id (детерминизм).
        /// </summary>
        private static RuntimeUnit LowestHpAlly(RuntimeUnit caster, IReadOnlyList<RuntimeUnit> units)
        {
            RuntimeUnit best      = null;
            float       bestPct   = float.MaxValue;
            float       bestDistSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit other = units[i];
                if (other == caster || other.IsDead || other.Team != caster.Team) continue;

                float pct    = HpPct(other);
                float distSq = (other.Position - caster.Position).sqrMagnitude;

                bool better =
                    best == null
                    || pct < bestPct
                    || (pct == bestPct && distSq < bestDistSq)
                    || (pct == bestPct && distSq == bestDistSq && other.Id < best.Id);

                if (better) { best = other; bestPct = pct; bestDistSq = distSq; }
            }

            return best;
        }
    }
}
