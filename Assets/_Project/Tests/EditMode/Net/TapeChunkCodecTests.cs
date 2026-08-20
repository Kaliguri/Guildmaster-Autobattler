using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Net.Tape;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Обёртка для тестов, которым размер чанка безразличен: пишет с потолком формата и падает, если не
    /// влезло.
    /// </summary>
    /// <remarks>
    /// Живёт здесь, а не в продакшн-коде: настоящий предел приходит от транспорта и передаётся
    /// параметром (см. <see cref="TapeChunkWriter.TryWrite"/>). Тесты про дельту, версию формата и
    /// усечение — не про размер, и разбор <c>false</c> в каждом из них только прятал бы их смысл.
    /// </remarks>
    internal static class TapeChunkWriterTestExtensions
    {
        public static ArraySegment<byte> Write(this TapeChunkWriter writer,
            BattleTape tape, int firstTick, int tickCount)
        {
            Assert.IsTrue(
                writer.TryWrite(tape, firstTick, tickCount, TapeChunkFormat.MaxChunkBytes, out ArraySegment<byte> bytes),
                "чанк не влез даже в потолок формата — этот тест не про размер");

            return bytes;
        }
    }

    /// <summary>
    /// Кодек чанка боевой ленты (ТЗ кооп-вертикали §5): хост укладывает срез ленты в байты, гость
    /// разбирает их в свою ленту и играет тем же плеером, которым играет соло.
    /// <para>Главное здесь — <b>что именно обещано восстановить точно, а что с потерей</b>. Числа событий
    /// (урон, лечение) едут полным <c>float</c> и обязаны совпасть до бита: они попадают в цифры на
    /// экране и в аудит. Снимки квантуются, и обещание у них другое — «в пределах шага упаковки».
    /// Смешивать эти два обещания нельзя: тест, требующий побайтового совпадения от снимков, заставил бы
    /// отказаться от квантования и утроил бы трафик.</para>
    /// </summary>
    public sealed class TapeChunkCodecTests
    {
        [Test]
        public void Events_SurviveTheRoundtrip_Exactly()
        {
            var source = new BattleTape(windowTicks: 64);
            var units  = new List<UnitSnapshot> { Unit(id: 1, hp: 100f), Unit(id: 2, hp: 80f, team: 1) };

            source.CaptureSnapshots(0, units);

            var damage = new DamageResult(hpDamage: 13.75f, shieldDamage: 4.5f, killedTarget: true,
                sourceKind: DamageSourceKind.AutoAttack, type: DamageType.Slash,
                vulnerability: 1.25f, mitigated: 6.125f);
            source.RecordDamage(tick: 0, sourceId: 1, targetId: 2, in damage, subTick: 0.5f);
            source.Record(new TapeEvent(TapeEventKind.Healed, 0, sourceId: 2, targetId: 1, amount: 7.25f));

            BattleTape restored = Roundtrip(source, firstTick: 0, tickCount: 1, out TapeChunkStatus status);

            Assert.AreEqual(TapeChunkStatus.Ok, status);
            Assert.AreEqual(2, restored.EventCount, "Оба события доехали");

            // Порядок внутри тика — по ДОЛЕ, а не по порядку записи: лечение с долей 0 идёт раньше
            // удара с долей 0.5, хотя записан вторым. Это тот же инвариант, что держит лента у хоста
            // (сортированная вставка), и он обязан пережить дорогу — иначе у гостя вспышка и цифра
            // менялись бы местами.
            Assert.AreEqual(TapeEventKind.Healed, restored.GetEvent(0).Kind,
                "Раннее по доле событие идёт первым и после роундтрипа");
            Assert.AreEqual(TapeEventKind.DamageDealt, restored.GetEvent(1).Kind);

            DamageResult got = restored.GetDamage(restored.GetEvent(1).PayloadIndex);
            Assert.AreEqual(damage.HpDamage,      got.HpDamage,      "Урон по HP — бит в бит");
            Assert.AreEqual(damage.ShieldDamage,  got.ShieldDamage,  "И по щиту");
            Assert.AreEqual(damage.Mitigated,     got.Mitigated,     "И срезанное бронёй");
            Assert.AreEqual(damage.Vulnerability, got.Vulnerability, "И уязвимость");
            Assert.IsTrue(got.KilledTarget, "И «добил»");
            Assert.AreEqual(DamageType.Slash, got.Type);
            Assert.AreEqual(DamageSourceKind.AutoAttack, got.SourceKind);

            Assert.AreEqual(7.25f, restored.GetEvent(0).Amount, "Лечение — тоже точно");
        }

        // Доля тика едет одним байтом: это подача, и точность кадра (1/255 тика ≈ 0.13 мс) заведомо
        // тоньше, чем разница, которую способен показать монитор.
        [Test]
        public void SubTick_SurvivesWithinAByte()
        {
            var source = new BattleTape(windowTicks: 16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f) });
            source.Record(new TapeEvent(TapeEventKind.UnitDied, 0, sourceId: 1, subTick: 0.37f));

            BattleTape restored = Roundtrip(source, 0, 1, out _);

            Assert.AreEqual(0.37f, restored.GetEvent(0).SubTick, 1f / 255f,
                "Доля тика доехала с точностью байта");
        }

        [Test]
        public void Snapshots_SurviveWithinTheQuantizationStep()
        {
            var source = new BattleTape(windowTicks: 16);
            UnitSnapshot unit = Unit(id: 3, hp: 137f, team: 1, position: new Vector2(-4.31f, 2.77f));
            source.CaptureSnapshots(0, new List<UnitSnapshot> { unit });

            BattleTape restored = Roundtrip(source, 0, 1, out _);

            Assert.IsTrue(restored.TryGetFrame(0, out IReadOnlyList<UnitSnapshot> frame));
            UnitSnapshot got = frame[0];

            Assert.AreEqual(unit.Id, got.Id);
            Assert.AreEqual(unit.Team, got.Team, "Команда едет один раз, с первым появлением юнита");
            Assert.AreEqual(unit.Position.x, got.Position.x, 1f / TapeQuantization.PositionScale);
            Assert.AreEqual(unit.Position.y, got.Position.y, 1f / TapeQuantization.PositionScale);
            Assert.AreEqual(unit.CurrentHP, got.CurrentHP, 1f, "HP округляется до целого — полоске хватает");
            Assert.AreEqual(unit.Phase, got.Phase, "Фаза атаки — как есть, это перечисление");
            Assert.AreEqual(unit.TargetId, got.TargetId);
            Assert.AreEqual(unit.IsDead, got.IsDead);
            Assert.AreEqual(unit.CanAct, got.CanAct, "Признаки упакованы в байт и распакованы обратно");
        }

        // PreviousPosition по сети не едет вовсе: приёмник знает её из прошлого кадра. Проверяем, что
        // знание работает — иначе интерполяция у гостя дёргалась бы каждый тик.
        [Test]
        public void PreviousPosition_IsRebuiltFromThePriorFrame_NotSentOverTheWire()
        {
            var source = new BattleTape(windowTicks: 16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f, position: new Vector2(0f, 0f)) });
            source.CaptureSnapshots(1, new List<UnitSnapshot> { Unit(1, 100f, position: new Vector2(1f, 0f)) });

            BattleTape restored = Roundtrip(source, 0, 2, out _);

            Assert.IsTrue(restored.TryGetFrame(1, out IReadOnlyList<UnitSnapshot> frame));
            Assert.AreEqual(0f, frame[0].PreviousPosition.x, 1f / TapeQuantization.PositionScale,
                "Прошлая позиция взялась из прошлого кадра чанка");
            Assert.AreEqual(1f, frame[0].Position.x, 1f / TapeQuantization.PositionScale);
        }

        // Смысл дельты: стоящий юнит почти ничего не стоит. Если это перестанет быть так, трафик
        // вырастет в разы, и тест — единственное место, где это заметят вовремя.
        [Test]
        public void UnchangedUnits_CostAlmostNothingAfterTheFirstFrame()
        {
            var tape = new BattleTape(windowTicks: 64);
            var units = new List<UnitSnapshot>();
            for (int i = 0; i < 8; i++) units.Add(Unit(i, 100f));

            tape.CaptureSnapshots(0, units);
            var writer = new TapeChunkWriter();
            int oneFrame = writer.Write(tape, 0, 1).Count;

            for (int tick = 1; tick < 30; tick++) tape.CaptureSnapshots(tick, units);   // ничего не меняется
            int thirtyFrames = new TapeChunkWriter().Write(tape, 0, 30).Count;

            int perExtraFrame = (thirtyFrames - oneFrame) / 29;
            Assert.Less(perExtraFrame, oneFrame / 4,
                $"Кадр без изменений обошёлся в {perExtraFrame} Б против {oneFrame} Б первого — " +
                "дельта работает; если сравнялось, значит маска перестала отсекать неизменное");
        }

        [Test]
        public void SameChunkTwice_IsAppliedOnce()
        {
            var source = new BattleTape(windowTicks: 16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f) });
            source.Record(new TapeEvent(TapeEventKind.UnitDied, 0, sourceId: 1));

            var writer = new TapeChunkWriter();
            byte[] chunk = Copy(writer.Write(source, 0, 1));

            var target = new BattleTape(windowTicks: 16);
            var reader = new TapeChunkReader(target, new FakeContent());

            Assert.AreEqual(TapeChunkStatus.Ok, reader.Read(new ArraySegment<byte>(chunk)));
            Assert.AreEqual(TapeChunkStatus.Duplicate, reader.Read(new ArraySegment<byte>(chunk)),
                "Повтор чанка — дубль: так выглядит и пересылка потерянного, и реконнект");
            Assert.AreEqual(1, target.EventCount,
                "Событие применено один раз — иначе на один удар пришлись бы две цифры урона");
        }

        [Test]
        public void UnknownContentId_IsRefusedLoudly_NotSkipped()
        {
            var source = new BattleTape(windowTicks: 16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f) });
            source.RecordEffect(0, TapeEventKind.EffectApplied, targetId: 1, def: Effect("effect.burn"));

            var writer = new TapeChunkWriter();
            byte[] chunk = Copy(writer.Write(source, 0, 1));

            var target = new BattleTape(windowTicks: 16);
            var reader = new TapeChunkReader(target, new FakeContent());   // реестр ПУСТ

            Assert.AreEqual(TapeChunkStatus.UnknownContentId, reader.Read(new ArraySegment<byte>(chunk)),
                "Показ без определения эффекта не знает, чем светить: пропустить молча значит дать гостю " +
                "тихо другую картинку");
            StringAssert.Contains("effect.burn", reader.LastError, "И отказ называет виновника");
        }

        [Test]
        public void ForeignFormatVersion_IsRefused()
        {
            var source = new BattleTape(windowTicks: 16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f) });

            byte[] chunk = Copy(new TapeChunkWriter().Write(source, 0, 1));
            chunk[0] = 99;   // чужая версия формата

            var reader = new TapeChunkReader(new BattleTape(16), new FakeContent());
            Assert.AreEqual(TapeChunkStatus.VersionMismatch, reader.Read(new ArraySegment<byte>(chunk)),
                "Чанк из другой версии читать как попало нельзя — он живёт и в реплее на диске");
        }

        [Test]
        public void TruncatedChunk_IsReportedAsCorrupted_NotSilentlyHalfApplied()
        {
            var source = new BattleTape(windowTicks: 16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f), Unit(2, 90f) });

            byte[] full = Copy(new TapeChunkWriter().Write(source, 0, 1));
            var cut = new ArraySegment<byte>(full, 0, full.Length - 5);

            var reader = new TapeChunkReader(new BattleTape(16), new FakeContent());
            Assert.AreEqual(TapeChunkStatus.Corrupted, reader.Read(cut));
            Assert.IsNotEmpty(reader.LastError, "Причина названа, а не проглочена");
        }

        /// <summary>
        /// Чанк сверх предела — это <c>false</c>, а НЕ исключение, и номер чанка при этом не тратится.
        /// </summary>
        /// <remarks>
        /// Ниже нашего кода размер не проверяет никто: Steam вернёт InvalidParam, а транспорт его не
        /// читает — чанк уехал бы в тишину. Но «не влезло» здесь ожидаемый исход, на который у стримера
        /// есть ответ (поделить диапазон), а не ошибка: так требует и конвенция .NET для Try-методов.
        /// Пока это было исключением, оно улетало наверх с уже потраченным номером — раздача вставала
        /// навсегда, а у гостя оставалась дыра в нумерации, которую он просил повторить до конца боя.
        /// </remarks>
        [Test]
        public void ChunkOverTheLimit_ReturnsFalse_AndKeepsTheChunkNumber()
        {
            var tape = new BattleTape(windowTicks: 64);
            var units = new List<UnitSnapshot>(255);
            for (int i = 0; i < 255; i++) units.Add(Unit(i, 100f + i, position: new Vector2(i * 0.37f, i * 0.11f)));

            for (int tick = 0; tick < 40; tick++)
            {
                // Двигаем всех каждый тик, чтобы дельта не спасла: это и есть худший случай.
                var moved = new List<UnitSnapshot>(units.Count);
                for (int i = 0; i < units.Count; i++)
                    moved.Add(Unit(i, 100f + i + tick, position: new Vector2(i * 0.37f + tick, i * 0.11f + tick)));
                tape.CaptureSnapshots(tick, moved);
            }

            var writer = new TapeChunkWriter();
            int numberBefore = writer.NextChunkNumber;

            Assert.IsFalse(writer.TryWrite(tape, 0, 40, TapeChunkFormat.MaxChunkBytes, out ArraySegment<byte> bytes),
                "Чанк сверх предела — отказ на нашей стороне, а не отправка в тишину");
            Assert.AreEqual(0, bytes.Count, "Не влезло — отдавать нечего");
            Assert.AreEqual(numberBefore, writer.NextChunkNumber,
                "Номер не потрачен: этот же чанк уедет меньшими кусками, и дыры в нумерации у гостя не будет");
        }

        // ── Утилиты ──

        private static BattleTape Roundtrip(BattleTape source, int firstTick, int tickCount,
            out TapeChunkStatus status)
        {
            var writer = new TapeChunkWriter();
            ArraySegment<byte> chunk = writer.Write(source, firstTick, tickCount);

            var target = new BattleTape(windowTicks: 64);
            var content = new FakeContent();

            // Все определения, что могли уехать в чанк, обязаны быть у приёмника — это ровно то, что
            // гарантирует handshake отпечатка контента перед сессией.
            for (int i = 0; i < source.EventCount; i++)
            {
                TapeEvent ev = source.GetEvent(i);
                if (ev.Kind is TapeEventKind.EffectApplied or TapeEventKind.EffectEnded)
                {
                    EffectData def = source.GetEffect(ev.PayloadIndex);
                    if (def != null) content.Add(def);
                }
            }

            var reader = new TapeChunkReader(target, content);
            status = reader.Read(chunk);
            return target;
        }

        private static byte[] Copy(ArraySegment<byte> segment)
        {
            var bytes = new byte[segment.Count];
            Array.Copy(segment.Array, segment.Offset, bytes, 0, segment.Count);
            return bytes;
        }

        private static UnitSnapshot Unit(int id, float hp, int team = 0, Vector2 position = default) =>
            new UnitSnapshot(
                id, team, position, position,
                currentHp: hp, maxHp: 150f, currentShield: 0f, currentResource: 25f, maxResource: 50f,
                size: 1f, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                attackCooldownTicks: 12, targetId: -1, effectTagMask: EffectTag.None, isDead: false,
                attackRange: 1.5f, canAct: true);

        private static EffectData Effect(string id)
        {
            var def = ScriptableObject.CreateInstance<EffectData>();
            def.name = id;
            typeof(ContentDefinition)
                .GetField("_id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(def, id);
            return def;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly Dictionary<string, ContentDefinition> _byId = new Dictionary<string, ContentDefinition>();

            public void Add(ContentDefinition def) => _byId[def.Id] = def;

            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                def = null;
                if (id != null && _byId.TryGetValue(id, out ContentDefinition found) && found is T typed)
                {
                    def = typed;
                    return true;
                }
                return false;
            }

            public IReadOnlyList<T> All<T>() where T : ContentDefinition
            {
                var list = new List<T>();
                foreach (ContentDefinition def in _byId.Values)
                    if (def is T typed) list.Add(typed);
                return list;
            }
        }
    }
}
