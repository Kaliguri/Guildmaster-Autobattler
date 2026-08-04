using Guildmaster.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Контракт раскладки призыва. Держит инвариант, живущий между файлами: раскладку зовут и способность
    /// призыва, и стартовый отряд, а нарушить её может третий вызывающий, который о зеркале не думал.
    /// </summary>
    /// <remarks>
    /// Сторож зеркала (<c>MirrorMatchTests</c>) ловит нарушение тоже, но отвечает поздно и косвенно:
    /// «дуэль Некроманта разошлась на тике 7 по позиции X» — оттуда до раскладки идти через выбор цели и
    /// расталкивание. Здесь тот же дефект виден одной строкой.
    /// </remarks>
    public sealed class SummonLayoutTests
    {
        private static RuntimeUnit Summoner(int team) => new RuntimeUnit { Team = team };

        /// <summary>
        /// Отражённые призыватели обязаны разложить тела зеркально: X противоположен, Y совпадает. Прежняя
        /// редакция считала смещение одинаковым для обеих команд, и единственное тело левого уходило за
        /// спину, а правого — вперёд, между хозяином и врагом.
        /// </summary>
        [Test]
        public void Offset_IsMirrored_BetweenTeams([Values(0, 1, 2, 3, 4)] int index)
        {
            const float Step = 0.8f;

            Vector2 left  = SummonLayout.Offset(index, Step, Summoner(0));
            Vector2 right = SummonLayout.Offset(index, Step, Summoner(1));

            Assert.That(left.x, Is.EqualTo(-right.x).Within(1e-5f), "X обязан менять знак вместе со стороной");
            Assert.That(left.y, Is.EqualTo(right.y).Within(1e-5f), "Y у отражённых сторон совпадает");
        }

        /// <summary>Тело встаёт ЗА спиной — в сторону своего тыла, а не навстречу врагу.</summary>
        [Test]
        public void Offset_StepsBack_TowardTheOwnRear([Values(0, 1)] int team)
        {
            Vector2 offset = SummonLayout.Offset(0, 1f, Summoner(team));
            Vector2 home = FleeSteering.HomeDir(Summoner(team));

            Assert.That(Vector2.Dot(offset, home), Is.GreaterThan(0f), "отход считается от тыла команды");
        }

        /// <summary>Тела одного призыва не садятся друг в друга: веер поперёк фронта разводит их по Y.</summary>
        [Test]
        public void Offset_SpreadsBodiesAcrossTheFront()
        {
            const float Step = 0.8f;

            Vector2 first  = SummonLayout.Offset(0, Step, Summoner(0));
            Vector2 second = SummonLayout.Offset(1, Step, Summoner(0));
            Vector2 third  = SummonLayout.Offset(2, Step, Summoner(0));

            Assert.That(first.y, Is.Not.EqualTo(second.y), "первая пара расходится по фронту");
            Assert.That(Mathf.Abs(third.y), Is.GreaterThan(Mathf.Abs(first.y)), "следующий ряд встаёт дальше от оси");
        }
    }
}
