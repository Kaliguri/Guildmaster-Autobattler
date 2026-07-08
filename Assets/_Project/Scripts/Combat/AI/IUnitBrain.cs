namespace Guildmaster.Combat
{
    /// <summary>
    /// Главный шов AI (вики «13» §3.1): единственная точка, где принимается «дорогое» решение
    /// «кого/где». Детерминирован — читает мир через <see cref="IBattleView"/>, пишет ТОЛЬКО интент
    /// на самого юнита (<c>CurrentTarget</c>/<c>AutoAttackTarget</c>/<c>Positioning</c>), не мутируя сим.
    /// <para>Cast-решение здесь НЕ принимается — оно дешёвое и проверяется per-tick в AbilitySystem.</para>
    /// <para>v1-реализация — единственная (<c>ProfileBrain</c>). Шов оставлен под будущую подмену
    /// (граф-примитивы / пер-архетип) без правок интеграции в сим (§2.1).</para>
    /// </summary>
    public interface IUnitBrain
    {
        /// <summary>Переоценить решение юнита по текущему срезу боя. Пишет интент на <paramref name="self"/>.</summary>
        void Decide(RuntimeUnit self, IBattleView view);
    }
}
