using Guildmaster.Game.Flow;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Шов «мероприятие заказывает бой» обязан стоять В ТОТ ЖЕ МОМЕНТ, когда родился его владелец, а не
    /// кадром позже.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между <see cref="BattleHost"/> и всеми, кто заказывает бой сразу после открытия
    /// мероприятия, — потому он в тесте, а не в комментарии. Пока привязка жила в <c>IStartable.Start</c>,
    /// её выполнял диспетчер точек входа VContainer на СЛЕДУЮЩЕМ кадре, а узел успевал попросить бой в
    /// текущем: <c>RequestLaunch</c> отвечал «некому», узел уходил в <c>Aborted</c>, и забег обрывался
    /// возвратом в главное меню (наход. Макса 22.08.2026, замер в живой игре: <c>launchBound=false</c>
    /// сразу после <c>ActivityHost.Open</c> и <c>true</c> кадром позже).
    /// </remarks>
    public sealed class BattleSeamBindingTests
    {
        /// <summary>
        /// Только что созданный владелец боя уже отвечает на заказы. Проверяем через перезапуск: он
        /// привязывается той же строкой, что запуск, но не требует ни префаба боевого скоупа, ни сцены.
        /// </summary>
        [Test]
        public void BattleHost_BindsTheSeamWhenItIsBorn_NotOnTheNextFrame()
        {
            var session = new BattleSession();
            Assert.IsFalse(session.CanRestart, "шов занят до того, как владелец боя вообще появился");

            _ = new BattleHost(session, runStates: null, activity: null,
                               battleScopePrefab: null, worldStage: null);

            Assert.IsTrue(session.CanRestart,
                "владелец боя родился, но шов не привязан: значит привязка снова отложена на точку входа, " +
                "и первый же бой, заказанный в кадр открытия мероприятия, уйдёт в Aborted");
        }

        /// <summary>Снос владельца освобождает шов: чужое мероприятие не должно ловить наши заказы.</summary>
        [Test]
        public void Dispose_ReleasesTheSeam()
        {
            var session = new BattleSession();
            var host = new BattleHost(session, runStates: null, activity: null,
                                      battleScopePrefab: null, worldStage: null);

            host.Dispose();

            Assert.IsFalse(session.CanRestart, "владельца боя снесли, а шов остался привязанным к нему");
        }
    }
}
