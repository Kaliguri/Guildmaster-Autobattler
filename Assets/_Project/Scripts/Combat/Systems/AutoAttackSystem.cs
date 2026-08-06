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

        /// <summary>Буфер соседей цели для заряженного удара по площади (см. <see cref="SplashIfEmpowered"/>).</summary>
        private readonly List<RuntimeUnit> _splashTargets = new List<RuntimeUnit>();

        // Удары, дозревшие на этом тике: цифры сняты, но ещё никому не прилетело (см. Tick).
        private readonly List<ResolvedHit> _hits = new List<ResolvedHit>();

        /// <summary>Удар, у которого замах истёк: кто, по кому и с какими цифрами бьёт.</summary>
        private readonly struct ResolvedHit
        {
            public readonly RuntimeUnit Unit;
            public readonly RuntimeUnit Target;
            public readonly float Raw;
            public readonly float Reach;

            /// <summary>Тип урона этого удара — снят вместе с цифрами, чтобы дожить до прилёта неизменным.</summary>
            public readonly DamageType DamageType;

            public readonly bool Blink;
            /// <summary>Разовое пробивание этого удара (взведено «Скрытностью») — снято вместе с цифрами.</summary>
            public readonly float FlatPen;

            /// <summary>Дистанция толчка этого удара (взведена зарядом усиления); 0 = удар не толкает.</summary>
            public readonly float Knockback;

            /// <summary>Эффект, который этот удар накладывает СВЕРХ обычных on-hit (взведён зарядом).</summary>
            public readonly EffectData[] BonusEffects;

            /// <summary>Сколько раз наложить КАЖДЫЙ из <see cref="BonusEffects"/>.</summary>
            public readonly int BonusCount;

            /// <summary>Доля удара, уходящая <see cref="SplitType"/>; 0 = удар одночастный.</summary>
            public readonly float SplitShare;

            /// <summary>Тип отщеплённой половины (Лёд у «Восходящего удара»).</summary>
            public readonly DamageType SplitType;

            /// <summary>Радиус задевания соседей цели (взведён зарядом); 0 = удар только по цели.</summary>
            public readonly float SplashRadius;

            /// <summary>Удар уходит мимо (слепота): цифры сняты, но ни урона, ни on-hit не будет.</summary>
            public readonly bool Missed;

            /// <summary>
            /// Доля тика, в которую пришёлся кадр контакта (0 = момента внутри тика нет). Едет в заявке, а
            /// не читается при прилёте, по той же причине, что тип урона и заряд: между снятием цифр и
            /// прилётом кит не меняется, но так момент и удар заведомо не могут разойтись.
            /// </summary>
            public readonly float SubTick;

            public ResolvedHit(RuntimeUnit unit, RuntimeUnit target, float raw, float reach,
                DamageType damageType, bool blink, float flatPen, float knockback,
                EffectData[] bonusEffects, int bonusCount, float splitShare, DamageType splitType,
                float splashRadius, bool missed, float subTick = 0f)
            {
                Unit       = unit;
                Target     = target;
                Raw        = raw;
                Reach      = reach;
                DamageType = damageType;
                Blink     = blink;
                FlatPen   = flatPen;
                Knockback = knockback;
                BonusEffects = bonusEffects;
                BonusCount  = bonusCount;
                SplitShare  = splitShare;
                SplitType   = splitType;
                SplashRadius = splashRadius;
                Missed      = missed;
                SubTick     = subTick;
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

            // --- Проход 0: Комбо. Считается по фазе, С КОТОРОЙ юнит вошёл в тик, и до того, как эту фазу
            // начнут менять гейты ниже: иначе «вне лупа» у одного юнита мерилось бы состоянием прошлого
            // тика, а у другого — уже нового, и разрыв серии зависел бы от места в списке. ---
            for (int i = 0; i < units.Count; i++) UpdateCombo(units[i], ctx);

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
                    // Канал рвётся БЕЗ рефанда (решение Макса 2026-07-30): часть тиков урона уже прошла,
                    // значит атака состоялась частично — обнулить кулдаун значило бы отдать её бесплатно.
                    // Юнит доигрывает хвост, как после обычного удара: сворачивание потока он всё равно
                    // отрабатывает, и именно этим контроль по нему и наказывает.
                    else if (unit.Phase == AttackPhase.Channel) BreakChannel(unit, ctx);
                    else if (unit.Phase == AttackPhase.Recovery) { unit.Phase = AttackPhase.Idle; unit.RecoveryRemaining = 0; }

                    // Оглушённый ВЫПАЛ из цикла атаки — это и есть «вне боя» (2026-07-30/11: таймер Комбо
                    // считает время именно вне лупа), поэтому боевое ожидание под станом не живёт.
                    // Но хвост, назначенный оборванному каналу, здесь не отменяется: BreakChannel только
                    // что перевёл юнита в Recovery намеренно — сворачивание потока он отрабатывает, и
                    // именно этим контроль по нему и наказывает.
                    else unit.Phase = AttackPhase.Idle;
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

                // Фаза канала: поток тиков урона между замахом и хвостом. Цифры снимаются здесь, а сам
                // урон прилетает во второй фазе Tick вместе со всеми — двухфазность канал не отменяет.
                if (unit.Phase == AttackPhase.Channel)
                {
                    TickChannel(unit, ctx);
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
                    // Хвост доигран — Атака прошла путь целиком, и только теперь она засчитана в серию.
                    CompleteAttack(unit, ctx);
                }

                // Ещё на кулдауне — ждём своего окна. Это и есть боевое ожидание, если цель под рукой:
                // юнит остаётся в цикле атаки, держит слот контакта и ловится парированием (07-30/10).
                if (unit.AttackCooldownTicks > 0) { SetRestingPhase(unit, ctx); continue; }

                // Каст занимает юнита целиком: новый замах не начинается, пока идёт подготовка или канал
                // (M3). Уже занесённый замах каст не рвёт — умение его доигрывает (M18, гейт в AbilitySystem).
                // Кастующий не «ждёт своего удара», он занят другим — боевым ожиданием это не считается.
                if (unit.IsCastBusy) { unit.Phase = AttackPhase.Idle; continue; }

                // Кит без авто-атаки (Пожирательница снов, Барабанщик) в ритм ударов не входит вообще:
                // ни замаха, ни хвоста. Гейт стоит ПОСЛЕ кулдауна намеренно — таймер продолжает тикать,
                // чтобы включение режима на ходу (баф, улучшение) не давало мгновенного удара.
                // В боевое ожидание он тоже не входит: ждать ему нечего.
                if (HasNoAutoAttack(unit)) { unit.Phase = AttackPhase.Idle; continue; }

                // Готов к атаке: нужна валидная цель в радиусе. Для хил-режима «цель авто-атаки» —
                // раненый союзник (AutoAttackTarget, пишет мозг), не враг: гейтим/снапшотим замах по нему,
                // тогда Resolve лечит именно его (§9.2). CurrentTarget (враг) остаётся движению/отступлению.
                RuntimeUnit target = IsHealMode(unit) ? unit.AutoAttackTarget : unit.CurrentTarget;
                if (target == null || target.IsDead) { unit.Phase = AttackPhase.Idle; continue; }

                // Гейт старта замаха = базовый радиус (не расширяем захват) И предсказание «замах докрутит»
                // (слой 2, вики «14»). Убегающую цель, которая за время замаха выйдет за reach + tolerance,
                // не бьём вхолостую (это только замедляло бы погоню штрафом занятости) — движение продолжает
                // сближение (MoveApproach дожимает дистанцию), свинг стартует, лишь когда попадёт.
                // Метрика едина с движением и сепарацией: см. CombatPositioning.AttackReachCenter.
                // Удар с разбега — единственное исключение: он начинает замах ЗА границей досягаемости,
                // чтобы кадр контакта пришёлся на въезд в неё, а не на «добежал, встал, ударил». Остаток
                // дистанции закрывает ход (рут снят в MovementSystem на время такого замаха).
                int windupTicks = AttackTiming.WindupTicksFor(unit);
                bool chargingIn = ChargesIntoReach(unit, target, windupTicks, ctx);
                if (!chargingIn)
                {
                    // Не дотягивается — он ещё бежит, то есть в цикл атаки не вошёл.
                    if (!CombatPositioning.InAttackRange(unit, target, ctx.Tuning))
                    { unit.Phase = AttackPhase.Idle; continue; }

                    // Дотягивается, но цель успеет уйти за время замаха: удар не начинаем, а вот из боя
                    // юнит не выпадал — он стоит над целью и ждёт момента. Это боевое ожидание.
                    if (!CombatPositioning.CanLandWindup(unit, target, windupTicks, ctx.Tuning))
                    { unit.Phase = AttackPhase.CombatIdle; continue; }
                }

                EnterWindup(unit, target, ctx, windupTicks, chargingIn);
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
        /// та же длина, по которой гейт предсказал попадание, без повторного расчёта/расхождения.
        /// <paramref name="chargingIn"/> — замах начат из-за границы досягаемости и доезжает сам себя.</summary>
        private void EnterWindup(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx, int windupTicks,
            bool chargingIn)
        {
            float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
            int intervalTicks = AttackTiming.IntervalTicks(attackSpeed);

            // Кулдаун-якорь держит период «удар → удар» равным интервалу. У канального кита интервал
            // отмеряет тик ВНУТРИ потока, а не период между атаками, поэтому якорить им нечего: ритм
            // целиком держат фазы (замах → канал → хвост), и следующий замах начинается сразу за хвостом.
            // Ноль здесь, а не полный цикл: сорванный канал уже наказан потерянными тиками урона, и
            // добавлять к этому ожидание значило бы наказать дважды за одно прерывание.
            unit.AttackCooldownTicks = HasChannel(unit) ? 0 : intervalTicks;

            // Контакты этого свинга: один у обычного кита, несколько у многоударного. Считаются здесь и
            // не пересчитываются — занесённый удар живёт по тем цифрам, с которыми начался.
            AttackTiming.ContactTicks(unit, unit.SwingContacts);
            unit.SwingHitIndex = 0;

            unit.WindupTicks = unit.WindupRemaining = windupTicks;

            // Хвост-занятость = доигрыш клипа после кадра контакта (авто-масштаб со скоростью атаки) +
            // опциональный доп.хвост в секундах (сознательный «оверкоммит» для отдельных китов). Считаем
            // здесь — на старте свинга, как и windup, — чтобы бафф скорости в полёте не «расклеил» тайминг.
            // У серии доигрыш идёт от ПОСЛЕДНЕГО контакта: хвост — это то, что после удара, а последний
            // удар серии приходит позже первого.
            int maxAnimTicks  = unit.Unit != null ? unit.Unit.AttackSwingTicks : 0; // 0 = глобальный потолок
            int lastContact   = unit.SwingContacts.Count > 0
                ? unit.SwingContacts[unit.SwingContacts.Count - 1]
                : windupTicks;

            // Ускоренный рекастом замах СОКРАЩАЕТ свинг, а не переливает сэкономленное в доигрыш. Хвост
            // меряется от контакта — значит по сырой формуле удар, вышедший вдвое быстрее, получил бы
            // вдвое более длинный доигрыш, и весь выигрыш рекаста вернулся бы обратно тем же тиком.
            // Поэтому доигрыш считаем от БАЗОВОЙ длины замаха: контакт наступает раньше, всё после него —
            // как обычно. Поймано тестом RecoveryCut_IsSpentByOneSwing (2026-07-31).
            int tailAnchor = lastContact;
            if (unit.NextWindupMult > 0f)
                tailAnchor += AttackTiming.WindupTicksFor(unit, ignoreRecast: true) - windupTicks;

            float windupShare = unit.Unit != null ? unit.Unit.WindupShare : 0f;
            int followThrough = AttackTiming.FollowThroughTicks(windupShare, intervalTicks, tailAnchor,
                maxAnimTicks);
            // У канальной формы хвост свой — сворачивание потока, — и берётся из профиля канала: тот же
            // кит в ближней форме бьёт короткими выпадами, и общий хвост кита навязал бы им чужую цену.
            int extraTail = HasChannel(unit)
                ? AttackTiming.RecoveryTicks(unit.AttackChannel.RecoverySeconds)
                : unit.Unit != null ? AttackTiming.RecoveryTicks(unit.Unit.AttackRecoverySeconds) : 0;
            unit.RecoveryTicks = followThrough + extraTail;

            unit.Phase = AttackPhase.Windup;
            unit.WindupTarget = target;
            // Въезд держится весь замах: по нему движение снимает рут, и он же кончается вместе с фазой.
            unit.ChargingIn = chargingIn;

            // Разбег тратится ЭТИМ свингом: длину замаха он уже отдал (WindupTicksFor читает заряд),
            // и следующий удар обязан быть обычным, иначе разбег стал бы постоянным режимом. Признак
            // переезжает на сам свинг — он живёт до его конца, иначе показу нечего было бы прочитать:
            // заряд гаснет в том же тике, в котором взведён, а снимок снимается после тика.
            //
            // Разбег требует ВЪЕЗДА, а не просто заряда: удар с разбега — это удар на ходу, и стоящий
            // юнит не должен играть клип рывка. Тот, кто добежал и остановился, заряд уже потерял
            // (StopSprint), так что здесь это скорее второй замок, чем первый.
            unit.ChargedSwing       = unit.ChargedAttackReady && chargingIn;
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
            // Контакт разрешён — считаем его состоявшимся ДО расчёта урона. Это важно для промаха: он
            // тоже контакт (техдолг §3.9), иначе кайт получал бы рефанд кулдауна и ускорял того, от кого
            // убегает.
            int hitIndex = unit.SwingHitIndex;
            unit.SwingHitIndex++;

            // Серия: следующий контакт этого же свинга. Фаза остаётся замахом — юнит занят одной Атакой
            // от взмаха до конца доигрыша (2026-07-30/7), и рут, гейты и показ работают тем же законом.
            bool hasNextContact = hitIndex + 1 < unit.SwingContacts.Count;
            if (hasNextContact)
            {
                unit.WindupRemaining = unit.SwingContacts[hitIndex + 1] - unit.SwingContacts[hitIndex];
                TryQueueHit(unit, unit.WindupTarget, ctx, hitIndex);
                return;
            }

            // Замах кончился → хвост-восстановление (или сразу Idle, если восстановления нет). Переход
            // выполняем ДО расчёта урона: юнит «занят» бэксвингом независимо от того, попал он или вхолостую.
            unit.WindupRemaining = 0;
            unit.ChargingIn = false;   // въезд кончился вместе с замахом: дальше юнита рутует хвост
            RuntimeUnit target = unit.WindupTarget;
            unit.WindupTarget = null;

            // Канальный кит вместо хвоста открывает поток: замах кончился — значит поток пошёл. Первый
            // тик урона снимается тут же, общим путём — тик канала и есть обычный удар. Промах замаха
            // (цель мертва/ушла) канал не открывает: держать поток не в кого.
            bool opensChannel = HasChannel(unit) && CanHit(unit, target, ctx);
            if (opensChannel) EnterChannel(unit, target);
            else EnterRecovery(unit, ctx);

            TryQueueHit(unit, target, ctx, hitIndex);
        }

        /// <summary>
        /// Тик канала (<see cref="AttackPhase.Channel"/>): поток льётся в свою цель, пока та жива, в
        /// радиусе и пока не вышло время. Урон снимается ровно тем же путём, что удар обычной атаки.
        /// </summary>
        /// <remarks>
        /// Разрыв — без рефанда кулдауна и с доигрыванием хвоста: часть тиков урона уже прошла, атака
        /// состоялась частично. Именно поэтому уход из радиуса и контроль ЯВЛЯЮТСЯ ценой для канального
        /// кита, а не бесплатной перезарядкой.
        /// </remarks>
        private void TickChannel(RuntimeUnit unit, ICombatContext ctx)
        {
            RuntimeUnit target = unit.AttackChannelTarget;
            if (!CanHit(unit, target, ctx)) { BreakChannel(unit, ctx); return; }

            if (unit.AttackChannelTickRemaining > 0) unit.AttackChannelTickRemaining--;
            if (unit.AttackChannelTickRemaining <= 0)
            {
                TryQueueHit(unit, target, ctx);
                unit.AttackChannelTickRemaining = ChannelTickInterval(unit);
            }

            unit.AttackChannelRemaining--;
            if (unit.AttackChannelRemaining <= 0) BreakChannel(unit, ctx);
        }

        /// <summary>Открыть канал: длительность и период тика — снимок на старте потока, как и замах.</summary>
        private static void EnterChannel(RuntimeUnit unit, RuntimeUnit target)
        {
            unit.Phase = AttackPhase.Channel;
            unit.AttackChannelTarget = target;
            unit.AttackChannelRemaining = AttackTiming.RecoveryTicks(unit.AttackChannel.DurationSeconds);
            // Первый тик урона снимается в том же тике, что открытие канала (его ставит Resolve), поэтому
            // отсчёт до второго начинается с полного периода.
            unit.AttackChannelTickRemaining = ChannelTickInterval(unit);
        }

        /// <summary>Погасить канал и уйти в хвост: рефанда кулдауна нет ни при разрыве, ни по времени.</summary>
        private static void BreakChannel(RuntimeUnit unit, ICombatContext ctx)
        {
            unit.AttackChannelRemaining = 0;
            unit.AttackChannelTickRemaining = 0;
            unit.AttackChannelTarget = null;
            EnterRecovery(unit, ctx);
        }

        /// <summary>
        /// Период между тиками канала = интервал атаки. «Скорость атаки — это расстояние между тиками
        /// урона» (формулировка Макса): при уроне тика в <c>AutoAttackDamage</c> это даёт DPS, равный
        /// классовой норме кита, и скорость атаки скейлит его линейно, как у любого бойца.
        /// </summary>
        private static int ChannelTickInterval(RuntimeUnit unit)
        {
            int ticks = AttackTiming.IntervalTicks(unit.Stats.Get(StatType.AttackSpeed));
            return ticks < 1 ? 1 : ticks;
        }

        /// <summary>Юнит держит канал вместо одномоментного удара — по РАНТАЙМ-снимку: канал бывает свойством стойки.</summary>
        private static bool HasChannel(RuntimeUnit unit) => unit.AttackChannel.Exists;

        /// <summary>Цель ещё поражаема: жива и в досягаемости с прощающим буфером (та же метрика, что у резолва).</summary>
        private static bool CanHit(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx)
        {
            if (target == null || target.IsDead) return false;

            float landReach = CombatPositioning.AttackReachCenter(unit, target, ctx.Tuning)
                              + SimConstants.AttackReachTolerance;
            return (target.Position - unit.Position).sqrMagnitude <= landReach * landReach;
        }

        /// <summary>
        /// Снять цифры удара по цели и поставить его в очередь прилёта (<see cref="Land"/> во второй фазе
        /// <see cref="Tick"/>). Общий путь для одномоментного удара и для тика канала — у них нет ни одного
        /// различия в расчёте, и разведи я их, различие однажды появилось бы само.
        /// </summary>
        private void TryQueueHit(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx, int hitIndex = 0)
        {
            // Цель пропала к удару (мертва / вне радиуса) → вхолостую, кулдаун уже потрачен на старте.
            if (target == null || target.IsDead) return;

            // Досягаемость с учётом тел (см. EnterWindup). reach также = длина линейной АА ниже.
            // Прощающий буфер (слой 1, вики «14»): цель, сдвинувшаяся за замах в пределах tolerance
            // (микро-дрожание, обычный шаг), ещё поражается; ушедшая за него (блинк/рывок) — вхолостую,
            // кулдаун уже потрачен на старте → «воу, уклонился». Замах при этом не прерывался: юнит
            // доиграл свинг и хвост (переход фазы делает вызывающий) независимо от исхода.
            float reach = CombatPositioning.AttackReachCenter(unit, target, ctx.Tuning);
            float landReach = reach + SimConstants.AttackReachTolerance;
            if ((target.Position - unit.Position).sqrMagnitude > landReach * landReach) return;

            // Прирост ресурса — на момент реального удара (мана-реликвии).
            GainResourceOnHit(unit, ctx);

            // Маскировка слетает от своего удара — «притаился перед нанесением удара» и есть её смысл
            // (Макс 2026-07-31). Снимаем ДО расчёта: удар наносит уже видимый юнит, иначе один и тот же
            // момент был бы и скрытым, и нет. У Убийцы это же состояние снимет ниже расход усиления —
            // диспел идемпотентен, а вот кит БЕЗ усиления иначе остался бы скрытым навсегда.
            if (unit.ConcealTier != Data.Definitions.ConcealmentTier.None)
                ctx.Dispel(new DispelRequest(unit, DispelTargetPolarity.Any, EffectTag.Stealth, int.MaxValue, 0));

            float raw = unit.Stats.Get(StatType.AutoAttackDamage);

            // Сила ЭТОГО Удара в серии: у одноударного кита множитель всегда 1, у Монаха — половина на
            // каждый. Дефолт единица, а не «поровну»: число Ударов не должно молча делить урон кита
            // (вердикт Макса 2026-07-31), силу серии объявляет автор.
            if (unit.Unit != null) raw *= unit.Unit.HitDamageShare(hitIndex);

            // Тип урона снимается ВМЕСТЕ с цифрами и едет до прилёта: между замахом и попаданием кит
            // не меняется, но так удар и его тип заведомо не могут разойтись.
            DamageType damageType = unit.AutoAttackDamageType;

            // §9.6 усиление следующей атаки («Скрытность»): множим урон разово, забираем разовое
            // пробивание и снимаем баф стелса. Пробивание тратится тем же ударом, что и множитель.
            float flatPen = 0f;
            float knockback = 0f;
            EffectData[] bonusEffects = null;
            int bonusCount = 0;
            float splitShare = 0f;
            DamageType splitType = DamageType.Undefined;
            float splashRadius = 0f;
            if (unit.EmpowerDamageMult > 0f)
            {
                raw *= unit.EmpowerDamageMult;
                unit.EmpowerDamageMult = 0f;
                flatPen = unit.EmpowerFlatPen;
                unit.EmpowerFlatPen = 0f;
                knockback = unit.EmpowerKnockback;
                unit.EmpowerKnockback = 0f;
                bonusEffects = unit.EmpowerBonusEffects;
                bonusCount  = unit.EmpowerBonusCount;
                splitShare  = unit.EmpowerSplitShare;
                splitType   = unit.EmpowerSplitType;
                unit.EmpowerBonusEffects = null;
                unit.EmpowerBonusCount  = 0;
                unit.EmpowerSplitShare  = 0f;
                unit.EmpowerSplitType   = DamageType.Undefined;
                splashRadius = unit.EmpowerSplashRadius;
                unit.EmpowerSplashRadius = 0f;
                // Снимаем ИМЕННО тот эффект, который заряд выдал (у Убийцы — стелс, у периодического
                // заряда — свой тег): жёсткий Stealth здесь срывал бы скрытность любому, кто просто
                // взвёл усиленный удар, и наоборот оставлял бы висеть чужой заряд.
                // Тег НЕ ЗАДАН — снимать нечего: диспел по None означает «по любому тегу» и сносит с
                // носителя вообще всё. Замером 2026-07-31 так и вышло: заряд цикла голема стирал сам цикл
                // и каменный оберег, из-за чего голем навсегда застревал на первом ударе.
                if (unit.EmpowerConsumeTag != EffectTag.None)
                    ctx.Dispel(new DispelRequest(unit, DispelTargetPolarity.Any, unit.EmpowerConsumeTag, int.MaxValue, 0));
            }

            // §10.5 блинк убийцы: телепорт за спину едет с ударом — он меняет позиции, а их читают
            // соседние резолвы этого же тика.
            bool blink = unit.BlinkBehindOnNextAttack;
            unit.BlinkBehindOnNextAttack = false;

            // Атака считается ДО опроса слепоты и независимо от её исхода: «каждая X-я мимо» отмеряет
            // взмахи носителя, а не попадания. Иначе слепота, отняв удар, сдвигала бы собственный счёт и
            // период поехал бы (первый промах отодвигал бы следующий на четыре УДАЧНЫХ удара).
            unit.HitsMade++;
            bool missed = ctx.ResolveAttackMiss(unit);

            // Доля тика — только у ПЕРВОГО контакта свинга: он один стоит там, где его посчитал замах.
            // Остальные контакты серии раздвинуты своими инвариантами (минимум тик между ударами, упор в
            // границу интервала), а тик канала вообще отмеряется периодом, а не кадром клипа — у них
            // момента внутри тика нет, и придумывать его значило бы двигать вспышку в никуда.
            // Фазу сравниваем с Channel, а не с Windup: к этому месту Resolve уже перевёл юнита в хвост
            // (или в канал), и проверка «ещё в замахе» обнулила бы долю у обычного одноударного кита —
            // то есть ровно у того, для кого она и считается.
            float subTick = hitIndex == 0 && unit.Phase != AttackPhase.Channel
                ? AttackTiming.ContactSubTick(unit, unit.WindupTicks)
                : 0f;

            _hits.Add(new ResolvedHit(unit, target, raw, reach, damageType, blink, flatPen, knockback,
                bonusEffects, bonusCount, splitShare, splitType, splashRadius, missed, subTick));
        }

        /// <summary>
        /// Прилёт снятого удара. Оболочка держит долю тика на носителе ровно на время нанесения: её
        /// читает запись в ленту, а всё, что бьёт вне удара — периодика, реактив, способность — обязано
        /// видеть ноль. Ставить её на весь свинг нельзя: событие урона у всех источников одно.
        /// </summary>
        private void Land(in ResolvedHit hit, ICombatContext ctx)
        {
            hit.Unit.ContactSubTick = hit.SubTick;
            LandResolved(in hit, ctx);
            hit.Unit.ContactSubTick = 0f;
        }

        /// <summary>Прилёт снятого удара: урон/снаряд/хил и on-hit эффекты. Блинк уже отыгран (проход 2a).</summary>
        private void LandResolved(in ResolvedHit hit, ICombatContext ctx)
        {
            RuntimeUnit unit = hit.Unit, target = hit.Target;
            // Между снятием цифр и прилётом обоих могли добить ударом того же тика.
            if (unit.IsDead || target.IsDead) return;

            // Слепой промахнулся: взмах состоялся (кулдаун списан, счётчик атак сдвинут, показ увидел
            // удар), но ни урона, ни on-hit, ни толчка нет. Заряд усиления при этом уже потрачен — он
            // снимается вместе с цифрами, и «промазал заряженным» честнее, чем бесплатная перезарядка.
            if (hit.Missed)
            {
                ctx.ReportAttackMissed(unit, target);
                return;
            }

            float raw = hit.Raw;

            // Тип урона несёт и школу брони, и идентичность удара: верхняя ступень холодной линии
            // добавляет +20% именно Дробящему, и без типа она не отличит молот от кинжала.
            DamageType damageType = hit.DamageType;
            AttackType attackType = unit.AttackType;   // рантайм-снимок: доставку переписывает стойка
            AreaShape shape = unit.Unit != null ? unit.Unit.AutoAttackShape : AreaShape.None;

            // Хил-режим (Светлый пастырь): вместо урона — tracking-хил-снаряд в снапшот-союзника.
            // amount = AutoAttackDamage (сырое; HealShieldDealt/TakenEff применяет ctx.Heal при попадании).
            if (IsHealMode(unit))
            {
                float healSpeed  = unit.Stats.Get(StatType.ProjectileSpeed);
                float healRadius = unit.Stats.Get(StatType.Size) * ctx.Tuning.ProjectileHitRadiusFactor;
                ctx.SpawnProjectile(new ProjectileSpawn(
                    unit, unit.Position, target,
                    healSpeed, healRadius, raw, damageType, ctx.ArmorK, maxPierces: 0, isHeal: true));
                return;
            }

            // Канал бьёт МГНОВЕННО, даже когда кит формально дальнобойный (решение Макса 2026-07-30:
            // «должен быть урон мгновенно»). Снаряд по своей природе дискретная посылка с временем полёта —
            // поток из посылок перестаёт быть потоком, а «Кровавый обмен» Десятины считает урон по
            // позиции жертвы в момент попадания, которая у летящего снаряда уже другая.
            if (attackType == AttackType.Melee || HasChannel(unit))
            {
                if (shape == AreaShape.Line)
                {
                    // Полоса длиннее дальности выбора цели (Копейщик: бьёт на 2, накрывает до 4).
                    // Зона всегда одна и та же и растёт от ног носителя — цель может стоять в её
                    // середине, а не только на конце.
                    DealLineDamage(unit, target, hit.Reach * unit.Unit.AutoAttackLengthMult,
                        raw, damageType, ctx);
                }
                else
                {
                    // Заряженный удар может расщепляться на два типа («Восходящий удар» Монаха воды:
                    // половина Дробящим, половина Льдом). Два запроса, а не одна цифра с половинчатой
                    // школой: каждая половина режется своей бронёй и будит своих потребителей.
                    // Расщепление ПО ТЕГУ ЦЕЛИ живёт отдельно (AttackSplit у Мечника) — там условие
                    // смотрит на цель, здесь свойство самого заряда.
                    float splitShare = hit.SplitType != DamageType.Undefined ? hit.SplitShare : 0f;

                    if (splitShare < 1f)
                        ctx.DealDamage(new DamageRequest(unit, target, raw * (1f - splitShare), damageType, ctx.ArmorK, sourceKind: DamageSourceKind.AutoAttack, bonusFlatPen: hit.FlatPen));
                    if (splitShare > 0f)
                        ctx.DealDamage(new DamageRequest(unit, target, raw * splitShare, hit.SplitType, ctx.ArmorK, sourceKind: DamageSourceKind.AutoAttack, bonusFlatPen: hit.FlatPen));
                    ApplyAutoAttackOnHit(unit, target, ctx); // §9.1 (мили single)
                    ApplyEmpowerBonus(unit, target, in hit, ctx);
                    PushIfEmpowered(unit, target, in hit, ctx);
                    SplashIfEmpowered(unit, target, in hit, ctx);
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
                    speed, collRadius, raw, damageType, ctx.ArmorK, pierces,
                    onHitEffects: unit.AutoAttackOnHit,
                    isAutoAttack: true));
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
                cannonball: false, damage: 0f, damageType: hit.DamageType, width: 0f));
        }

        /// <summary>
        /// Заряженный удар по площади: соседи цели в <see cref="ResolvedHit.SplashRadius"/> получают тот же
        /// урон, что и она (огненная сфера гоблина-мага, размашистый удар земляного голема).
        /// </summary>
        /// <remarks>
        /// <b>Центр — цель, а не носитель.</b> Это взмах, накрывающий строй вокруг того, кого ударили;
        /// круг вокруг себя — другая форма, и она уже есть у способностей (<c>AreaShape.Circle</c>).
        /// <para><b>Соседям достаётся урон, но не on-hit эффекты и не расщепление.</b> On-hit — свойство
        /// удара по ЦЕЛИ (иначе яд с клинка размазывался бы по всем задетым в обход своей стоимости), а
        /// расщепление снято под конкретную цель. Соседи получают одну цифру одним типом.</para>
        /// <para>Только для ближнего single-удара — там же, где живут толчок и бонусные наложения: у линии
        /// цель не одна, а снаряд прилетает позже и без снятых цифр этого удара.</para>
        /// </remarks>
        private void SplashIfEmpowered(RuntimeUnit unit, RuntimeUnit target, in ResolvedHit hit, ICombatContext ctx)
        {
            if (hit.SplashRadius <= 0f) return;

            _splashTargets.Clear();
            ctx.QueryUnitsInRadius(target.Position, hit.SplashRadius, _splashTargets,
                TargetFilter.Enemies, unit.Team);

            for (int i = 0; i < _splashTargets.Count; i++)
            {
                RuntimeUnit other = _splashTargets[i];
                // Саму цель пропускаем: её удар уже нанесён выше, и второй заход был бы двойным.
                if (other == null || other == target || other.IsDead) continue;

                ctx.DealDamage(new DamageRequest(unit, other, hit.Raw, hit.DamageType, ctx.ArmorK,
                    sourceKind: DamageSourceKind.AutoAttack, bonusFlatPen: hit.FlatPen));
            }
            _splashTargets.Clear();
        }

        /// <summary>
        /// Доп. наложения, взведённые зарядом усиления: «каждая третья» Драугра вгоняет в цель лишние
        /// стаки «Изморози». Накладываем повторными вызовами, а не порцией эффекта, потому что порция —
        /// свойство ассета и одинакова для всех, а лишние стаки принадлежат ИМЕННО заряженному удару.
        /// </summary>
        private static void ApplyEmpowerBonus(RuntimeUnit unit, RuntimeUnit target, in ResolvedHit hit, ICombatContext ctx)
        {
            if (hit.BonusEffects == null || hit.BonusCount <= 0 || target.IsDead) return;

            for (int e = 0; e < hit.BonusEffects.Length; e++)
            {
                EffectData def = hit.BonusEffects[e];
                if (def == null) continue;
                for (int i = 0; i < hit.BonusCount; i++)
                    ctx.ApplyEffect(target, def, unit);
            }
        }

        private static void ApplyAutoAttackOnHit(RuntimeUnit unit, RuntimeUnit target, ICombatContext ctx)
        {
            EffectData[] effects = unit.AutoAttackOnHit;
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null) ctx.ApplyEffect(target, effects[i], unit);
        }

        /// <summary>
        /// Начинать ли этот замах ВЪЕЗДОМ — из-за границы досягаемости, чтобы кадр контакта пришёлся на
        /// момент, когда дистанции хватит для удара (см. <see cref="CombatPositioning.CanCloseIntoReach"/>).
        /// </summary>
        /// <remarks>
        /// Въезд полагается КАЖДОМУ подбегающему, а не только разогнавшемуся (решение Макса 31.07.2026:
        /// «атака должна случаться сразу при достижении нужной дистанции»). Пока условием стоял заряд
        /// разбега, все остальные добегали, тормозили и лишь потом начинали замах — удар выходил на
        /// полсекунды позже момента, в который он выглядел заслуженным.
        /// <para>Только ближний бой. Стрелку въезд дал бы замах, начатый в движении на его рабочей
        /// дистанции, — а там никакого сближения и нет: порог считается от зазора сверх досягаемости
        /// именно потому, что по сырому расстоянию стрелок «бежал бы всегда».</para>
        /// </remarks>
        private static bool ChargesIntoReach(RuntimeUnit unit, RuntimeUnit target, int windupTicks, ICombatContext ctx)
        {
            if (unit.Unit == null || unit.AttackType != AttackType.Melee) return false;

            return CombatPositioning.CanCloseIntoReach(unit, target, windupTicks, ctx.Tuning);
        }

        /// <summary>Хил-автоатака (Светлый пастырь): авто-атака лечит союзника вместо урона по врагу (§9.2).</summary>
        private static bool IsHealMode(RuntimeUnit unit) =>
            unit.Unit?.Ai != null && unit.Unit.Ai.AutoAttackMode == AutoAttackMode.Heal;

        /// <summary>Кит без авто-атаки: весь его урон живёт в способностях (<see cref="AutoAttackMode.None"/>).</summary>
        private static bool HasNoAutoAttack(RuntimeUnit unit) =>
            unit.Unit?.Ai != null && unit.Unit.Ai.AutoAttackMode == AutoAttackMode.None;

        /// <summary>
        /// Комбо носителя на этом тике: пробыл вне атакующего лупа дольше
        /// <see cref="Core.Simulation.SimTuning.ComboBreakSeconds"/> — серия рвётся и начинается заново
        /// (ГДД: глоссарий, 2026-07-30/11).
        /// </summary>
        /// <remarks>
        /// «Вне лупа» = <see cref="AttackPhase.Idle"/>: бежит, стоит без цели, лежит в стане. Боевое
        /// ожидание счётчик обнуляет — боец держит цель и ждёт своего интервала, серию это не рвёт.
        /// <para>Событие шлётся ровно один раз на разрыв: условие смотрит на <c>ComboAttacks &gt; 0</c>, а
        /// он тут же обнуляется. Иначе стоящий без цели юнит слал бы «серия порвалась» каждый тик, и
        /// владельцы зарядов гасили бы уже погашенное.</para>
        /// </remarks>
        private static void UpdateCombo(RuntimeUnit unit, ICombatContext ctx)
        {
            if (unit.IsDead) return;

            if (unit.Phase != AttackPhase.Idle)
            {
                unit.ComboIdleTicks = 0;
                return;
            }

            unit.ComboIdleTicks++;
            if (unit.ComboIdleTicks < ctx.Tuning.ComboBreakTicks || unit.ComboAttacks <= 0) return;

            unit.ComboAttacks = 0;
            ctx.NotifyComboBroken(unit);
        }

        /// <summary>
        /// Атака дошла до конца пути (замах → канал → хвост) и засчитана в текущее Комбо. Промах её
        /// засчитывает так же, как попадание: считается взмах, а не результат (вердикт Макса 2026-08-01).
        /// </summary>
        /// <remarks>
        /// Прерванная контролем Атака сюда не попадает — ни из замаха (<see cref="Interrupt"/>), ни из
        /// оборванного хвоста: и там, и там юнит уходит в <see cref="AttackPhase.Idle"/> мимо этой точки.
        /// Оборванный контролем КАНАЛ хвост всё же доигрывает, и если стан к тому времени спал — Атака
        /// засчитывается: она отработала свой путь до конца, просто короче задуманного.
        /// </remarks>
        private static void CompleteAttack(RuntimeUnit unit, ICombatContext ctx)
        {
            unit.ComboAttacks++;
            ctx.NotifyAttackCompleted(unit);
        }

        /// <summary>Хвост-восстановление после удара: Recovery на запланированные тики (доигрыш клипа +
        /// доп. секунды, посчитано в <see cref="EnterWindup"/>), либо сразу Idle, если хвоста нет.</summary>
        private static void EnterRecovery(RuntimeUnit unit, ICombatContext ctx)
        {
            // Рекаст, взведённый ещё в замахе, ускоряет именно хвост: занесённый удар доигрывает целиком,
            // а вот доигрыш после него идёт быстрее (модель Макса 2026-07-31). Скорость тратится вместе со
            // свингом — следующая атака получает свой хвост в обычном темпе.
            // Пол в один тик: фаза, у которой хвост вообще есть, не должна исчезать от ускорения — она
            // короткая, но существует, и её окном пользуется чужой ответ.
            int ticks = unit.RecoveryTicks;
            if (unit.SwingRecoverySpeed > 1f && ticks > 0)
            {
                ticks = (int)global::System.Math.Round(ticks / unit.SwingRecoverySpeed,
                    global::System.MidpointRounding.AwayFromZero);
                if (ticks < 1) ticks = 1;
            }
            unit.SwingRecoverySpeed = 1f;
            if (ticks <= 0)
            {
                unit.Phase = AttackPhase.Idle;
                unit.RecoveryRemaining = 0;
                // Хвоста нет вовсе — путь Атаки кончается здесь же, и засчитать её надо в том же тике.
                CompleteAttack(unit, ctx);
            }
            else
            {
                unit.Phase = AttackPhase.Recovery;
                unit.RecoveryRemaining = ticks;
            }
        }

        /// <summary>
        /// Фаза покоя между ударами: <see cref="AttackPhase.CombatIdle"/>, если юнит остаётся в цикле
        /// атаки (цель жива и в досягаемости), иначе <see cref="AttackPhase.Idle"/>.
        /// </summary>
        /// <remarks>
        /// Граница проходит по <b>досягаемости</b>, а не по наличию цели: бегущий к выбранной цели ещё не
        /// в бою (уточнение Макса 2026-07-30/10). Метрика та же, что у гейта атаки и у движения —
        /// <see cref="CombatPositioning.InAttackRange"/>, — иначе «в бою» по одной формуле и «бью» по
        /// другой разошлись бы на границе.
        /// </remarks>
        private static void SetRestingPhase(RuntimeUnit unit, ICombatContext ctx)
        {
            RuntimeUnit target = IsHealMode(unit) ? unit.AutoAttackTarget : unit.CurrentTarget;

            bool inLoop = target != null
                       && !target.IsDead
                       && !HasNoAutoAttack(unit)
                       && CombatPositioning.InAttackRange(unit, target, ctx.Tuning);

            unit.Phase = inLoop ? AttackPhase.CombatIdle : AttackPhase.Idle;
        }

        /// <summary>
        /// Прерывание замаха: сброс и — только пустому свингу — рефанд кулдауна, плюс событие.
        /// </summary>
        /// <remarks>
        /// <b>Рефанд положен лишь свингу, не нанёсшему ни одного контакта</b> (техдолг §3.9, журнал
        /// «Refund Belongs Only To An Empty Swing»). Для одиночного удара это честно: не ударил — ничего
        /// не потерял. Для серии рефанд был бы эксплойтом: первый Удар уже прошёл, микростан рвёт
        /// остаток, кулдаун обнуляется — и новый свинг начинается снова с первого контакта, то есть
        /// контроль УСКОРЯЛ бы жертву. Без рефанда период «первый контакт → первый контакт» равен
        /// интервалу всегда, сколько бы хвоста серии ни съел контроль, а отнятые Удары и есть его цена.
        /// <para>Промах засчитан контактом раньше, в <see cref="Resolve"/>, — иначе рефанд получал бы
        /// кайт, и убегающий ускорял бы того, от кого убегает.</para>
        /// </remarks>
        private static void Interrupt(RuntimeUnit unit, ICombatContext ctx)
        {
            bool emptySwing = unit.SwingHitIndex == 0;

            unit.Phase = AttackPhase.Idle;
            unit.WindupRemaining = 0;
            unit.RecoveryRemaining = 0;
            unit.WindupTarget = null;
            unit.ChargingIn = false;
            if (emptySwing) unit.AttackCooldownTicks = 0;
            ctx.NotifyAttackInterrupted(unit);
        }

        /// <summary>Линейная авто-атака «Размашистый выпад»: полоса к цели, урон по всем врагам в ней.</summary>
        private void DealLineDamage(RuntimeUnit unit, RuntimeUnit target, float length, float raw, DamageType damageType, ICombatContext ctx)
        {
            float width = unit.Unit.AutoAttackWidth;
            Vector2 dir = target.Position - unit.Position;

            // Dev-оверлей зоны (показываем полосу даже если никого не задели).
            ctx.ReportAreaHit(AreaHit.Line(unit.Position, dir, length, width, unit.Team));

            ctx.QueryUnitsInLine(unit.Position, dir, length, width, _lineTargets, TargetFilter.Enemies, unit.Team);

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итоговое состояние.
            for (int t = 0; t < _lineTargets.Count; t++)
            {
                ctx.DealDamage(new DamageRequest(unit, _lineTargets[t], raw, damageType, ctx.ArmorK, sourceKind: DamageSourceKind.AutoAttack));
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
