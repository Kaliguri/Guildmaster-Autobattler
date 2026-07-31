using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Интегрирует позиции живых юнитов. Ручная математика — без Rigidbody2D и без Time.deltaTime
    /// (dt передаётся снаружи из <see cref="CombatLoopService"/>). Ветвится по
    /// <see cref="RuntimeUnit.Positioning"/> (§9.7): Approach (Ф1), Kite (полоса дистанции),
    /// Retreat (побег через <see cref="FleeSteering"/>: от центроида врагов к своему тылу, с избеганием стен).
    /// Стрельба на ходу (§9.8) снимает рут замаха.
    /// <para>
    /// Шаг считается в ДВА прохода: сначала все намерения — от общего состояния мира на начало тика,
    /// затем применение. См. <see cref="Tick"/>: одного прохода тут быть не может.
    /// </para>
    /// </summary>
    public sealed class MovementSystem
    {
        // Намеченные за проход позиции (по индексу юнита в списке) — применяются ПОСЛЕ обхода всех.
        private Vector2[] _next = new Vector2[64];

        // Двухрадиусный гистерезис подхода (против троттлинга «бьёт/бежит»):
        //  • ВНЕШНИЙ радиус = reach (полная досягаемость). Пока юнит внутри него — он «в бою»: держит
        //    позицию и бьёт, НЕ пере-подбегает. Это гасит дёрганье, когда расталкивание чуть сдвигает
        //    юнита каждый тик (иначе движение каждый тик тянуло бы его назад → мельтешение Run/Attack).
        //  • ВНУТРЕННИЙ радиус = reach × ApproachStopFactor. Юнит стремится СЮДА, только когда его
        //    вытолкнуло ЗА внешний радиус: подбегает с запасом внутрь, чтобы не выскочить обратно тут же.
        // Движение идёт ПЕРЕД расталкиванием в тике, поэтому запас ещё и страхует гейт атаки от выпихивания.
        // 0.7 → полоса гистерезиса [0.7·reach … reach] ≈ 0.65 ед. при мили-reach ~2.15 (перекрывает толчок
        // сепарации 0.1–0.3, чтобы не пере-подбегать каждый тик). Мельче — снова начинает дёргаться.
        private const float ApproachStopFactor = 0.7f;

        /// <summary>Продвинуть позиции всех живых юнитов на один тик.</summary>
        /// <param name="units">Список всех юнитов в бою.</param>
        /// <param name="dt">Длительность тика (всегда <see cref="SimConstants.TickDelta"/>).</param>
        /// <param name="bounds">Границы поля: итоговая позиция клампится внутрь (<see cref="ArenaBounds.Unbounded"/> = без стен).</param>
        /// <remarks>
        /// ДВА ПРОХОДА, и это принципиально. Пока шаг ложился прямо в <c>Position</c> по ходу обхода, юнит
        /// видел уже сдвинутыми тех, кто стоит в списке раньше него, и нетронутыми — тех, кто позже. Курс
        /// на цель считался от разного состояния мира в зависимости от места в списке, а у зеркальных
        /// сторон это место обратное: левая команда целилась в ещё не сдвинувшегося врага, правая — в уже
        /// сдвинувшегося, вектор выходил чуть круче, и равные команды расходились с первого же тика
        /// (пойман зондом: Y −0.5566 против −0.5543). Считаем намерения от ОДНОГО снимка мира, применяем
        /// после обхода — та же правка, что уже сделана в <c>SeparationSystem</c>, и по той же причине.
        /// <para>
        /// <c>PreviousPosition</c> при расчёте ещё держит позицию начала ПРОШЛОГО тика — на этом стоит
        /// оценка убегания цели (<c>TargetRecedeSpeedPerTick</c>): в первом проходе она читает шаг
        /// прошлого тика, одинаковый для всех, а не «успел ли сосед сходить». Обновляется во втором.
        /// </para>
        /// </remarks>
        public void Tick(List<RuntimeUnit> units, float dt, in ArenaBounds bounds, in SimTuning tuning)
        {
            if (_next.Length < units.Count) _next = new Vector2[units.Count];

            // --- Проход 1: намерения. Мир не меняется, все читают одно и то же состояние. ---
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                _next[i] = unit.Position;   // по умолчанию остаёмся на месте

                // Разбег гаснет вместе с любой причиной не бежать: иначе он залипает на убитом или
                // обездвиженном, и показ считает, что тот всё ещё несётся к цели. Вместе с ним гаснет и
                // заряд удара с разбега: сбитый с ног разгон особого удара не покупает (StopSprint).
                if (unit.IsDead) { unit.StopSprint(); continue; }

                // В полёте (§9.9) юнита двигает DisplacementSystem — сам он не перемещается.
                if (unit.DisplacedTicksRemaining > 0) { unit.StopSprint(); continue; }

                // Контроль (корень/обездвиживание) — стоим на месте (вики «6» §5.3).
                if (!unit.CanMove) { unit.StopSprint(); continue; }

                // «Занят» атакой = замах ИЛИ восстановление (весь бэксвинг, вики «14»): в оба хвоста
                // юнит либо стоит, либо (со «стрельбой на ходу») движется со штрафом. Recovery = 0 у
                // большинства китов → фаза мгновенна, поведение как раньше; ненулевое — у стрелка/комбо.
                // Канал держит юнита так же, как замах: поток льётся с места. «Стрельба на ходу» и здесь
                // остаётся объявленным исключением кита — если однажды появится канальный кит, который
                // умеет идти, он объявит это тем же полем, а не новой веткой.
                // Владелец правила один — RuntimeUnit.IsSwinging. Боевое ожидание (CombatIdle) сюда НЕ
                // входит: между ударами юнит идёт на полной скорости, и это то самое окно, в котором цель
                // успевает разорвать дистанцию со стрелком (2026-07-30/13).
                bool firing            = unit.IsSwinging;
                bool attackWhileMoving = unit.Unit != null && unit.Unit.CanAttackWhileMoving;

                // Въездной замах ДОЕЗЖАЕТ сам себя. Гейт атаки начал его за границей досягаемости именно
                // потому, что остаток дистанции закрывается ходом (CombatPositioning.CanCloseIntoReach);
                // рут здесь оставил бы юнита замахиваться в пустоту в двух шагах от цели. Хвост (Recovery)
                // рутуется как обычно: удар уже случился, дальше вес и остановка.
                //
                // Спрашиваем ChargingIn, а не ChargedSwing: въезжает КАЖДЫЙ подбегающий мили-юнит, чтобы
                // кадр контакта пришёлся на вход в досягаемость, а разбег — это уже про то, каким выйдет
                // сам удар. По ChargedSwing разутыми оставались бы все, кто не успел разогнаться.
                bool chargingIn = unit.ChargingIn && unit.Phase == AttackPhase.Windup;

                // Атака рутит юнита (свинг на месте) — КРОМЕ реликвий со «стрельбой на ходу» (§9.8):
                // те продолжают движение со штрафом скорости.
                if (firing && !attackWhileMoving && !chargingIn) { unit.StopSprint(); continue; }

                // Каст держит на месте так же, как авто-атака (решение Макса по Q9). Исключение объявляет
                // сама способность (`_canMoveWhileCasting`), по образцу «Стрельбы на ходу»: «Марш»
                // Барабанщика — канал, который идёт в движении.
                if (unit.IsCastBusy && !CastAllowsMovement(unit)) { unit.StopSprint(); continue; }

                RuntimeUnit target = unit.CurrentTarget;
                if (target == null) { unit.StopSprint(); continue; }

                // Разбег на заряженном замахе ЗАМОРАЖИВАЕТСЯ, а не пересчитывается: точку старта гейт
                // посчитал по текущей скорости, и гистерезис, погасивший разгон на подъезде (зазор падает
                // ниже SprintExitGap как раз в эти тики), уронил бы скорость на треть — юнит не доехал бы
                // до досягаемости к кадру контакта и ударил вхолостую.
                if (!chargingIn) UpdateSprint(unit, target, firing, in tuning);

                float moveSpeed = unit.Stats.Get(StatType.MoveSpeed);
                // Штраф «стрельбы на ходу» въезду не положен по той же причине: он ломает расчёт въезда.
                if (firing && attackWhileMoving && !chargingIn)
                    moveSpeed *= Mathf.Max(0f, 1f - unit.Unit.MovingAttackSpeedPenaltyPct); // §9.8
                // Прибавка приходит ДОЛЕЙ разгона, а не тумблером: юнит выходит на полную скорость за то же
                // время, за которое показ домешивает клип бега к шагу.
                if (unit.SprintRamp > 0f)
                    moveSpeed *= 1f + (tuning.SprintSpeedMult - 1f) * unit.SprintRamp;

                float maxMove = moveSpeed * dt;
                if (maxMove <= 0f) continue;

                Vector2 moved = unit.Positioning switch
                {
                    PositioningIntent.Kite    => MoveKite(unit, target, maxMove, bounds, in tuning),
                    PositioningIntent.Retreat => MoveRetreat(unit, units, maxMove, in bounds, in tuning),
                    _                         => MoveApproach(unit, target, maxMove, in tuning),
                };

                // Стены арены: не даём кайту/отступлению уйти за поле (вики «15» §7).
                _next[i] = bounds.Clamp(moved);
            }

            // --- Проход 2: применение. Только здесь мир сдвигается. ---
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                // Мёртвыми и летящими владеют другие системы — их интерполяцию не трогаем.
                if (unit.IsDead || unit.DisplacedTicksRemaining > 0) continue;

                unit.PreviousPosition = unit.Position;
                unit.Position         = _next[i];
            }
        }

        /// <summary>
        /// Разбег на дальнем подходе: намерение бежать возникает, когда до цели ЗАЗОР сверх собственной
        /// досягаемости больше входного порога, и пропадает, когда зазор упал ниже выходного. Полоса между
        /// порогами — гистерезис: один порог мигал бы на каждой перебежке (и вместе с ним мигал бы клип).
        /// <para>
        /// Само намерение скоростью НЕ является. Юнит сначала идёт обычным шагом, и только потом
        /// разгоняется — доля <see cref="RuntimeUnit.SprintRamp"/> растёт из того, сколько тиков подряд он
        /// хочет бежать. Разгон, отданный щелчком, читается как телепорт скорости.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Меряем зазор, а не сырую дистанцию: «дальше трёх метров» для мили — начало разбега, а для
        /// стрелка с досягаемостью 8 — уже позиция для стрельбы, и он бежал бы вечно.
        /// <para>
        /// Разбег живёт только в честном подходе: кайтер и отступающий двигаются по своей логике, и
        /// прибавка скорости там означала бы «убегает быстрее, чем должен». В замахе и хвосте — тоже нет:
        /// юнит либо стоит, либо идёт со штрафом «стрельбы на ходу», и разбег бы этот штраф съел.
        /// </para>
        /// </remarks>
        // Разрешает ли ИДУЩИЙ каст движение — спрашиваем у самой способности, а не у юнита: одно и то же
        // существо может иметь и «стоячую» ульту, и канал на ходу.
        private static bool CastAllowsMovement(RuntimeUnit unit)
        {
            int index = unit.CastingAbilityIndex;
            if (index < 0 || index >= unit.Abilities.Count) return false;

            AbilityData data = unit.Abilities[index].Data;
            return data != null && data.CanMoveWhileCasting;
        }

        private static void UpdateSprint(RuntimeUnit unit, RuntimeUnit target, bool firing, in SimTuning tuning)
        {
            // Бросил честный подход (ушёл в кайт/побег) или стреляет на ходу — это не прибытие, а обрыв:
            // вместе с разбегом гаснет и заряд. Врубается тот, кто добежал, а не тот, кто передумал.
            if (firing || unit.Positioning != PositioningIntent.Approach || tuning.SprintSpeedMult <= 1f)
            {
                unit.StopSprint();
                return;
            }

            float reach = CombatPositioning.AttackReachCenter(unit, target, in tuning);
            float gap   = (target.Position - unit.Position).magnitude - reach;

            // Гистерезис — на НАМЕРЕНИИ, а не на скорости: вход по большему порогу, выход по меньшему,
            // между ними держим что было. Так короткая перебежка не мигает разгоном туда-сюда.
            bool wants = unit.SprintWantTicks > 0 ? gap >= tuning.SprintExitGap : gap > tuning.SprintEnterGap;

            if (wants)
            {
                // Намерение непрерывно — копим. Заряд прошлого сближения недействителен: удар с разбега
                // принадлежит тому сближению, которым он и добыт.
                if (unit.SprintWantTicks == 0) unit.ChargedAttackReady = false;
                unit.SprintWantTicks++;
                unit.SprintRamp = tuning.SprintRampAt(unit.SprintWantTicks);

                // Заряд появляется, как только разгон набран ПОЛНОСТЬЮ, — ещё в беге, а не по прибытии.
                // Взвод по прибытии не мог попасть во въездной замах: тот обязан стартовать за границей
                // досягаемости, а прибытие — это уже её граница. Юнит, который прошёл пару шагов и упёрся
                // в цель, по-прежнему добыл обычный удар: полного разгона у него нет.
                if (unit.SprintRamp >= 1f) unit.ChargedAttackReady = true;
                return;
            }

            // Намерение кончилось ПРИБЫТИЕМ — разбег гаснет вместе с зарядом. Добежавший и вставший бьёт
            // ОБЫЧНЫМ ударом: рывок принадлежит тому, кто въехал в досягаемость на ходу, и сохранять заряд
            // прибывшему значило показывать клип рывка у юнита, стоящего на месте.
            unit.StopSprint();
        }

        /// <summary>
        /// Сближение до дистанции атаки (поведение Ф1). Точка остановки — <see cref="CombatPositioning.AttackReachCenter"/>
        /// (та же body-aware метрика, что у гейта автоатаки): движение подводит юнита ровно туда, откуда
        /// автоатака засчитает попадание, поэтому расталкивание не выбивает его из радиуса «вхолостую».
        /// </summary>
        private static Vector2 MoveApproach(RuntimeUnit unit, RuntimeUnit target, float maxMove, in SimTuning tuning)
        {
            float reach       = CombatPositioning.AttackReachCenter(unit, target, in tuning); // внешний радиус
            Vector2 toTarget  = target.Position - unit.Position;
            float distSq      = toTarget.sqrMagnitude;

            // В пределах досягаемости. Обычно — гистерезис: стоим и бьём, не пере-подбегаем (гасит троттлинг,
            // когда расталкивание чуть сдвигает юнита каждый тик). НО если цель убегает так, что рутовый
            // замах ОТСЮДА придётся вхолостую (слой 2), стоять нельзя — иначе гейт атаки не начнёт свинг,
            // и юнит замрёт в reach, отпуская цель. Тогда проваливаемся к сближению и дожимаем дистанцию
            // («сначала подойти ближе с учётом скоростей»), чтобы свинг успел докрутить. Для стоящей/
            // наступающей цели CanLandWindup=true → поведение прежнее, гистерезис сохранён.
            if (distSq <= reach * reach)
            {
                int windup = AttackTiming.WindupTicksFor(unit);
                if (CombatPositioning.CanLandWindup(unit, target, windup, in tuning)) return unit.Position;
            }

            // Сближение к ВНУТРЕННЕМУ радиусу (с запасом внутрь, чтобы не выскочить обратно тут же —
            // и, при догоне убегающего, чтобы рутовый замах докрутил из положения внутри reach).
            float stop = reach * ApproachStopFactor;
            float dist = Mathf.Sqrt(distSq);
            if (dist <= stop) return unit.Position;           // уже ближе внутреннего радиуса — не наезжаем в тело
            return dist - stop <= maxMove
                ? target.Position - toTarget / dist * stop
                : unit.Position + toTarget / dist * maxMove;
        }

        /// <summary>
        /// Кайт (§9.7): держим дистанцию в полосе [FleeDist, FallbackDist] из <see cref="Kite"/> профиля —
        /// отходим (до FallbackDist), если ближе FleeDist; подходим, если дальше FallbackDist; иначе стоим
        /// и стреляем. Провал/пустой контент → деградируем на [AttackRange×0.6, AttackRange] (07 §3.8 B4).
        /// </summary>
        private static Vector2 MoveKite(RuntimeUnit unit, RuntimeUnit target, float maxMove, in ArenaBounds bounds, in SimTuning tuning)
        {
            Kite kite = unit.Unit?.Ai != null ? unit.Unit.Ai.Kite : default;
            float flee     = kite.FleeDist;
            float fallback = kite.FallbackDist;

            // Некорректные/незаданные дистанции (FallbackDist ≤ FleeDist или неположительные) —
            // фолбэк на радиус атаки, чтобы кайт не залипал. Валидатор контента (07 §3.8 C2) поймает
            // такое на авторинге; здесь — безопасная деградация в рантайме.
            if (flee <= 0f || fallback <= flee)
            {
                float range = unit.Stats.Get(StatType.AttackRange);
                flee     = range * tuning.KiteFleeFactor;
                fallback = range;
            }

            Vector2 toTarget = target.Position - unit.Position;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f) return unit.Position;

            Vector2 dir = toTarget / dist;
            if (dist < flee)
                // Ближе FleeDist — отходим до FallbackDist. Направление и обработку стены/дуги ведёт
                // FleeSteering (радиальный уход от цели + боковой уход + скольжение у стены), полосу
                // держит кап шага: не убегаем дальше FallbackDist.
                return FleeSteering.KiteFlee(unit.Position, -dir, dir, Mathf.Min(maxMove, fallback - dist), in bounds, in tuning);
            if (dist > fallback)
                return unit.Position + dir * Mathf.Min(maxMove, dist - fallback);  // дальше FallbackDist — подходим

            return unit.Position;   // в полосе [FleeDist, FallbackDist] — стоим (атакуем на ходу)
        }

        /// <summary>
        /// Отступление (§9.7): побег ведёт <see cref="FleeSteering"/> — отталкивание от центроида врагов +
        /// притяжение к своему тылу (по <see cref="RuntimeUnit.Team"/>) + превентивное избегание стен, со
        /// скольжением вдоль стены при заклинивании. Детерминизм — вся математика в FleeSteering, без RNG.
        /// </summary>
        private static Vector2 MoveRetreat(RuntimeUnit unit, List<RuntimeUnit> units, float maxMove, in ArenaBounds bounds, in SimTuning tuning)
            => FleeSteering.Retreat(unit, units, maxMove, in bounds, in tuning);
    }
}
