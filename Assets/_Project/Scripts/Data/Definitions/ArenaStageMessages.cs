namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Покажи место боя»: игрок вошёл в узел, арену пора явить. Публикует владелец боевой расстановки
    /// (<c>DeploymentController</c>), исполняет презентер арены — он и решает, играть полную смену облика
    /// или короткий всполох, если облик тот же.
    /// <para>Событие несёт ЧТО показать, а не КАК: подача (сколько актов, какого цвета, с какой скоростью)
    /// живёт в презентации и меняется без правок боевого потока.</para>
    /// </summary>
    public readonly struct ArenaRevealRequest
    {
        /// <summary>Облик арены этого узла. Пусто — оставить текущий (пока у узлов один облик на всех).</summary>
        public readonly string SkinId;

        /// <summary>Показать «как есть», без анимации: загрузка сейва, dev-запуск.</summary>
        public readonly bool Instant;

        public ArenaRevealRequest(string skinId, bool instant = false)
        {
            SkinId  = skinId;
            Instant = instant;
        }
    }
}
