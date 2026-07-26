using System.Collections.Generic;
using Guildmaster.Core.Flow;
using Guildmaster.Presentation.Transition;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Переход между кадрами обязан ДОЖИТЬ до конца, даже когда тот, кто его заказал, уходит на закрытом
    /// кадре. Так родился QA #53: три фазы вела карта акта, выбор узла уводил игрока с карты — и карта
    /// обрывала собственное моргание на пике, оставляя от него одно закрытие.
    /// </summary>
    public sealed class ScreenTransitionRunnerTests
    {
        // Ловим то, что шторка вещает наружу: плотность и точку схлопывания.
        private sealed class FadeSpy : IPublisher<ScreenFadeChangedEvent>
        {
            public readonly List<ScreenFadeChangedEvent> Events = new List<ScreenFadeChangedEvent>();
            public void Publish(ScreenFadeChangedEvent message) => Events.Add(message);
        }

        // Ровные фазы по 0.2с при шаге 0.05с: закрытие занимает 4 шага, выдержка — 4, открытие — 4.
        private const float Step = 0.05f;

        private static ScreenTransitionShape Shape(Vector2 focus)
            => new ScreenTransitionShape(0.2f, 0.2f, 0.2f, focus);

        private static void Tick(ScreenTransitionRunner runner, int steps)
        {
            for (int i = 0; i < steps; i++) runner.Tick(Step);
        }

        [Test]
        public void Play_ClosesHoldsAndOpens_EvenWhenRequesterLeavesOnCoveredFrame()
        {
            var spy = new FadeSpy();
            var runner = new ScreenTransitionRunner(spy);

            int covered = 0;
            runner.Play(Shape(new Vector2(0.5f, 0.5f)), null, () => covered++);

            // Заказчик на закрытом кадре «уходит»: раньше это же место и убивало переход.
            Tick(runner, 4);
            Assert.That(covered, Is.EqualTo(1), "подмена делается ровно раз, на закрытом кадре");
            Assert.That(spy.Events[spy.Events.Count - 1].Progress, Is.EqualTo(1f).Within(0.001f));
            Assert.That(runner.Busy, Is.True, "выдержка и открытие ещё впереди");

            Tick(runner, 3); // выдержка
            Assert.That(spy.Events[spy.Events.Count - 1].Progress, Is.EqualTo(1f).Within(0.001f),
                        "на выдержке кадр остаётся закрытым");

            Tick(runner, 5); // добрать выдержку и открыться
            Assert.That(runner.Busy, Is.False);
            Assert.That(spy.Events[spy.Events.Count - 1].Progress, Is.EqualTo(0f).Within(0.001f),
                        "кадр открылся до конца, а не застыл на полпути");
        }

        [Test]
        public void Play_ReachesFullDarkness_BeforeCoveredCallback()
        {
            var spy = new FadeSpy();
            var runner = new ScreenTransitionRunner(spy);

            float progressWhenCovered = -1f;
            runner.Play(Shape(new Vector2(0.2f, 0.8f)), null,
                        () => progressWhenCovered = spy.Events[spy.Events.Count - 1].Progress);

            Tick(runner, 4);

            Assert.That(progressWhenCovered, Is.EqualTo(1f).Within(0.001f),
                        "подменять то, что под шторкой, можно только когда она закрыта наглухо");
        }

        [Test]
        public void Closing_MovesCollapsePoint_FromNodeToScreenCenter()
        {
            var spy = new FadeSpy();
            var runner = new ScreenTransitionRunner(spy);

            var node = new Vector2(0.15f, 0.75f);
            runner.Play(Shape(node), null, null);

            Assert.That(spy.Events[0].Center, Is.EqualTo(node), "начинаем схлопываться там, куда ткнул игрок");

            Tick(runner, 4);

            ScreenFadeChangedEvent closed = spy.Events[spy.Events.Count - 1];
            Assert.That(closed.Center.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(closed.Center.y, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Closing_ReportsProgress_ToRequester()
        {
            var spy = new FadeSpy();
            var runner = new ScreenTransitionRunner(spy);

            var seen = new List<float>();
            runner.Play(Shape(new Vector2(0.5f, 0.5f)), seen.Add, null);

            Tick(runner, 4);

            Assert.That(seen.Count, Is.GreaterThan(1), "наезд камеры идёт в ногу с закрытием, а не одним рывком");
            Assert.That(seen[seen.Count - 1], Is.EqualTo(1f).Within(0.001f));
            Assert.That(seen, Is.Ordered, "ход закрытия только вперёд");
        }

        [Test]
        public void Play_WhileBusy_IsIgnored()
        {
            var spy = new FadeSpy();
            var runner = new ScreenTransitionRunner(spy);

            int firstCovered = 0, secondCovered = 0;
            runner.Play(Shape(new Vector2(0.5f, 0.5f)), null, () => firstCovered++);
            runner.Play(Shape(new Vector2(0.1f, 0.1f)), null, () => secondCovered++);

            Tick(runner, 14);

            Assert.That(firstCovered, Is.EqualTo(1));
            Assert.That(secondCovered, Is.Zero, "у шторки один хозяин — тот, кто её начал");
        }

        [Test]
        public void Cancel_OpensFrameImmediately()
        {
            var spy = new FadeSpy();
            var runner = new ScreenTransitionRunner(spy);

            runner.Play(Shape(new Vector2(0.5f, 0.5f)), null, null);
            Tick(runner, 1);
            runner.Cancel();

            Assert.That(runner.Busy, Is.False);
            Assert.That(spy.Events[spy.Events.Count - 1].Progress, Is.EqualTo(0f).Within(0.001f),
                        "чернила не остаются на экране, когда мира под ними уже нет");
        }
    }
}
