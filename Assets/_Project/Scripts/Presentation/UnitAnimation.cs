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
        public static UnitAnimationState Select(bool isDead, bool attackPlaying, bool isMoving)
        {
            if (isDead)        return UnitAnimationState.Death;
            if (attackPlaying) return UnitAnimationState.Attack;
            return isMoving ? UnitAnimationState.Run : UnitAnimationState.Idle;
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
    }
}
