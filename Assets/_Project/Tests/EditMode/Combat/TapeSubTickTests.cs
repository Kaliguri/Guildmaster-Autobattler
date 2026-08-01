using System.Collections.Generic;
using Guildmaster.Combat.Tape;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Sub-tick подача событий ленты: событие несёт долю тика, в которую случилось на самом деле, и
    /// показ отдаёт его в ТОТ кадр, а не на границе тика (разброс был до 33 мс — вспышка и звук мимо
    /// кадра контакта при идеально посчитанном уроне).
    /// <para>Держит два инварианта шва, которые легко нарушить снаружи: лента отсортирована по паре
    /// (тик, доля), а прежние вызовы <c>PumpTo</c> без доли означают «тик показан целиком» и работают
    /// как до появления доли.</para>
    /// </summary>
    public sealed class TapeSubTickTests
    {
        // Главное обещание: внутри показываемого тика событие ждёт своей доли.
        [Test]
        public void Dispatcher_HoldsEventWithinTheTick_UntilItsShareIsShown()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            var shown = new List<int>();
            dispatcher.UnitDied += id => shown.Add(id);

            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 5, sourceId: 1, subTick: 0.7f));

            dispatcher.PumpTo(5, alpha: 0.3f);
            Assert.AreEqual(0, shown.Count,
                "Тик показан на треть, а контакт случился на 0.7 — событию ещё рано");

            dispatcher.PumpTo(5, alpha: 0.69f);
            Assert.AreEqual(0, shown.Count, "Вплотную к доле — всё ещё рано");

            dispatcher.PumpTo(5, alpha: 0.8f);
            Assert.AreEqual(1, shown.Count, "Показ прошёл 0.7 своего тика — вот теперь событие видно");
        }

        // Долгий кадр не имеет права терять события: прошлые тики отдаются целиком.
        [Test]
        public void Dispatcher_PastTicks_AreDeliveredWhole_RegardlessOfAlpha()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            int calls = 0;
            dispatcher.UnitDied += _ => calls++;

            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 5, sourceId: 1, subTick: 0.9f));

            dispatcher.PumpTo(6, alpha: 0f);
            Assert.AreEqual(1, calls,
                "Тик 5 уже позади: его доля отыграна, и ждать её на новом тике значило бы потерять событие");
        }

        // Иначе sub-tick точность съела бы сама себя: линейный курсор задержал бы раннее событие
        // до доли позднего, попавшего в список первым.
        [Test]
        public void Tape_SortsEventsWithinATick_ByTheirShare()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            var order = new List<int>();
            dispatcher.UnitDied += id => order.Add(id);

            // Порядок ЗАПИСИ — обратный порядку моментов (так и бывает: это порядок обхода юнитов).
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 4, sourceId: 90, subTick: 0.9f));
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 4, sourceId: 10, subTick: 0.1f));

            dispatcher.PumpTo(4, alpha: 0.5f);
            Assert.AreEqual(1, order.Count, "Дошли до половины тика — отдано только раннее событие");
            Assert.AreEqual(10, order[0], "И это то, чей момент раньше, а не то, что записали первым");

            dispatcher.PumpTo(4, alpha: 1f);
            Assert.AreEqual(2, order.Count, "Тик доигран — отдано и позднее");
            Assert.AreEqual(90, order[1]);
        }

        // Стабильность вставки: у событий без доли (смерть, эффект, периодика) порядок прежний —
        // порядок записи. Иначе правка ради точности незаметно переставила бы всё остальное.
        [Test]
        public void Tape_KeepsWriteOrder_ForEventsWithoutAShare()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            var order = new List<int>();
            dispatcher.UnitDied += id => order.Add(id);

            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 7, sourceId: 1));
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 7, sourceId: 2));
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, tick: 7, sourceId: 3));

            dispatcher.PumpTo(7);

            Assert.AreEqual(new[] { 1, 2, 3 }, order,
                "Равные доли не переставляются: вставка стабильна");
        }

        // Совместимость шва: кто про долю не знает (тесты, диагностика, догоняющая прокрутка) —
        // получает тик целиком, как до её появления.
        [Test]
        public void Dispatcher_DefaultAlpha_MeansTheWholeTick()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            int calls = 0;
            dispatcher.AttackEvaded += _ => calls++;

            tape.Record(new TapeEvent(TapeEventKind.AttackEvaded, tick: 2, targetId: 4, subTick: 0.99f));
            dispatcher.PumpTo(2);

            Assert.AreEqual(1, calls, "Без доли PumpTo отдаёт весь тик — прежнее поведение цело");
        }

        // Доля — это доля ОДНОГО тика: 1 и больше означали бы «в следующем тике», а отрицательное —
        // «в предыдущем». И то и другое сдвинуло бы событие мимо своего тика.
        [Test]
        public void SubTick_IsClampedInsideItsOwnTick()
        {
            var below = new TapeEvent(TapeEventKind.UnitDied, tick: 1, subTick: -0.5f);
            var above = new TapeEvent(TapeEventKind.UnitDied, tick: 1, subTick: 1.5f);

            Assert.AreEqual(0f, below.SubTick, 1e-6f, "Отрицательная доля — это ноль, а не прошлый тик");
            Assert.Less(above.SubTick, 1f, "Доля не достигает единицы — иначе это уже следующий тик");
        }
    }
}
