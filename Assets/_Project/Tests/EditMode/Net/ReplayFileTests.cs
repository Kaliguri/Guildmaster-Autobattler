using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;
using Guildmaster.Net.Tape;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Файл повтора боя: заголовок плюс поток записей (состав и чанки ленты). Проверяем, что запись на
    /// диск и чтение обратно дают ту же ленту, из которой рисует показ — реплей это третий наполнитель
    /// той же <see cref="BattleTape"/> после живого сима и гостя по сети.
    /// <para>Главное обещание — то же, что у кооп-кодека: числа СОБЫТИЙ (урон) бит в бит, снимки в
    /// пределах шага упаковки. Плюс своё: заголовок переживает дорогу, а чужой/битый файл отвергается
    /// вердиктом, а не мусором в ленте.</para>
    /// </summary>
    public sealed class ReplayFileTests
    {
        [Test]
        public void Header_RoundTrips()
        {
            var writer = new ReplayFileWriter(new BattleTape(16),
                new ReplayFile.Header(ReplayFile.FormatVersion, "1.4.2", 0xABCDEF12u, "relic.antimage vs relic.treant"));

            var reader = new NetByteReader(Copy(writer.Written));
            ReplayLoadResult result = ReplayFile.TryReadHeader(reader, out ReplayFile.Header header);

            Assert.AreEqual(ReplayLoadResult.Ok, result);
            Assert.AreEqual(ReplayFile.FormatVersion, header.FormatVersion);
            Assert.AreEqual("1.4.2", header.GameVersion);
            Assert.AreEqual(0xABCDEF12u, header.Seed);
            Assert.AreEqual("relic.antimage vs relic.treant", header.Title);
        }

        [Test]
        public void NotOurFile_IsCorrupted()
        {
            var reader = new NetByteReader(new ArraySegment<byte>(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            Assert.AreEqual(ReplayLoadResult.Corrupted, ReplayFile.TryReadHeader(reader, out _),
                "Чужая сигнатура — не наш файл, читать дальше нельзя");
        }

        [Test]
        public void NewerFormat_IsRejected_NotReadAsGarbage()
        {
            var writer = new ReplayFileWriter(new BattleTape(16),
                new ReplayFile.Header(ReplayFile.FormatVersion, "1.0", 0UL, "t"));
            byte[] bytes = Copy(writer.Written).Array;
            bytes[ReplayFile.Magic.Length] = 99;   // версия формата из будущего

            var reader = new NetByteReader(new ArraySegment<byte>(bytes));
            Assert.AreEqual(ReplayLoadResult.TooNew, ReplayFile.TryReadHeader(reader, out _),
                "Формат новее нашего — как сейв TooNew: не грузим и не читаем как попало");
        }

        [Test]
        public void Truncated_IsCorrupted()
        {
            var writer = new ReplayFileWriter(new BattleTape(16),
                new ReplayFile.Header(ReplayFile.FormatVersion, "1.0", 7UL, "title"));
            ArraySegment<byte> full = writer.Written;

            var cut = new ArraySegment<byte>(Copy(full).Array, 0, full.Count - 3);   // обрезали заголовок
            var reader = new NetByteReader(cut);
            Assert.AreEqual(ReplayLoadResult.Corrupted, ReplayFile.TryReadHeader(reader, out _));
        }

        // Ядро: бой с диска даёт ту же ленту. Ростер тут не нужен — чанк несёт id юнитов числами, а
        // определения (арт) резолвит показ; здесь проверяются кадры и события.
        [Test]
        public void Battle_RoundTrips_ThroughTheFile()
        {
            var source = new BattleTape(windowTicks: 64);
            for (int tick = 0; tick < 4; tick++)
            {
                source.CaptureSnapshots(tick, new List<UnitSnapshot>
                {
                    Unit(1, hp: 100f - tick, position: new Vector2(tick * 0.5f, 0f)),
                    Unit(2, hp: 80f, team: 1, position: new Vector2(3f - tick * 0.5f, 1f)),
                });
            }

            var damage = new DamageResult(hpDamage: 13.75f, shieldDamage: 4.5f, killedTarget: false,
                sourceKind: DamageSourceKind.AutoAttack, type: DamageType.Slash,
                vulnerability: 1.25f, mitigated: 6.125f);
            source.RecordDamage(tick: 1, sourceId: 1, targetId: 2, in damage, subTick: 0.5f);

            // Короткий бой: полный чанк (30 тиков) не набирается, весь хвост уходит Flush'ем.
            var writer = new ReplayFileWriter(source,
                new ReplayFile.Header(ReplayFile.FormatVersion, "1.0", 42UL, "roundtrip"));
            writer.Flush(3);

            byte[] bytes = Copy(writer.Written).Array;

            var target = new BattleTape(windowTicks: 64);
            ReplayFilePlayer player = OpenPlayer(bytes, target, out ReplayLoadResult result);

            Assert.AreEqual(ReplayLoadResult.Ok, result);
            Assert.AreEqual("roundtrip", player.Header.Title);

            player.FeedUpTo(4);   // кормим до фронта 4 — весь бой (0..3) уложится, дальше файл кончится

            Assert.AreEqual(3, target.FrontTick, "Весь бой доехал в ленту");

            Assert.IsTrue(target.TryGetFrame(2, out IReadOnlyList<UnitSnapshot> frame));
            Assert.AreEqual(1f, frame[0].Position.x, 1f / TapeQuantization.PositionScale, "Позиция кадра совпала");
            Assert.AreEqual(98f, frame[0].CurrentHP, 1f, "И HP");

            Assert.AreEqual(1, target.EventCount, "Событие урона доехало");
            DamageResult got = target.GetDamage(target.GetEvent(0).PayloadIndex);
            Assert.AreEqual(damage.HpDamage, got.HpDamage, "Урон по HP — бит в бит, как в кооп-кодеке");
            Assert.AreEqual(damage.Mitigated, got.Mitigated, "И срезанное бронёй");
        }

        // Пустой/несуществующий файл — не падение, а вердикт Missing: фон меню просто не заведётся.
        [Test]
        public void EmptyBytes_AreMissing_NotACrash()
        {
            var target = new BattleTape(16);
            ReplayFilePlayer player = OpenPlayer(Array.Empty<byte>(), target, out ReplayLoadResult result);

            Assert.AreEqual(ReplayLoadResult.Missing, result);
            Assert.DoesNotThrow(() => player.FeedUpTo(100), "Пустой источник просто ничего не подаёт");
            Assert.AreEqual(BattleTape.NoTick, target.FrontTick);
        }

        // ── needs editor: ростер резолвится через UnitData, а тот — ScriptableObject ──

        [Test]
        public void Roster_RegistersUnits_ForPlayback()
        {
            var content = new FakeContent();
            UnitData antimage = MakeUnit("relic.antimage");
            content.Add(antimage);

            var source = new BattleTape(16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f), Unit(2, 90f, team: 1) });

            var writer = new ReplayFileWriter(source,
                new ReplayFile.Header(ReplayFile.FormatVersion, "1.0", 0UL, "roster"));
            writer.AddUnit(1, 0, "relic.antimage");
            writer.AddUnit(2, 1, "");                     // болванчик без определения — законно
            writer.Flush(0);

            byte[] bytes = Copy(writer.Written).Array;

            var target = new BattleTape(16);
            var registry = new BattleUnitRegistry(null);   // реплею сим не нужен
            var reader = new TapeChunkReader(target, content);
            var player = new ReplayFilePlayer(bytes, target, new BattleTapePlayback(target), reader, registry, content);

            player.FeedUpTo(1);

            Assert.IsTrue(registry.TryGet(1, out UnitIdentity a), "Паспорт из ростера зарегистрирован");
            Assert.AreSame(antimage, a.Definition, "И с правильным определением");
            Assert.IsTrue(registry.TryGet(2, out UnitIdentity b), "Болванчик тоже — с пустым определением");
            Assert.IsNull(b.Definition);
        }

        [Test]
        public void UnknownContentId_SkipsTheUnit_ButKeepsPlaying()
        {
            var source = new BattleTape(16);
            source.CaptureSnapshots(0, new List<UnitSnapshot> { Unit(1, 100f) });

            var writer = new ReplayFileWriter(source,
                new ReplayFile.Header(ReplayFile.FormatVersion, "1.0", 0UL, "gone"));
            writer.AddUnit(1, 0, "relic.deleted_next_version");   // контента такого больше нет
            writer.Flush(0);

            byte[] bytes = Copy(writer.Written).Array;

            var target = new BattleTape(16);
            var registry = new BattleUnitRegistry(null);
            var reader = new TapeChunkReader(target, new FakeContent());   // пустой реестр
            var player = new ReplayFilePlayer(bytes, target, new BattleTapePlayback(target), reader, registry, new FakeContent());

            // Удаление контента — единственное, чего запись не переживает: бойца пропускаем, но кадры
            // доигрываем. Балансные правки, в отличие от этого, записи не касаются вовсе.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("relic.deleted_next_version"));
            player.FeedUpTo(1);

            Assert.IsFalse(registry.TryGet(1, out _), "Юнита без контента в реестре нет");
            Assert.AreEqual(0, target.FrontTick, "Но кадр всё равно доехал — бой играется дальше");
        }

        // ── Утилиты ──

        private static ReplayFilePlayer OpenPlayer(byte[] bytes, BattleTape target, out ReplayLoadResult result)
        {
            var registry = new BattleUnitRegistry(null);
            var reader   = new TapeChunkReader(target, new FakeContent());
            var player   = new ReplayFilePlayer(bytes, target, new BattleTapePlayback(target), reader, registry, new FakeContent());
            result = player.LoadResult;
            return player;
        }

        private static ArraySegment<byte> Copy(ArraySegment<byte> segment)
        {
            var bytes = new byte[segment.Count];
            Array.Copy(segment.Array, segment.Offset, bytes, 0, segment.Count);
            return new ArraySegment<byte>(bytes);
        }

        private static UnitSnapshot Unit(int id, float hp, int team = 0, Vector2 position = default) =>
            new UnitSnapshot(
                id, team, position, position,
                currentHp: hp, maxHp: 150f, currentShield: 0f, currentResource: 25f, maxResource: 50f,
                size: 1f, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                attackCooldownTicks: 12, targetId: -1, effectTagMask: EffectTag.None, isDead: false,
                attackRange: 1.5f, canAct: true);

        private static UnitData MakeUnit(string id)
        {
            // UnitData абстрактен — берём конкретного наследника; реплей за меню и есть дуэли мементо.
            var def = ScriptableObject.CreateInstance<RelicData>();
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
