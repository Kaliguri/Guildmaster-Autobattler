using System;
using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Net;
using Guildmaster.Net.Presence;
using Guildmaster.Net.Tape;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Присутствие (ТЗ кооп-вертикали §6): чужие курсоры — 128 Гц с dirty-check, склеенный пакет,
    /// ненадёжный канал, интерполяция на приёме.
    /// <para>Проверяется то, что легко сломать, не заметив: курсор в покое не стоит ни пакета, чужой
    /// курсор не дёргается на изломах, потерянный пакет не роняет картинку, а опоздавший не откатывает
    /// её назад.</para>
    /// </summary>
    public sealed class PresenceTests
    {
        /// <summary>
        /// Жест доезжает до другого конца — целиком, через упаковку.
        /// </summary>
        /// <remarks>
        /// Инвариант живёт между отправителем и кодеком, и его отсутствие НЕ ВИДНО ниоткуда: жест
        /// просто не появляется у напарника, а всё остальное работает. Так и вышло 07.08.2026 — поле
        /// добавили в состояние, версию пакета подняли, а записать и прочитать его забыли; компилятор
        /// и прежние тесты промолчали.
        /// </remarks>
        [Test]
        public void Gesture_SurvivesThePacket()
        {
            var sender = new PresenceSender();
            sender.Show(PlayerGesture.Like, now: 0f);

            Assert.IsTrue(sender.TrySample(new Vector2(1f, 1f), playerId: 2, now: 0f, out PresenceState sent));
            Assert.AreEqual(PlayerGesture.Like, sent.Gesture, "жест обязан попасть в отправляемое состояние");

            var bytes = new NetByteWriter(64);
            PresenceCodec.Write(bytes, new[] { sent });

            var got = new List<PresenceState>();
            Assert.IsTrue(PresenceCodec.TryRead(bytes.WrittenSegment, got));
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(PlayerGesture.Like, got[0].Gesture, "и пережить упаковку — иначе его никто не увидит");
        }

        /// <summary>
        /// Жест гаснет сам по времени: пары «показать / убрать» нет, и второй владелец не заводится.
        /// </summary>
        [Test]
        public void Gesture_FadesOnItsOwn()
        {
            var sender = new PresenceSender();
            sender.Show(PlayerGesture.Like, now: 0f);
            sender.TrySample(new Vector2(1f, 1f), playerId: 2, now: 0f, out _);

            float after = PresenceSender.GestureHoldSeconds + 0.1f;
            Assert.IsTrue(sender.TrySample(new Vector2(5f, 5f), playerId: 2, now: after, out PresenceState later));
            Assert.AreEqual(PlayerGesture.None, later.Gesture,
                "жест держится ограниченное время: иначе игрок остался бы с вечным лайком у курсора");
        }

        // Главная причина, по которой 128 Гц вообще можно себе позволить.
        [Test]
        public void StillCursor_CostsNothing()
        {
            var sender = new PresenceSender();
            var cursor = new Vector2(3f, 2f);

            Assert.IsTrue(sender.TrySample(cursor, playerId: 1, now: 0f, out _),
                "Первый замер отправляется: про нас ещё ничего не известно");

            for (int i = 1; i <= 200; i++)
                sender.TrySample(cursor, 1, now: i * 0.01f, out _);

            Assert.AreEqual(1, sender.SentCount,
                "Курсор стоит две секунды — пакетов ноль. Иначе высокая частота стоила бы трафика на пустом месте");
        }

        [Test]
        public void MovingCursor_IsCappedAtTheSendRate()
        {
            var sender = new PresenceSender();

            // Двигаем каждый миллисекундный шаг — вдесятеро чаще потолка.
            for (int i = 0; i < 1000; i++)
                sender.TrySample(new Vector2(i * 0.01f, 0f), 1, now: i * 0.001f, out _);

            Assert.LessOrEqual(sender.SentCount, (int)(PresenceSender.MaxRateHz * 1f) + 2,
                "За секунду отправлено не больше потолка 128 Гц");
            Assert.Greater(sender.SentCount, 100, "Но и не подозрительно мало — движение шлём");
        }

        [Test]
        public void GrabbingSomething_IsSentEvenWithoutMoving()
        {
            var sender = new PresenceSender();
            var cursor = new Vector2(1f, 1f);

            sender.TrySample(cursor, 1, 0f, out _);
            int before = sender.SentCount;

            Assert.IsTrue(sender.TrySample(cursor, 1, 0.5f, out PresenceState state, heldId: 42),
                "Курсор не двигался, но взял предмет — это изменение присутствия");
            Assert.AreEqual(42, state.HeldId);
            Assert.AreEqual(before + 1, sender.SentCount);
        }

        // Скорость считается по промежутку между ЗАМЕРАМИ: завышенная увела бы чужой курсор в сторону
        // на первой же потере пакета.
        [Test]
        public void Velocity_IsMeasuredBetweenSamples()
        {
            var sender = new PresenceSender();

            sender.TrySample(new Vector2(0f, 0f), 1, 0f, out _);
            sender.TrySample(new Vector2(1f, 0f), 1, 0.1f, out PresenceState state);

            Assert.AreEqual(10f, state.Velocity.x, 0.01f, "Метр за 0.1 сек = 10 единиц в секунду");
        }

        // ═══ Дорога ═══

        [Test]
        public void Packet_CarriesEveryCursor_AndSurvivesTheRoundtrip()
        {
            var states = new List<PresenceState>
            {
                new PresenceState(0, 7,  new Vector2(-2.5f, 1.25f), new Vector2(1f, 0f), hoveredId: 11),
                new PresenceState(1, 9,  new Vector2(4.75f, -3f),   new Vector2(0f, -2f), heldId: 42),
            };

            var writer = new NetByteWriter(64);
            PresenceCodec.Write(writer, states);

            var got = new List<PresenceState>();
            Assert.IsTrue(PresenceCodec.TryRead(writer.WrittenSegment, got));

            Assert.AreEqual(2, got.Count, "Все курсоры — одним склеенным пакетом");
            Assert.AreEqual(0, got[0].PlayerId);
            Assert.AreEqual(11, got[0].HoveredId, "Чужое наведение видно — это мягкая заявка на объект");
            Assert.AreEqual(42, got[1].HeldId, "И чужое «в руках» тоже");
            Assert.AreEqual(-2.5f, got[0].Cursor.x, 1f / TapeQuantization.PositionScale);
            Assert.AreEqual(-2f,   got[1].Velocity.y, 1f / TapeQuantization.PositionScale);
        }

        [Test]
        public void Packet_StaysSmallEnoughForAnUnreliableChannel()
        {
            var states = new List<PresenceState>();
            for (int i = 0; i < PresenceCodec.MaxPlayersPerPacket; i++)
                states.Add(new PresenceState(i, 1, Vector2.one, Vector2.zero));

            var writer = new NetByteWriter(256);
            PresenceCodec.Write(writer, states);

            Assert.Less(writer.Length, 1200,
                "Пакет обязан влезать в MTU: фрагментированный ненадёжный теряется целиком, и мы " +
                "потеряли бы сразу всех");
        }

        [Test]
        public void ForeignVersion_IsIgnored_NotCrashed()
        {
            var writer = new NetByteWriter(32);
            PresenceCodec.Write(writer, new List<PresenceState> { new PresenceState(0, 1, Vector2.zero, Vector2.zero) });

            byte[] packet = new byte[writer.Length];
            Array.Copy(writer.WrittenSegment.Array, packet, writer.Length);
            packet[0] = 99;

            var got = new List<PresenceState>();
            Assert.IsFalse(PresenceCodec.TryRead(new ArraySegment<byte>(packet), got),
                "Чужой формат — просто не обновляем присутствие: терять курсор безопасно, падать из-за него нет");
            Assert.IsEmpty(got);
        }

        // ═══ Приём ═══

        [Test]
        public void Interpolation_MovesSmoothlyBetweenPackets()
        {
            var interp = new PresenceInterpolator();
            var right  = new Vector2(10f, 0f);

            interp.Push(new PresenceState(1, 1, new Vector2(0f, 0f), right), receivedAt: 0f);
            interp.Push(new PresenceState(1, 2, new Vector2(1f, 0f), right), receivedAt: 0.1f);

            // Момент показа отстаёт на буфер, поэтому смотрим на середину отрезка с его учётом.
            float middle = 0.05f + PresenceInterpolator.BufferSeconds;
            Assert.IsTrue(interp.TrySample(1, middle, out _, out Vector2 position));

            Assert.Greater(position.x, 0.2f, "Курсор уже двинулся от прошлой точки");
            Assert.Less(position.x, 0.8f, "Но ещё не дошёл до новой — это и есть интерполяция");
        }

        // Эрмит нужен ровно за этим: на изломе линейная даёт угол, а курсор в коопе из изломов и состоит.
        [Test]
        public void Interpolation_RespectsVelocityDirection_NotJustEndpoints()
        {
            var interp = new PresenceInterpolator();

            // Обе точки на одной горизонтали, но скорости смотрят ВВЕРХ: кривая обязана выгнуться,
            // линейная прошла бы строго по прямой.
            interp.Push(new PresenceState(1, 1, new Vector2(0f, 0f), new Vector2(1f, 4f)), 0f);
            interp.Push(new PresenceState(1, 2, new Vector2(1f, 0f), new Vector2(1f, 4f)), 0.1f);

            // Смотрим на ЧЕТВЕРТИ, а не на середине: при одинаковых касательных на обоих концах
            // середина — точка перегиба, кривая там ровно пересекает прямую. Проверка в этой точке
            // прошла бы и для линейной интерполяции, то есть не проверяла бы ничего.
            interp.TrySample(1, 0.025f + PresenceInterpolator.BufferSeconds, out _, out Vector2 early);
            interp.TrySample(1, 0.075f + PresenceInterpolator.BufferSeconds, out _, out Vector2 late);

            Assert.Greater(early.y, 0.01f,
                "На выходе из точки кривая пошла ВВЕРХ — туда, куда смотрит скорость");
            Assert.Less(late.y, -0.01f,
                "А к следующей точке подходит снизу, чтобы прийти в неё с тем же направлением: " +
                "именно это и убирает угол на изломе, ради которого взят Эрмит");
        }

        [Test]
        public void LostPackets_AreCoveredByInertia_ThenTheCursorStops()
        {
            var interp = new PresenceInterpolator();
            interp.Push(new PresenceState(1, 1, Vector2.zero, new Vector2(10f, 0f)), receivedAt: 0f);

            interp.TrySample(1, 0.05f + PresenceInterpolator.BufferSeconds, out _, out Vector2 shortGap);
            Assert.Greater(shortGap.x, 0.3f, "Пакет потерян — идём по последней скорости, никто не заметил");

            interp.TrySample(1, 5f, out _, out Vector2 longGap);
            Assert.AreEqual(10f * PresenceInterpolator.MaxExtrapolationSeconds, longGap.x, 0.01f,
                "Через долгую тишину курсор ЗАМИРАЕТ, а не улетает: улетевший читается как баг, " +
                "замерший — как «человек отошёл»");
        }

        [Test]
        public void LatePacket_DoesNotRewindTheCursor()
        {
            var interp = new PresenceInterpolator();

            Assert.IsTrue(interp.Push(new PresenceState(1, 5, new Vector2(5f, 0f), Vector2.zero), 0f));
            Assert.IsFalse(interp.Push(new PresenceState(1, 4, new Vector2(4f, 0f), Vector2.zero), 0.01f),
                "Опоздавший пакет отброшен: ненадёжный канал не хранит порядок, а откат читается как рывок");

            interp.TrySample(1, 1f, out PresenceState state, out _);
            Assert.AreEqual(5, state.Sequence, "В силе остался свежий");
        }

        // ushort идёт по кругу: на 65536-м пакете наивное сравнение «меньше» замерло бы навсегда.
        [Test]
        public void SequenceWraparound_DoesNotFreezeTheCursor()
        {
            var interp = new PresenceInterpolator();

            interp.Push(new PresenceState(1, ushort.MaxValue - 1, Vector2.zero, Vector2.zero), 0f);
            Assert.IsTrue(interp.Push(new PresenceState(1, 1, Vector2.one, Vector2.zero), 0.01f),
                "Номер перевалил через край и это всё ещё новый пакет");
        }

        [Test]
        public void PlayerWhoLeft_TakesTheirCursorWithThem()
        {
            var interp = new PresenceInterpolator();
            interp.Push(new PresenceState(1, 1, Vector2.zero, Vector2.zero), 0f);
            Assert.AreEqual(1, interp.Count);

            interp.Remove(1);

            Assert.AreEqual(0, interp.Count);
            Assert.IsFalse(interp.TrySample(1, 1f, out _, out _), "Курсор ушедшего не рисуется");
        }
    }
}
