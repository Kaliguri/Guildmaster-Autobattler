using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Экземпляр эффекта на конкретном юните (POCO, живёт один бой). Несёт per-unit рантайм-
    /// состояние, которого НЕ может быть в общих <c>[SerializeReference]</c>-компонентах
    /// <see cref="EffectData"/> — те шарятся между всеми носителями эффекта и обязаны быть
    /// stateless (вики «12» §2.2, «6» §5).
    /// </summary>
    public sealed class RuntimeEffect : IModifierSource
    {
        /// <summary>Иммутабельное определение.</summary>
        public EffectData Def;

        /// <summary>
        /// Имя, под которым эффект показывается игроку в разборе стата («+12 (Ярость)»).
        /// Эффект — основной источник стат-модификаторов в бою, поэтому именно он делает
        /// разбор читаемым; безымянные источники в тултипе схлопываются в «прочее».
        /// </summary>
        public string ModifierSourceLocKey => ContentKeys.NameKey(Def);

        /// <summary>Кто наложил ПЕРВЫМ — скейл потенции (снимок заморожен, вики «11» §5.1) и триггеры.</summary>
        public RuntimeUnit Source;

        /// <summary>
        /// Кто и сколько раз подкрепил этот эффект — по нему делится атрибуция периодики (реш. Макса
        /// 2026-07-26: «делить пропорционально вкладу в стаки»). Экземпляр эффекта живёт ОДИН на цели,
        /// поэтому вопрос «чей это урон» решается здесь, а не разведением экземпляров: горение, которое
        /// вдвоём поддерживают двое, приносит каждому свою половину тика.
        /// <para>Два параллельных списка вместо словаря: вкладчиков единицы, порядок = порядок наложения,
        /// а значит обход детерминирован — на нём стоит чек-сумма.</para>
        /// </summary>
        public readonly System.Collections.Generic.List<RuntimeUnit> ContributorSources = new();

        /// <summary>Вес вкладчика: сколько раз он наложил или подкрепил эффект. Параллелен <see cref="ContributorSources"/>.</summary>
        public readonly System.Collections.Generic.List<int> ContributorWeights = new();

        /// <summary>Сумма весов — знаменатель доли. 0 = вкладчиков нет (эффект собран вручную в тесте).</summary>
        public int TotalContribution
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < ContributorWeights.Count; i++) sum += ContributorWeights[i];
                return sum;
            }
        }

        /// <summary>Засчитать наложение/подкрепление источнику: первое — заводит вкладчика, повторное — растит вес.</summary>
        public void AddContribution(RuntimeUnit source)
        {
            for (int i = 0; i < ContributorSources.Count; i++)
            {
                if (ReferenceEquals(ContributorSources[i], source)) { ContributorWeights[i]++; return; }
            }
            ContributorSources.Add(source);
            ContributorWeights.Add(1);
        }

        /// <summary>
        /// Остаток длительности в тиках. <c>-1</c> = постоянный (пассивка), <c>0</c> = мгновенный.
        /// Пишется только глаголами ниже: у длительности и её полной величины один вход, поэтому
        /// «поставить срок» и «дожить тик» невозможно перепутать местами.
        /// </summary>
        public int RemainingTicks { get; private set; }

        /// <summary>Полная длительность в тиках на момент наложения (для StackRule.Refresh).</summary>
        public int FullDurationTicks { get; private set; }

        /// <summary>
        /// Назначить срок: и остаток, и полную длительность. Один глагол на наложение и на подкрепление —
        /// они всегда ставят оба числа вместе, и разъехаться им нечем.
        /// </summary>
        public void SetDuration(int ticks)
        {
            RemainingTicks    = ticks;
            FullDurationTicks = ticks;
        }

        /// <summary>Прожить тик. Возвращает <c>true</c>, если срок вышел и эффект пора снимать.</summary>
        public bool TickDownDuration()
        {
            RemainingTicks--;
            return RemainingTicks <= 0;
        }

        /// <summary>
        /// Срок вышел досрочно: эффект снимается штатным путём, а не отдельной веткой. Так уходят «Угли»,
        /// когда осыпался последний стак.
        /// </summary>
        public void EndDuration() => RemainingTicks = 0;

        /// <summary>
        /// Тик, на котором эффект ПОЯВИЛСЯ на цели. Ставится один раз, при создании; рефреш и набор стака
        /// его не двигают — речь о появлении, а не об обновлении.
        /// <para><b>Зачем:</b> снятие обязано судить по состоянию НАЧАЛА тика. Список
        /// <see cref="RuntimeUnit.ActiveEffects"/> меняется немедленно (отложены только статы и маска
        /// тегов), поэтому диспел без этого поля видел бы наложения того же тика — и исход зависел бы от
        /// места юнита в обходе. Пойман зеркалом на тике 181: метку, только что наложенную одним
        /// Рейнджером, чужой клинс успевал снять у одной стороны и не успевал у другой.</para>
        /// </summary>
        public int AppliedTick;

        /// <summary>
        /// Счётчик срабатываний для компонентов, которым нужен «каждый N-й раз» («каждая третья атака»
        /// Драугра). Живёт здесь, потому что компоненты stateless и шарятся между носителями, а счёт
        /// принадлежит КОНКРЕТНОМУ экземпляру эффекта на конкретном юните.
        /// </summary>
        public int Counter;

        /// <summary>
        /// Текущее число стаков (≥ 0). ЖИВОЕ значение — оно уже включает всё, что этот тик успел набрать
        /// и срезать, поэтому влиять на исход по нему нельзя: для этого есть <see cref="VisibleStacks"/>.
        /// Пишется только методами этого класса — снаружи сеттера нет намеренно, см. ниже.
        /// </summary>
        public int Stacks { get; private set; } = 1;

        /// <summary>
        /// Число стаков, с которым эффект ВОШЁЛ в текущий тик. Обновляется одним проходом в
        /// <c>EffectSystem.CommitPending</c> — больше нигде.
        /// <para><b>Зачем:</b> закон видимости эффектов (см. <c>EffectSystem.CommitTickChanges</c>) до
        /// этого поля накрывал статы и маску тегов, но не стаки — а их правят посреди тика. Пойман
        /// зеркалом на тике 300: клинс союзного пастыря съедал 5 стаков «Углей» ЦЕНОЙ очищения, и у
        /// левой стороны свой пастырь успевал сделать это ДО хода вражеского мечника, а у правой —
        /// после. Мечники детонировали 18 и 13 стаков: 354 против 244,8 урона при одинаковом составе
        /// эффектов.</para>
        /// </summary>
        public int StacksAtTickStart { get; private set; } = 1;

        /// <summary>
        /// Стаки, которыми эффект ВЛИЯЕТ на исход прямо сейчас: снимок начала тика, а у эффекта, который
        /// родился внутри этого тика (снимка ещё нет), — его настоящее число. Единственный владелец этого
        /// правила: читатели стаков зовут его, а не собирают условие сами.
        /// </summary>
        public int VisibleStacks => StacksAtTickStart > 0 ? StacksAtTickStart : Stacks;

        /// <summary>
        /// Набрать стаки (подкрепление эффекта). Потолок держит вызывающий: он знает
        /// <see cref="EffectData.MaxStacks"/> и правило слияния.
        /// </summary>
        public void AddStacks(int count)
        {
            if (count > 0) Stacks += count;
        }

        /// <summary>Срезать стаки (цена очищения, сход «Углей»). Ниже нуля не уходит.</summary>
        public void RemoveStacks(int count)
        {
            if (count <= 0) return;
            Stacks = count >= Stacks ? 0 : Stacks - count;
        }

        /// <summary>
        /// Проявить набранное и срезанное за тик: снимок догоняет живое число. Единственная точка, где
        /// это происходит, — конец тика (<c>EffectSystem.CommitPending</c>). Отдельный метод, а не
        /// присваивание снаружи: так у границы тика один владелец, и её нельзя сдвинуть случайно.
        /// </summary>
        public void CommitStackSnapshot() => StacksAtTickStart = Stacks;

        /// <summary>
        /// Снимок потенции на компонент (параллельно <see cref="EffectData.Components"/>),
        /// резолвится из статов источника при наложении: per-second rate для DoT/HoT, величина
        /// щита и т.п. Храним rate-per-second, НЕ запечённый total (вики «11» §5.1).
        /// </summary>
        public float[] ScaledPotency;

        /// <summary>
        /// Счётчик сим-тиков с прошлого срабатывания на периодический компонент (параллельно
        /// компонентам). Целочисленный — float-аккумулятор дрейфует и ломает детерминизм периодики.
        /// </summary>
        public int[] PeriodicTicks;

        /// <summary>
        /// Сколько щита этот эффект УДЕРЖИВАЕТ прямо сейчас (§9.3, «Оплот»: <c>flat + %·недостающее HP</c>) —
        /// фактическая величина с runtime-расчётом, потому что из статов её не выразить.
        /// <para>Единственный владелец числа: снятие обязано убрать ровно удержанное, а не пересчитать
        /// формулу заново, и взрыв щита обязан считать урон от того же числа. Пишется только глаголами
        /// ниже — сеттера снаружи нет, чтобы у величины не завелось второго владельца.</para>
        /// </summary>
        public float HeldShield { get; private set; }

        /// <summary>Держать ровно столько щита (поднятие: величина известна целиком).</summary>
        public void HoldShield(float amount) => HeldShield = amount > 0f ? amount : 0f;

        /// <summary>Накопить щит сверх удерживаемого (сбор по ходу боя: «Стальной вихрь»).</summary>
        public void AddHeldShield(float amount)
        {
            if (amount > 0f) HeldShield += amount;
        }

        /// <summary>
        /// Отпустить удерживаемое: вернуть величину и обнулить счёт. Один глагол на все три случая, где
        /// щит перестаёт держаться (истечение, снятие, взрыв), — поэтому «прочитал и забыл забыть»
        /// невозможно по построению.
        /// </summary>
        public float ReleaseHeldShield()
        {
            float held = HeldShield;
            HeldShield = 0f;
            return held;
        }

        /// <summary>
        /// Заряды реактив-компонента (§9.4, «Изворотливость», «Оплот»): на каждый заряд — абсолютный тик
        /// готовности (≤ CurrentTick = готов). Независимая перезарядка. null у эффектов без зарядов.
        /// <para>Приватный намеренно: у публичного массива элементы правит кто угодно, а «найти готовый
        /// заряд и потратить его» — правило, а не строчка. Оно жило копией в двух компонентах.</para>
        /// </summary>
        private int[] _chargeReadyTicks;

        /// <summary>Сколько зарядов взведено (0 = компонент зарядами не пользуется).</summary>
        public int ChargeCount => _chargeReadyTicks?.Length ?? 0;

        /// <summary>Абсолютный тик готовности конкретного заряда — для диагностики и тестов.</summary>
        public int ChargeReadyTick(int index)
            => _chargeReadyTicks != null && index >= 0 && index < _chargeReadyTicks.Length
                ? _chargeReadyTicks[index]
                : 0;

        /// <summary>
        /// Взвести заряды: все стартуют ГОТОВЫМИ (тик готовности 0 не больше любого текущего). Зовётся
        /// при наложении и при подкреплении, которое взводит заново.
        /// </summary>
        public void ArmCharges(int count)
        {
            _chargeReadyTicks = new int[count > 0 ? count : 1];
        }

        /// <summary>
        /// Потратить один готовый заряд и отправить его на перезарядку. <c>false</c> = готовых нет,
        /// реакция не случается.
        /// </summary>
        /// <remarks>
        /// Первый готовый по порядку, а не «самый свежий»: порядок обхода фиксирован, поэтому расход
        /// зарядов детерминирован и одинаков у зеркальных сторон. Перезарядка у каждого своя — считается
        /// от текущего тика, без потиковых декрементов.
        /// </remarks>
        public bool TryConsumeCharge(int currentTick, int rechargeTicks)
        {
            if (_chargeReadyTicks == null) return false;

            for (int i = 0; i < _chargeReadyTicks.Length; i++)
            {
                if (_chargeReadyTicks[i] > currentTick) continue;   // ещё перезаряжается
                _chargeReadyTicks[i] = currentTick + (rechargeTicks > 0 ? rechargeTicks : 1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Служебный таймер компонента: абсолютный тик, на котором ему пора сработать. Нужен там, где
        /// собственный ритм эффекта не совпадает ни с длительностью, ни с периодом тика — например
        /// сход «Углей» по ускоряющейся кривой. Сверяется с <c>ctx.Combat.CurrentTick</c>.
        /// </summary>
        public int TimerTick { get; private set; }

        /// <summary>Текущий шаг служебного таймера в тиках (для кривых, где интервал меняется по ходу).</summary>
        public int TimerIntervalTicks { get; private set; }

        /// <summary>
        /// Пора сработать? Абсолютный тик против текущего — без потиковых декрементов, иначе таймер
        /// зависел бы от того, сколько раз его успели опросить.
        /// </summary>
        public bool IsTimerDue(int currentTick) => currentTick >= TimerTick;

        /// <summary>
        /// Взвести таймер: сработать на <paramref name="dueTick"/>, следующий шаг — <paramref name="intervalTicks"/>.
        /// Оба числа ставятся вместе, потому что порознь они бессмысленны: срок без шага не знает, когда
        /// прозвонит в следующий раз.
        /// </summary>
        public void ScheduleTimer(int dueTick, int intervalTicks)
        {
            TimerTick          = dueTick;
            TimerIntervalTicks = intervalTicks > 0 ? intervalTicks : 1;
        }

        /// <summary>
        /// Перевзвести на свой же шаг и назначить шаг для следующего раза: сначала ждём текущий интервал,
        /// и только потом укорачиваем. Иначе первый звонок после льготного окна уезжает уже с
        /// множителем, и заявленный первый интервал не отрабатывает ни разу.
        /// </summary>
        public void RescheduleTimer(int currentTick, int nextIntervalTicks)
            => ScheduleTimer(currentTick + TimerIntervalTicks, nextIntervalTicks);

        /// <summary>Постоянный эффект (пассивка) — не истекает по таймеру.</summary>
        public bool IsPermanent => RemainingTicks < 0;
    }
}
