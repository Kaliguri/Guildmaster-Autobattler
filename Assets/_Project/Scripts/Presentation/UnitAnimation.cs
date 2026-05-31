namespace Guildmaster.Presentation
{
    /// <summary>Состояние кадровой анимации вида юнита. Порядок стабилен (не использовать как сериализуемый индекс контента).</summary>
    public enum UnitAnimationState
    {
        Idle   = 0,
        Run    = 1,
        Attack = 2,
        Hit    = 3,
        Death  = 4,
    }

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
        /// <param name="hitRemaining">Остаток разовой «получил урон», сек (&gt;0 = играет).</param>
        /// <param name="attackRemaining">Остаток разовой «атака», сек (&gt;0 = играет).</param>
        /// <param name="isMoving">Позиция изменилась за тик (Position != PreviousPosition сверх эпсилона).</param>
        public static UnitAnimationState Select(bool isDead, float hitRemaining, float attackRemaining, bool isMoving)
        {
            if (isDead)              return UnitAnimationState.Death;   // латч, перекрывает всё
            if (hitRemaining   > 0f) return UnitAnimationState.Hit;     // флинч прерывает атаку/бег
            if (attackRemaining > 0f) return UnitAnimationState.Attack; // атака перекрывает бег
            return isMoving ? UnitAnimationState.Run : UnitAnimationState.Idle;
        }
    }
}
