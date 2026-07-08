namespace Guildmaster.Combat
{
    /// <summary>
    /// Намерение позиционирования, которое мозг (10 Гц) пишет, а <c>MovementSystem</c> (30 Гц) исполняет
    /// (вики «13» §2.7, §4.3). Рантайм-выход решения — не авторинг-данные.
    /// <para>Исполнение Kite/Retreat в движении приходит на шаге 6 фазы; пока мозг пишет интент,
    /// движение по умолчанию трактует всё как <see cref="Approach"/>.</para>
    /// </summary>
    public enum PositioningIntent
    {
        /// <summary>Сближаться с целью до дистанции атаки (поведение Фазы 1).</summary>
        Approach = 0,

        /// <summary>Держать дистанцию в [AttackRange×0.6, AttackRange], атакуя на ходу (требует CanAttackWhileMoving).</summary>
        Kite = 1,

        /// <summary>Отступать от центра масс врагов к своему тылу (HP% ниже порога Retreat).</summary>
        Retreat = 2,
    }
}
