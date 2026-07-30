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

        // Удары, дозревшие на этом тике: цифры сняты, но ещё никому не прилетело (см. Tick).
        private readonly List<ResolvedHit> _hits = new List<ResolvedHit>();

        /// <summary>Удар, у которого замах истёк: кто, по кому и с какими цифрами бьёт.</summary>
        private readonly struct ResolvedHit
        {
            public readonly RuntimeUnit Unit;
            public readonly RuntimeUnit Target;
            public readonly float Raw;
            public readonly float Reach;
            public readonly DamageSchool School;
            public readonly DamageAffinity Affinity;
            public readonly bool Blink;
            /// <summary>Разовое пробивание этого удара (взведено «Скрытностью») — снято вместе с цифрами.</summary>
            public readonly float FlatPen;

            /// <summary>Дистанция толчка этого удара (взведена зарядом усиления); 0 = удар не толкает.</summary>
            public readonly float Knockback;

            /// <summary>Эффект, который этот удар накладывает СВЕРХ обычных on-hit (взведён зарядом).</summary>
            public readonly EffectData BonusEffect;

            /// <summary>Сколько раз наложить <see cref="BonusEffect"/>.</summary>
            public readonly int BonusCount;

            public ResolvedHit(RuntimeUnit unit, RuntimeUnit target, float raw, float reach,
                DamageSchool school, DamageAffinity affinity, bool blink, float flatPen, float knockback,
                EffectData bonusEffect, int bonusCount)
            {
                Unit      = unit;
                Target    = target;
                Raw       = raw;
                Reach     = reach;
                School    = school;
                Affinity  = affinity;
                Blink     = blink;
                FlatPen   = flatPen;
                Knockback = knockback;
                BonusEffect = bonusEffect;
                BonusCount  = bonusCount;
            }
        }

        /// <summary>Обработать автоатаки всех живых юнитов за один тик. <paramref name="dt"/> не используется (тайминг на тиках).</summary>
        /// <remarks>
        /// ДВА ПРОХОДА: сначала у всех дозревают замахи и снимаются цифры удара, потом удары прилетают.
        /// Пока урон наносился по ходу обхода, ослабление, наложенное ранним ударом, успевало срезать удар
        /// того, кто стоит в списке позже. В зеркальном бою это ловилось как разный урон одинаковых бойцов:
        /// левый огневик бил в полную силу (146.9), правый — уже ослабленным «Решительным ударом» левого
        /// защитника (102.8 = ровно ×0.7). Сила удара берётся из статов на начало фазы — одинаково для всех,
        /// независимо от места в списке. Эффекты, наложенные этими ударами, работают со следующего тика,
        /// как это и заведено для контроля (<c>CanAct</c>, вики «14»).
        /// </remarks>
        /// <returns>
        /// true, если хоть один удар отыграл блинк и сдвинул тело. Такой сдвиг случается ПОСЛЕ перестройки
        /// пространственного хэша, поэтому сетка перестаёт соответствовать позициям, и звать перестройку
        /// заново обязан вызывающий. Пойман зеркалом: ассасин, блинкнувший за спину, оставался в хэше на
        /// старой клетке, соседи находили его по-разному с двух сторон — и одинаковые отряды разъезжались
        /// на тике 29.
        /// </returns>
        public bool Tick(List<RuntimeUnit> units, ICombatContext ctx, float dt)
        {
            _hits.Clear();

            // --- Проход 1: таймеры, гейты, снятие цифр. Мир не меняется. ---
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
                // Когда хвост истёк — освобождаемся и ПРОВАЛИВАЕМСЯ к гейту атаки в тот же тик: у стрелка
                // хвост = «интервал − замах», поэтому кулдаун обнуляется ровно тогда же → бесшовный
                // следующий замах без потери тика (иначе период damage→damage съехал бы на +1).
                if (unit.Phase == AttackPhase.Recovery)
                {
                    unit.RecoveryRemaining--;
                    if (unit.RecoveryRemaining > 0) continue;
                    unit.Phase = AttackPhase.Idle;
                }

                // Ещё на кулдауне — ждём.
                if (unit.AttackCooldownTicks > 0) continue;

                // Каст занимает юнита целиком: новый замах не начинается, пока идёт подготовка или канал
                // (M3). Уже занесённый замах каст не рвёт — умение его доигрывает (M18, гейт в AbilitySystem).
                if (unit.IsCastBusy) continue;

                // Готов к атаке: нужна валидная цель в радиусе. Для хил-режима «цель авто-атаки» —
                // раненый союзник (AutoAttackTarget, пишет мозг), не враг: гейтим/снапшотим замах по нему,
                // тогда Resolve лечит именно его (§9.2). CurrentTarget (враг) остаётся движению/отступлению.
                RuntimeUnit target = IsHealMode(unit) ? unit.AutoAttackTarget : unit.CurrentTarget;
                if (target == null || target.IsDead) continue;

                // Гейт старта замаха = базовый радиус (не расширяем захват) И предсказание «замах докрутит»
                // (слой 2, вики «14»). Убегающую цель, которая за время замаха выйдет за reach + tolerance,
                // не бьём вхолостую (это только замедляло бы погоню штрафом занятости) — движение продолжает
                // сближение (MoveApproach дожимает дистанцию), свинг стартует, лишь когда попадёт.
                // Метрика едина с движением и сепарацией: см. CombatPositioning.AttackReachCenter.
                int windupTicks = AttackTiming.WindupTicksFor(unit);
                if (!CombatPositioning.InAttackRange(unit, target, ctx.Tuning)) continue;
                if (!CombatPositioning.CanLandWindup(unit, target, windupTicks, ctx.Tuning)) continue;

                EnterWindup(unit, target, ctx, windupTicks);
            }

            // --- Проход 2a: блинки. Телепорт двигает тело, а из тел считается геометрия ударов, поэтому
            // все перемещения происходят ДО того, как хоть один удар начнёт мерить дистанции и линии.
            bool moved = false;
            for (int i = 0; i < _hits.Count; i++)
            {
                ResolvedHit hit = _hits[i];
                if (!hit.Blink || hit.Unit.IsDead || hit.Target.IsDead) continue;

                CombatPositioning.TeleportBehind(hit.Unit, hit.Target);
                moved = true;
            }

            // --- Проход 2b: удары прилетают. ---
            for (int i = 0; i < _hits.Count; i++) Land(_hits[i], ctx);

            return moved;
        }

        /// <summary>Вход в замах: рестарт кулдауна (якорь), снапшот цели, событие старта.
        /// <paramref name="windupTicks"/> уже посчитан гейтом (<see cref="AttackTiming.WindupTicksFor"/>) —
        /// та же длина, по которой гейт предсказал попадание, без повторного расчёта/расхождения.</summary>
        private void EnterWindup(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx, int windupTicks)
        {
            float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
            int intervalTicks = AttackTiming.IntervalTicks(attackSpeed);
            unit.AttackCooldownTicks = intervalTicks;

            UnitVisual visual = unit.Unit != null ? unit.Unit.Visual : null;
            int frameCount = visual != null ? visual.AttackFrameCount : 0;
            int hitFrame   = visual != null ? visual.AttackHitFrame  : 0;

            unit.WindupTicks = unit.WindupRemaining = windupTicks;

            // Хвост-занятость = доигрыш клипа после кадра контакта (авто-масштаб со скоростью атаки) +
            // опциональный доп.хвост в секундах (сознательный «оверкоммит» для отдельных китов). Считаем
            // здесь — на старте свинга, как и windup, — чтобы бафф скорости в полёте не «расклеил» тайминг.
            int followThrough = AttackTiming.FollowThroughTicks(hitFrame, frameCount, intervalTicks, windupTicks);
            int extraTail     = unit.Unit != null ? AttackTiming.RecoveryTicks(unit.Unit.AttackRecoverySeconds) : 0;
            unit.RecoveryTicks = followThrough + extraTail;

            unit.Phase = AttackPhase.Windup;
            unit.WindupTarget = target;
            // Разбег тратится ЭТИМ свингом: длину замаха он уже отдал (WindupTicksFor читает заряд),
            // и следующий удар обязан быть обычным, иначе разбег стал бы постоянным режимом. Признак
            // переезжает на сам свинг — он живёт до его конца, иначе показу нечего было бы прочитать:
            // заряд гаснет в том же тике, в котором взведён, а снимок снимается после тика.
            unit.ChargedSwing       = unit.ChargedAttackReady;
            unit.ChargedAttackReady = false;

            // Взведённый множитель замаха тратится тем же свингом и по той же причине: длину он уже
            // отдал (её прочитал WindupTicksFor), а постоянно ускоренный замах — это уже другой кит.
            unit.NextWindupMult = 0f;

            ctx.NotifyAttackStarted(unit, target);

            // Краевой случай hitFrame=0 / интервал=1 → windup 0 → удар в тот же тик.
            if (unit.WindupRemaining <= 0) Resolve(unit, ctx);
        }

        /// <summary>
        /// Конец замаха: снять цифры удара по снапшот-цели, если она жива и в радиусе; иначе вхолостую.
        /// Сам удар прилетает в <see cref="Land"/>, когда замахи дозрели у всех (см. <see cref="Tick"/>).
        /// </summary>
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
            // Прощающий буфер (слой 1, вики «14»): цель, сдвинувшаяся за замах в пределах tolerance
            // (микро-дрожание, обычный шаг), ещё поражается; ушедшая за него (блинк/рывок) — вхолостую,
            // кулдаун уже потрачен на старте → «воу, уклонился». Замах при этом не прерывался: юнит
            // доиграл свинг и хвост (см. EnterRecovery выше) независимо от исхода.
            float reach = CombatPositioning.AttackReachCenter(unit, target, ctx.Tuning);
            float landReach = reach + SimConstants.AttackReachTolerance;
            if ((target.Position - unit.Position).sqrMagnitude > landReach * landReach) return;

            // Прирост ресурса — на момент реального удара (мана-реликвии).
            GainResourceOnHit(unit, ctx);

            float raw = unit.Stats.Get(StatType.AutoAttackDamage);
            DamageSchool school = unit.DamageSchool;
            DamageAffinity affinity = unit.Affinity;

            // §9.6 усиление следующей атаки («Скрытность»): множим урон разово, забираем разовое
            // пробивание и снимаем баф стелса. Пробивание тратится тем же ударом, что и множитель.
            float flatPen = 0f;
            float knockback = 0f;
            EffectData bonusEffect = null;
            int bonusCount = 0;
            if (unit.EmpowerDamageMult > 0f)
            {
                raw *= unit.EmpowerDamageMult;
                unit.EmpowerDamageMult = 0f;
                flatPen = unit.EmpowerFlatPen;
                unit.EmpowerFlatPen = 0f;
                knockback = unit.EmpowerKnockback;
                unit.EmpowerKnockback = 0f;
                bonusEffect = unit.EmpowerBonusEffect;
                bonusCount  = unit.EmpowerBonusCount;
                unit.EmpowerBonusEffect = null;
                unit.EmpowerBonusCount  = 0;
                // Снимаем ИМЕННО тот эффект, который заряд выдал (у Убийцы — стелс, у периодического
                // заряда — свой тег): жёсткий Stealth здесь срывал бы скрытность любому, кто просто
                // взвёл усиленный удар, и наоборот оставлял бы висеть чужой заряд.
                ctx.Dispel(new DispelRequest(unit, DispelTargetPolarity.Any, unit.EmpowerConsumeTag, int.MaxValue, 0));
            }

            // §10.5 блинк убийцы: телепорт за спину едет с ударом — он меняет позиции, а их читают
            // соседние резолвы этого же тика.
            bool blink = unit.BlinkBehindOnNextAttack;
            unit.BlinkBehindOnNextAttack = false;

            _hits.Add(new ResolvedHit(unit, target, raw, reach, school, affinity, blink, flatPen, knockback,
                bonusEffect, bonusCount));
        }

        /// <summary>Прилёт снятого удара: урон/снаряд/хил и on-hit эффекты. Блинк уже отыгран (проход 2a).</summary>
        private void Land(in ResolvedHit hit, ICombatContext ctx)
        {
            RuntimeUnit unit = hit.Unit, target = hit.Target;
            // Подтип удара (Дробящий/Режущий/Колющий) едет вместе с уроном: верхняя ступень холодной
            // линии добавляет +20% именно дробящему, и без подтипа она не отличит молот от кинжала.
            PhysicalSubtype subtype = unit.Unit != null ? unit.Unit.PhysicalSubtype : PhysicalSubtype.None;
            // Между снятием цифр и прилётом обоих могли добить ударом того же тика.
            if (unit.IsDead || target.IsDead) return;

            float raw = hit.Raw;
            DamageSchool school = hit.School;
            DamageAffinity affinity = hit.Affinity;
            AttackType attackType = unit.Unit != null ? unit.Unit.AttackType : AttackType.Melee;
            AreaShape shape = unit.Unit != null ? unit.Unit.AutoAttackShape : AreaShape.None;

            // Хил-режим (Светлый пастырь): вместо урона — tracking-хил-снаряд в снапшот-союзника.
            // amount = AutoAttackDamage (сырое; HealShieldDealt/TakenEff применяет ctx.Heal при попадании).
            if (IsHealMode(unit))
            {
                float healSpeed  = unit.Stats.Get(StatType.ProjectileSpeed);
                float healRadius = unit.Stats.Get(StatType.Size) * ctx.Tuning.ProjectileHitRadiusFactor;
                ctx.SpawnProjectile(new ProjectileSpawn(
                    unit, unit.Position, target,
                    healSpeed, healRadius, raw, school, ctx.ArmorK, maxPierces: 0, isHeal: true));
                return;
            }

            if (attackType == AttackType.Melee)
            {
                if (shape == AreaShape.Line)
                {
                    // Полоса длиннее дальности выбора цели (Копейщик: бьёт на 2, накрывает до 4).
                    // Зона всегда одна и та же и растёт от ног носителя — цель может стоять в её
                    // середине, а не только на конце.
                    DealLineDamage(unit, target, hit.Reach * unit.Unit.AutoAttackLengthMult,
                        raw, school, affinity, subtype, ctx);
                }
                else
                {
                    ctx.DealDamage(new DamageRequest(unit, target, raw, school, ctx.ArmorK, sourceKind: DamageSourceKind.AutoAttack, affinity: affinity, bonusFlatPen: hit.FlatPen, subtype: subtype));
                    ApplyAutoAttackOnHit(unit, target, ctx); // §9.1 (мили single)
                    ApplyEmpowerBonus(unit, target, in hit, ctx);
                    PushIfEmpowered(unit, target, in hit, ctx);
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
                    speed, collRadius, raw, school, ctx.ArmorK, pierces,
                    onHitEffects: unit.Unit != null ? unit.Unit.AutoAttackEffects : null,
                    isAutoAttack: true, affinity: affinity));
            }
        }

        /// <summary>Наложить on-hit эффекты авто-атаки реликвии на задетую цель (§9.1, мили-путь).</summary>
        /// <summary>
        /// Толчок заряженного удара («Восходящий удар» Монаха воды): цель уезжает ОТ носителя на
        /// взведённую зарядом дистанцию. Обычный толчок, БЕЗ урона на линии полёта и без добивания о
        /// стену — впечатавшись в край арены, цель просто останавливается и лежит оглушённой
        /// (<c>WallImpactStunSeconds</c>).
        /// </summary>
        /// <remarks>
        /// <b>«Ядро» и второй удар о стену — уникальная механика Монаха вихря</b> (решение Макса
        /// 2026-07-30), поэтому здесь передаётся нулевой урон: <c>DisplacementSystem</c> бьёт о стену
        /// ровно тогда, когда у толчка задан свой урон, а стан выдаёт всегда. Сначала я поставила тут
        /// ядро с уроном удара — это давало обычному киту чужую уникальность и удваивало его выпад у
        /// любой стенки.
        /// <para>Только для ближнего single-удара. Линия и снаряд не толкают намеренно: у линии цель не
        /// одна (кого из четверых уносить — вопрос без ответа), а у снаряда попадание случается в
        /// <c>ProjectileSystem</c> позже и уже без снятых цифр этого удара.</para>
        /// </remarks>
        private static void PushIfEmpowered(RuntimeUnit unit, RuntimeUnit target, in ResolvedHit hit, ICombatContext ctx)
        {
            if (hit.Knockback <= 0f || target.IsDead) return;

            Vector2 away = target.Position - unit.Position;
            if (away.sqrMagnitude < 1e-6f) return; // стоят в одной точке — направления толчка нет

            ctx.Displace(new DisplaceRequest(
                target, unit, away.normalized, hit.Knockback,
                cannonball: false, damage: 0f, school: hit.School, width: 0f,
                affinity: hit.Affinity));
        }

        /// <summary>
        /// Доп. наложения, взведённые зарядом усиления: «каждая третья» Драугра вгоняет в цель лишние
        /// стаки «Изморози». Накладываем повторными вызовами, а не порцией эффекта, потому что порция —
        /// свойство ассета и одинакова для всех, а лишние стаки принадлежат ИМЕННО заряженному удару.
        /// </summary>
        private static void ApplyEmpowerBonus(RuntimeUnit unit, RuntimeUnit target, in ResolvedHit hit, ICombatContext ctx)
        {
            if (hit.BonusEffect == null || hit.BonusCount <= 0 || target.IsDead) return;

            for (int i = 0; i < hit.BonusCount; i++)
                ctx.ApplyEffect(target, hit.BonusEffect, unit);
        }

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

        /// <summary>Хвост-восстановление после удара: Recovery на запланированные тики (доигрыш клипа +
        /// доп. секунды, посчитано в <see cref="EnterWindup"/>), либо сразу Idle, если хвоста нет.</summary>
        private static void EnterRecovery(RuntimeUnit unit)
        {
            int ticks = unit.RecoveryTicks;
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
        private void DealLineDamage(RuntimeUnit unit, RuntimeUnit target, float length, float raw, DamageSchool school, DamageAffinity affinity, PhysicalSubtype subtype, ICombatContext ctx)
        {
            float width = unit.Unit.AutoAttackWidth;
            Vector2 dir = target.Position - unit.Position;

            // Dev-оверлей зоны (показываем полосу даже если никого не задели).
            ctx.ReportAreaHit(AreaHit.Line(unit.Position, dir, length, width, unit.Team));

            ctx.QueryUnitsInLine(unit.Position, dir, length, width, _lineTargets, TargetFilter.Enemies, unit.Team);

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итоговое состояние.
            for (int t = 0; t < _lineTargets.Count; t++)
            {
                ctx.DealDamage(new DamageRequest(unit, _lineTargets[t], raw, school, ctx.ArmorK, sourceKind: DamageSourceKind.AutoAttack, affinity: affinity, subtype: subtype));
                ApplyAutoAttackOnHit(unit, _lineTargets[t], ctx); // §9.1 (мили Line — по каждой задетой)
            }
        }

        /// <summary>
        /// Начислить ресурс за удар (× ResourceGainEff), клампить к MaxResource и к потолку набора
        /// «единиц в секунду», если он задан у юнита.
        /// </summary>
        /// <remarks>
        /// Потолок нужен там, где кит разгоняет собственный темп: удвоенная скорость атаки иначе
        /// удваивает и приток ресурса, и «рекомендованный» темп ульты обваливается вдвое сам собой.
        /// Окно — ровно секунда от первого начисления, скользящее по тикам (никаких таймеров).
        /// </remarks>
        private static void GainResourceOnHit(RuntimeUnit unit, ICombatContext ctx)
        {
            float onHit = unit.Unit != null ? unit.Unit.ResourceOnHit : 0f;
            if (onHit <= 0f) return;

            float gain = onHit * unit.Stats.Get(StatType.ResourceGainEff);

            float perSecondCap = unit.Unit.MaxResourceGainPerSecond;
            if (perSecondCap > 0f)
            {
                int now = ctx.CurrentTick;
                if (now - unit.ResourceWindowStartTick >= SimConstants.TickRate)
                {
                    unit.ResourceWindowStartTick = now;
                    unit.ResourceGainedInWindow  = 0f;
                }

                float room = perSecondCap - unit.ResourceGainedInWindow;
                if (room <= 0f) return;

                if (gain > room) gain = room;
                unit.ResourceGainedInWindow += gain;
            }

            unit.CurrentResource += gain;

            float maxRes = unit.Stats.Get(StatType.MaxResource);
            if (maxRes > 0f && unit.CurrentResource > maxRes) unit.CurrentResource = maxRes;
        }
    }
}
