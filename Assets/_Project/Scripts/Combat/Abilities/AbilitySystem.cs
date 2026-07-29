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

        // Касты, решённые за этот тик — применяются ПОСЛЕ того, как решения приняты всеми (см. Tick).
        private readonly List<PlannedCast> _planned = new List<PlannedCast>();

        /// <summary>Что именно применяется в фазе применения тика.</summary>
        private enum PlanKind
        {
            /// <summary>Заявка на каст: списать цену и либо применить сразу, либо взвести подготовку/канал.</summary>
            Begin = 0,

            /// <summary>Нагрузка идущего каста: подготовка дошла до конца или сработал период канала.</summary>
            Payload = 1,
        }

        /// <summary>Решённый, но ещё не применённый каст: кто, чем и по кому бьёт. Цель выбрана по состоянию
        /// начала тика — в том числе и разворот лечения на себя по панике (блок E).</summary>
        private readonly struct PlannedCast
        {
            public readonly RuntimeUnit Caster;
            public readonly AbilityRuntime Ability;
            public readonly RuntimeUnit Target;
            public readonly int AbilityIndex;
            public readonly PlanKind Kind;

            public PlannedCast(
                RuntimeUnit caster, AbilityRuntime ability, RuntimeUnit target,
                int abilityIndex, PlanKind kind)
            {
                Caster       = caster;
                Ability      = ability;
                Target       = target;
                AbilityIndex = abilityIndex;
                Kind         = kind;
            }
        }

        /// <summary>Успешный каст активки кастующим (презентация-сигнал для звука/VFX; симуляцию не трогает).</summary>
        public event System.Action<RuntimeUnit> OnAbilityCast;

        /// <summary>
        /// Начата подготовка или канал (<see cref="AbilityData.TakesTime"/>): кастующий и длительность
        /// подготовки в тиках. Мгновенные способности этого события не дают — у них подводить нечего.
        /// </summary>
        public event System.Action<RuntimeUnit, AbilityData, int> OnAbilityCastStarted;

        /// <summary>Каст или канал оборван, не доиграв: контроль, полёт, смерть или потеря цели.</summary>
        public event System.Action<RuntimeUnit> OnAbilityCastInterrupted;

        /// <summary>Тик способностей: кулдауны, решения о кастах, затем сами касты.</summary>
        /// <remarks>
        /// РЕШЕНИЯ ОТДЕЛЕНЫ ОТ ПРИМЕНЕНИЯ, и это принципиально. Пока каст применялся сразу по ходу обхода,
        /// наложенный им контроль тут же лишал права каста всех, кто стоит в списке позже, — а список у
        /// зеркальных сторон обратный. Два готовых в один тик криоманта решали исход тем, кто заспавнен
        /// первым: левый вешал «Оковы», правый ловил стан и терял свой каст навсегда (пойман зондом на
        /// тике 240 — у левых «Заморозка», у правых уже стан от чужого криоманта). Теперь оба решают по
        /// состоянию НАЧАЛА тика и оба кастуют: одновременная готовность разрешается одновременно,
        /// а не по месту в списке. Тот же приём, что в <c>MovementSystem</c> и <c>SeparationSystem</c>.
        /// </remarks>
        public void Tick(IReadOnlyList<RuntimeUnit> units, ICombatContext ctx, float dt)
        {
            _planned.Clear();

            // --- Проход 1: кулдауны и решения. Мир не меняется. ---
            for (int u = 0; u < units.Count; u++)
            {
                RuntimeUnit unit = units[u];

                // Смерть обрывает каст молча: показывать прерывание трупу нечего, а состояние обязано
                // уйти — иначе dev-рестарт поднимет юнита с чужим кастом на счётчике.
                if (unit.IsDead)
                {
                    if (unit.IsCastBusy) ClearCast(unit);
                    continue;
                }

                if (unit.Abilities.Count == 0) continue;

                for (int a = 0; a < unit.Abilities.Count; a++)
                {
                    AbilityRuntime ability = unit.Abilities[a];
                    if (ability.CooldownRemaining > 0f) ability.CooldownRemaining -= dt;
                }

                // Идёт подготовка или канал — юнит занят: продвигаем его каст, новый начать нельзя.
                if (unit.IsCastBusy)
                {
                    AdvanceCast(unit);
                    continue;
                }

                // Плейсхолдер-триггер: кастуем первую готовую активку, если можем (в полёте — нет, §9.9).
                if (unit.CanAct && unit.CanCast && unit.DisplacedTicksRemaining == 0)
                {
                    for (int a = 0; a < unit.Abilities.Count; a++)
                    {
                        if (!TryPlan(unit, a, units, ctx, out PlannedCast plan)) continue;
                        _planned.Add(plan);
                        break;  // одна способность за тик
                    }
                }
            }

            // --- Проход 2: применение. Только здесь мир меняется. ---
            for (int i = 0; i < _planned.Count; i++) Execute(_planned[i], units, ctx);
        }

        /// <summary>
        /// Попытаться скастовать способность <paramref name="abilityIndex"/> НЕМЕДЛЕННО (решение + применение
        /// одним вызовом). Возвращает false, если не готова / не хватает ресурса / нет валидной цели.
        /// Внутри тика так не кастуют — там решения собираются со всех и применяются после (см. <see cref="Tick"/>);
        /// этот вход остаётся для прямого каста из тестов и dev-команд.
        /// <para>У способности с подготовкой (<see cref="AbilityData.TakesTime"/>) <c>true</c> означает
        /// «каст НАЧАТ», а не «нагрузка применена»: цена списана, счётчик взведён, а сама нагрузка придёт
        /// через свои тики. Проверять результат такого каста нужно после прогона тиков.</para>
        /// </summary>
        public bool TryCast(RuntimeUnit caster, int abilityIndex, IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            if (!TryPlan(caster, abilityIndex, units, ctx, out PlannedCast plan)) return false;
            Execute(plan, units, ctx);
            return true;
        }

        /// <summary>
        /// Продвинуть идущий каст на тик: прерывание, подготовка, срабатывания канала. Сюда попадает только
        /// занятый кастом юнит; сам каст мир не меняет — нагрузка уезжает в фазу применения (<see cref="_planned"/>).
        /// </summary>
        /// <remarks>
        /// Прерывает ровно то, что решил Макс (Q10, 2026-07-29): контроль, полностью выводящий из строя
        /// (<c>CanAct == false</c> — оглушение, сон). Замедление, корень и урон каст НЕ рвут. Полёт от
        /// отбрасывания рвёт тоже: он и начать каст не даёт (§9.9), значит не может и дать его завершить.
        /// Немота (<c>CanCast</c>) запрещает НАЧАТЬ каст, но начатый не обрывает — это прямое следствие
        /// формулировки Q10, а не отдельное решение.
        /// </remarks>
        private void AdvanceCast(RuntimeUnit unit)
        {
            AbilityRuntime ability = unit.Abilities[unit.CastingAbilityIndex];
            AbilityData data = ability.Data;

            if (!unit.CanAct || unit.DisplacedTicksRemaining > 0 || data == null)
            {
                InterruptCast(unit);
                return;
            }

            // Подготовка: тикает до нуля, на нуле — нагрузка (и, если способность канальная, старт канала).
            if (unit.CastRemaining > 0)
            {
                unit.CastRemaining--;
                if (unit.CastRemaining > 0) return;

                PlanPayload(unit, ability);
                StartChannelOrClear(unit, data);
                return;
            }

            // Канал: срабатывает периодом. Тик, на котором канал кончается, нагрузки НЕ даёт — иначе канал
            // в 3 с с периодом в 1 с выдавал бы четыре срабатывания вместо трёх (первое идёт на старте).
            if (unit.ChannelTickRemaining > 0) unit.ChannelTickRemaining--;
            if (unit.ChannelRemaining > 0) unit.ChannelRemaining--;

            bool ended = unit.ChannelRemaining <= 0;
            if (!ended && unit.ChannelTickRemaining <= 0)
            {
                PlanPayload(unit, ability);
                unit.ChannelTickRemaining = AttackTiming.RecoveryTicks(data.ChannelTickSeconds);
            }

            if (ended) ClearCast(unit);
        }

        // Нагрузка идущего каста заезжает в фазу применения. Цель берётся ЗАНОВО, если та, на кого
        // начинали, уже мертва (решение Макса): долгий каст не должен уходить в труп, но и «умного»
        // перенаведения на лучшую цель здесь нет — только замена выбывшей.
        private void PlanPayload(RuntimeUnit unit, AbilityRuntime ability)
        {
            _planned.Add(new PlannedCast(
                unit, ability, unit.CastTarget, unit.CastingAbilityIndex, PlanKind.Payload));
        }

        // Подготовка позади: либо начинается канал, либо каст закончен. Один владелец перехода на оба
        // входа — старт без подготовки (BeginCast) и конец подготовки (AdvanceCast).
        private static void StartChannelOrClear(RuntimeUnit unit, AbilityData data)
        {
            int channelTicks = AttackTiming.RecoveryTicks(data.ChannelSeconds);
            if (channelTicks <= 0)
            {
                ClearCast(unit);
                return;
            }

            unit.ChannelTicks         = channelTicks;
            unit.ChannelRemaining     = channelTicks;
            unit.ChannelTickRemaining = AttackTiming.RecoveryTicks(data.ChannelTickSeconds);
        }

        /// <summary>Оборвать каст, не доиграв: цена уже уплачена и НЕ возвращается (решение Макса по Q10).</summary>
        private void InterruptCast(RuntimeUnit unit)
        {
            ClearCast(unit);
            OnAbilityCastInterrupted?.Invoke(unit);
        }

        /// <summary>Снять состояние каста. Единственный писатель этих полей — эта система.</summary>
        private static void ClearCast(RuntimeUnit unit)
        {
            unit.CastingAbilityIndex  = -1;
            unit.CastRemaining        = 0;
            unit.CastTicks            = 0;
            unit.ChannelRemaining     = 0;
            unit.ChannelTicks         = 0;
            unit.ChannelTickRemaining = 0;
            unit.CastTarget           = null;
        }

        /// <summary>
        /// Решить, кастуется ли способность, и по кому — БЕЗ единой мутации мира. Всё, что меняет состояние
        /// (расход ресурса, кулдаун, эффекты, урон), делает <see cref="Execute"/>.
        /// </summary>
        private bool TryPlan(RuntimeUnit caster, int abilityIndex, IReadOnlyList<RuntimeUnit> units,
            ICombatContext ctx, out PlannedCast plan)
        {
            plan = default;
            if (caster == null || abilityIndex < 0 || abilityIndex >= caster.Abilities.Count) return false;

            AbilityRuntime ability = caster.Abilities[abilityIndex];
            AbilityData data = ability.Data;
            if (data == null || !ability.IsReady) return false;
            if (caster.CurrentResource < data.ResourceCost) return false;

            // Рекаст авто-атаки (M18): умение-удар вклинивается в ритм атак, но занесённый замах
            // ДОИГРЫВАЕТ (решение Макса по Q8) — удар без замаха читается как пропущенный кадр. Хвост
            // после удара (Recovery) умение перебивает: в этом и весь выигрыш рекаста.
            if (data.DamageMultiplier > 0f && caster.Phase == AttackPhase.Windup) return false;

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

            plan = new PlannedCast(caster, ability, target, abilityIndex, PlanKind.Begin);
            return true;
        }

        /// <summary>
        /// Применить решённый план. Заявка (<see cref="PlanKind.Begin"/>) платит цену и либо применяет
        /// нагрузку в тот же тик (мгновенная способность), либо взводит подготовку/канал. Нагрузка идущего
        /// каста (<see cref="PlanKind.Payload"/>) цену не платит — она уже уплачена на старте.
        /// </summary>
        private void Execute(in PlannedCast plan, IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            RuntimeUnit caster = plan.Caster;
            // Между решением и применением кастующего могли добить (урон в этом же тике).
            if (caster.IsDead) return;

            if (plan.Kind == PlanKind.Payload)
            {
                ApplyPayload(caster, plan.Ability, plan.Target, units, ctx);
                return;
            }

            AbilityRuntime ability = plan.Ability;
            AbilityData data       = ability.Data;

            caster.CurrentResource -= data.ResourceCost;
            ability.CooldownRemaining = data.BaseCooldown * caster.Stats.Get(StatType.CooldownEff);

            if (!data.TakesTime)
            {
                ApplyPayload(caster, ability, plan.Target, units, ctx);
                return;
            }

            // Способность занимает время: взводим подготовку. Хвост предыдущей авто-атаки при этом
            // перебивается — умение вклинивается в ритм, а не ждёт его (M18).
            if (caster.Phase == AttackPhase.Recovery)
            {
                caster.Phase = AttackPhase.Idle;
                caster.RecoveryRemaining = 0;
            }

            int castTicks = AttackTiming.RecoveryTicks(data.CastSeconds);
            caster.CastingAbilityIndex = plan.AbilityIndex;
            caster.CastTarget          = plan.Target;
            caster.CastTicks           = castTicks;
            caster.CastRemaining       = castTicks;

            OnAbilityCastStarted?.Invoke(caster, data, castTicks);

            // Подготовки нет, но есть канал: первое срабатывание идёт в этот же тик, дальше — периодом.
            if (castTicks <= 0)
            {
                StartChannelOrClear(caster, data);
                ApplyPayload(caster, ability, plan.Target, units, ctx);
            }
        }

        /// <summary>
        /// Нагрузка способности: урон, лечение, эффекты — по её форме. Одно место на все три входа
        /// (мгновенный каст, конец подготовки, срабатывание канала), поэтому канал не может «случайно»
        /// отличаться от разового применения.
        /// </summary>
        private void ApplyPayload(
            RuntimeUnit caster, AbilityRuntime ability, RuntimeUnit target,
            IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            AbilityData data = ability.Data;

            bool isMassTag  = data.TargetMode == AbilityTargetMode.AllEnemiesWithTag;
            bool isAllyAura = data.TargetMode == AbilityTargetMode.AlliesInRadius;

            // Цель могла выбыть за время подготовки: берём новую тем же TargetMode (решение Макса).
            // Не нашли — нагрузка не уходит в пустоту, каст обрывается; цена остаётся уплаченной.
            bool needsTarget = !isMassTag && !isAllyAura && data.AreaShape != AreaShape.Circle;
            if (needsTarget && (target == null || target.IsDead))
            {
                target = ResolveTarget(caster, data.TargetMode, units);
                if (target == null)
                {
                    if (caster.IsCastBusy) InterruptCast(caster);
                    return;
                }
                caster.CastTarget = target;
            }

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

            // Рекаст авто-атаки (M18): умение-УДАР сбрасывает таймер обычной атаки, и она выходит сразу
            // по готовности — в окне получаются два удара почти подряд. Обнуление ПОСЛЕ удара умением
            // (решение Макса по Q8). Канал сюда не попадает намеренно: сброс на каждом его срабатывании
            // держал бы авто-атаку взведённой всё время канала — это уже другая механика.
            if (data.DamageMultiplier > 0f && data.ChannelSeconds <= 0f)
                caster.AttackCooldownTicks = 0;

            OnAbilityCast?.Invoke(caster); // презентация-сигнал «каст состоялся»
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
                caster, caster, dashDir, dashDist,
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
        /// <remarks>
        /// Нагрузка бывает двух видов, и они не исключают друг друга: мгновенное восстановление HP и
        /// <see cref="AbilityData.HealEffect"/> — эффект-HoT, где множитель превращается в СТАКИ. Стаки
        /// набиваются повторным наложением, а не отдельным API: <c>StackRule.Stack</c> для этого и есть,
        /// и так же ведёт себя любой другой источник стаков в бою (решение по Друиду 2026-07-28).
        /// </remarks>
        private void HealAlliesAround(RuntimeUnit caster, RuntimeUnit epicenter, AbilityData data, int multiplier, ICombatContext ctx)
        {
            ctx.QueryUnitsInRadius(epicenter.Position, data.AreaRadius, _targets, TargetFilter.Allies, caster.Team);
            for (int i = 0; i < _targets.Count; i++)
            {
                RuntimeUnit ally = _targets[i];
                if (ally.IsDead) continue;

                float heal = HealAmount(ally, data, caster) * multiplier;
                if (heal > 0f) ctx.Heal(ally, heal, caster);

                if (data.HealEffect == null) continue;
                for (int stack = 0; stack < multiplier; stack++)
                    ctx.ApplyEffect(ally, data.HealEffect, caster);
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
            if (data.IsHeal) ctx.Heal(t, HealAmount(t, data, caster), caster);
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
                ctx.Heal(target, HealAmount(target, data, caster), caster);
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

        /// <summary>
        /// Сырое лечение способности = <c>HealFlat + HealPctTargetMissingHp × недостающее HP цели</c>,
        /// а на самого кастующего — ещё и × <see cref="AbilityData.SelfHealFraction"/>.
        /// </summary>
        private static float HealAmount(RuntimeUnit target, AbilityData data, RuntimeUnit caster = null)
        {
            float missing = target.Stats.Get(StatType.MaxHP) - target.CurrentHP;
            if (missing < 0f) missing = 0f;

            float amount = data.HealFlat + data.HealPctTargetMissingHp * missing;
            return ReferenceEquals(target, caster) ? amount * data.SelfHealFraction : amount;
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
        /// Кастующий входит в перебор наравне со всеми: лечит того, кому хуже, будь то он сам.
        /// Тай-брейк — дистанция, затем Id (детерминизм).
        /// </summary>
        /// <remarks>
        /// Себя раньше исключали (решение 2026-07-26/7: «свет — это то, что он отдаёт другим»), но это
        /// оставляло хилера без единственного инструмента, когда фокус переводили на него самого.
        /// Решение 2026-07-28: адресную ульту разрешили и на себя, а цена перенесена в ПАССИВКУ —
        /// само-лечение светом вчетверо слабее союзного (25% против 100%). Отдавать по-прежнему
        /// выгоднее, но выбор «спасти себя» перестал быть невозможным.
        /// </remarks>
        private static RuntimeUnit LowestHpAlly(RuntimeUnit caster, IReadOnlyList<RuntimeUnit> units)
        {
            RuntimeUnit best      = null;
            float       bestPct   = float.MaxValue;
            float       bestDistSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit other = units[i];
                if (other.IsDead || other.Team != caster.Team) continue;

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
