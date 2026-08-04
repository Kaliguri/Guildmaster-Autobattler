using Guildmaster.Data.Definitions;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Чистая проекция «состояние сима → состояние анимации» (спайк S4, вики «13» §5).
    /// <para>
    /// Ключевой инвариант развязки: функция читает ТОЛЬКО наблюдаемое состояние сима
    /// (мертвость, движение) и таймеры разовых анимаций, заведённые от событий сим-тика
    /// (получen урон / совершил атаку). Она ничего не возвращает в симуляцию и не зависит
    /// от wall-clock. Поэтому два клиента с идентичным состоянием сима выбирают одинаковую
    /// анимацию — анимация не может внести рассинхрон (урон уже нанесён на тике, кадр косметичен).
    /// </para>
    /// Чистая и без Unity-типов → проверяется EditMode-тестом без рендера.
    /// </summary>
    public static class UnitAnimationSelector
    {
        /// <summary>
        /// Выбрать состояние анимации.
        /// </summary>
        /// <param name="isDead">Юнит помечен мёртвым симуляцией.</param>
        /// <param name="attackPlaying">Играет клип атаки: идёт замах (windup) или фоллоу-сру после удара (вики «14»).</param>
        /// <param name="isMoving">Позиция изменилась за тик (Position != PreviousPosition сверх эпсилона).</param>
        /// <param name="canAct">Дееспособен. false = выведен контролем → оглушение важнее любой локомоции.</param>
        /// <param name="isSprinting">Идёт разбегом (признак симуляции, не наблюдаемая скорость).</param>
        /// <param name="chargedAttack">Свинг идёт с разбега — у него свой клип.</param>
        /// <remarks>
        /// Порядок ветвей — это приоритет состояний, и он не произволен. Оглушение стоит выше атаки:
        /// сим замах при контроле прерывает, но между прерыванием и следующим снимком показ не должен
        /// успеть показать удар, которого уже нет. Разбег стоит НИЖЕ атаки по той же причине, по которой
        /// он гаснет в симе на замахе: юнит, начавший бить, больше не бежит.
        /// </remarks>
        /// <param name="hasTarget">
        /// У юнита есть цель — то есть он В БОЮ, а не отдыхает. Разводит две стойки: <c>CombatIdle</c>
        /// (оружие наготове) и <c>Idle</c> (клинок опущен). Это НЕ вывод показа: цель назначает сим, и
        /// показ читает её из снимка тем же способом, что и всё остальное.
        /// <para>Спрашивать здесь <c>AttackPhase</c> было бы неверно: фаза описывает свинг, а «в бою ли
        /// юнит» — другой вопрос. Боец, бегущий к врагу через всю арену, не машет и не отдыхает.</para>
        /// </param>
        public static UnitAnimationState Select(bool isDead, bool attackPlaying, bool isMoving,
            bool canAct = true, bool isSprinting = false, bool chargedAttack = false,
            bool hasTarget = false)
        {
            if (isDead)  return UnitAnimationState.Death;
            if (!canAct) return UnitAnimationState.Stun;
            if (attackPlaying)
                return chargedAttack ? UnitAnimationState.AttackCharge : UnitAnimationState.Attack;
            if (!isMoving) return hasTarget ? UnitAnimationState.CombatIdle : UnitAnimationState.Idle;
            return isSprinting ? UnitAnimationState.Sprint : UnitAnimationState.Run;
        }

        /// <summary>
        /// Играть ли поверх локомоции клип атаки (замах/удар/хвост). Разводит два случая — и именно
        /// смешение их через сырое «сдвинулся ли юнит» ломало картинку (свинг рвался в Run от толчка
        /// сепарации; у преследователя не рисовались замах/хвост — только момент удара):
        /// <list type="bullet">
        /// <item><b>Сим реально в свинге</b> (<paramref name="simInSwing"/> = Windup/Recovery): мили-юнит
        /// зарутован симом, любое смещение — это толчок <c>SeparationSystem</c>, а НЕ бег. Клип атаки
        /// показываем ВСЕГДА, игнорируя <paramref name="isMoving"/>.</item>
        /// <item><b>Свинга нет, но рендер тянет хвост-цикл</b> до следующего удара
        /// (<paramref name="attackCycleActive"/>): смещение здесь = настоящая локомоция. Держим клип атаки,
        /// только если юнит стоит (боец на месте лупит атаку бесшовно) — иначе преследователь в паузе
        /// между ударами бежит. Стрельба на ходу (<paramref name="canAttackWhileMoving"/>) всегда «в атаке».</item>
        /// </list>
        /// </summary>
        public static bool AttackClipPlaying(bool attackCycleActive, bool simInSwing, bool canAttackWhileMoving, bool isMoving)
        {
            if (!attackCycleActive) return false;
            return simInSwing || canAttackWhileMoving || !isMoving;
        }

        /// <summary>
        /// Прогресс скраба клипа [0..1] по счётчику сим-тиков — с учётом доли ВНУТРИ показываемого тика.
        /// <para><b>Зачем дробность.</b> Счётчики снимка (<c>WindupRemaining</c>, <c>AttackCooldownTicks</c>)
        /// целые и меняются <c>TickRate</c> раз в секунду. Скраб по ним даёт клипу ровно столько положений:
        /// при замахе в 6 тиков — 6 поз на весь замах, сколько бы кадров ни рисовал рендер. Это и читается
        /// как «визуал в 30 Гц», хотя частота сима тут ни при чём — тем же <c>alpha</c>, которым уже
        /// интерполируется позиция, поза течёт непрерывно при любом <c>TickRate</c>.</para>
        /// <para><b>Почему <c>+1</c>.</b> Счётчик в снимке — значение на КОНЕЦ тика, а
        /// <paramref name="frameAlpha"/> = 0 означает его начало (как и у позиции: <c>alpha</c> = 1 — это
        /// <c>Position</c>, конец тика). Значит на показанный момент осталось
        /// <c>ticksLeft + (1 − alpha)</c> тиков.</para>
        /// </summary>
        /// <param name="ticksLeft">Сколько тиков осталось на конец показываемого тика.</param>
        /// <param name="totalTicks">Длина окна в тиках (замах целиком / промежуток до следующего замаха).</param>
        /// <param name="frameAlpha">Доля внутри показываемого тика [0..1].</param>
        public static float ScrubProgress(int ticksLeft, int totalTicks, float frameAlpha)
        {
            if (totalTicks <= 0) return 0f;

            float remaining = ticksLeft + 1f - frameAlpha;
            float progress  = 1f - remaining / totalTicks;
            return progress < 0f ? 0f : (progress > 1f ? 1f : progress);
        }

        /// <summary>
        /// Начался ли НОВЫЙ взмах: клип свинга всегда идёт вперёд (замах к контакту, хвост к концу), поэтому
        /// откат времени назад означает следующий удар.
        /// </summary>
        /// <param name="previous">Где стоял скраб в прошлом кадре; отрицательное = свинг не играл.</param>
        /// <param name="current">Где он стоит сейчас.</param>
        /// <param name="drop">
        /// Насколько велик должен быть откат. Порог, а не любое уменьшение: между кадрами время клипа
        /// дрожит на доли процента от интерполяции тика, а настоящий откат — прыжок с конца клипа в начало.
        /// </param>
        /// <remarks>
        /// Признак нужен потому, что смены ФАЗЫ для этого недостаточно: у кита, чей следующий замах
        /// начинается в тот же тик, где кончился прошлый удар, фаза замаха не прерывается ни на кадр. Пока
        /// показ ориентировался на неё, точка A оставалась снятой с первого взмаха, и дуга за клинком
        /// заказывалась ровно один раз за бой (04.08.2026).
        /// </remarks>
        public static bool IsNewSwing(float previous, float current, float drop) =>
            previous >= 0f && current < previous - drop;

        /// <summary>
        /// Позиция клипа свинга внутри КАНАЛА авто-атаки [0..1). Поток бьёт не один раз, а каждый свой тик,
        /// и кадр контакта обязан прийтись на каждый из них — поэтому клип не скрабится «замах → хвост», а
        /// крутится циклом от маркера через конец обратно к маркеру: один оборот = один период между
        /// ударами.
        /// <para>Пропорцию «хвост / замах» внутри оборота задаёт положение маркера в клипе, и это не
        /// упущение: отдельных замаха и хвоста у тиков потока не существует — они есть только у канала
        /// целиком, на входе в него и на выходе.</para>
        /// </summary>
        /// <param name="markerNormalized">Доля клипа до кадра контакта.</param>
        /// <param name="ticksLeft">Тиков до следующего удара потока.</param>
        /// <param name="periodTicks">Период между ударами потока в тиках.</param>
        /// <param name="frameAlpha">Доля внутри показываемого тика [0..1].</param>
        /// <remarks>
        /// <b>Остаток берётся по модулю периода, и это не перестраховка.</b> Счётчик потока перезаряжается
        /// В ТОМ ЖЕ тике, в котором бьёт, поэтому снимок тика удара несёт уже полный период — нуля, на
        /// который встаёт замах (<c>WindupRemaining</c>), здесь не бывает вовсе. Без модуля этот тик
        /// начинал бы новый оборот с нуля, и клип прыгал бы на кадр контакта, не дойдя до него последние
        /// 1/период клипа: удар выглядел бы обрубленным ровно в момент удара.
        /// </remarks>
        public static float ChannelClipTime(float markerNormalized, int ticksLeft, int periodTicks,
            float frameAlpha)
        {
            if (periodTicks <= 0) return markerNormalized;

            float t = markerNormalized + ScrubProgress(ticksLeft % periodTicks, periodTicks, frameAlpha);
            return t >= 1f ? t - 1f : t;
        }
    }
}
