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
    }
}
