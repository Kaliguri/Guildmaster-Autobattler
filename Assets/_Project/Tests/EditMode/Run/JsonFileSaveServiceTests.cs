using System;
using System.Collections.Generic;
using System.IO;
using Guildmaster.Core.Persistence;
using Guildmaster.Game.Services;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Настоящий дисковый бэкенд сейва. Остальные тесты забега подставляют <c>InMemorySaveService</c>,
    /// который держит объект в памяти по ссылке — поэтому ни сериализацию, ни повреждение файла, ни
    /// прерванную запись, ни версию схемы не видел никто (аудит 2026-07-26: TS-10, TS-21).
    /// </summary>
    public sealed class JsonFileSaveServiceTests
    {
        /// <summary>Без атрибута — версия схемы 1 (начало отсчёта).</summary>
        [Serializable]
        public sealed class Dto
        {
            public string  Name;
            public int     Gold;
            public Vector2 Position;
        }

        /// <summary>Тип, чья схема ушла вперёд — нужен, чтобы проверить ветку «файл старее».</summary>
        [Serializable]
        [SaveSchema(3)]
        public sealed class DtoV3
        {
            public string Name;
        }

        private const string Key       = "__test_run_save";
        private const string NestedKey = "profiles/__test_profile/guilds/__test_guild/run";

        private static string Root      => System.IO.Path.Combine(GameDataPath.Root, JsonFileSaveService.SavesFolder);
        private static string Path0     => System.IO.Path.Combine(Root, Key + ".json");
        private static string Backup    => Path0 + ".bak";
        private static string Temp      => Path0 + ".tmp";
        private static string Corrupt   => Path0 + ".corrupt";
        private static string NestedDir => System.IO.Path.Combine(Root, "profiles", "__test_profile");

        private JsonFileSaveService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new JsonFileSaveService();
            CleanUp();
        }

        [TearDown]
        public void TearDown() => CleanUp();

        private static void CleanUp()
        {
            foreach (string p in new[] { Path0, Backup, Temp, Corrupt })
                if (File.Exists(p)) File.Delete(p);

            if (Directory.Exists(NestedDir)) Directory.Delete(NestedDir, recursive: true);
        }

        private static void WriteRawEnvelope(string schemaVersion, string payload = "{\"Name\":\"x\"}")
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path0,
                $"{{\"schemaVersion\":{schemaVersion},\"gameVersion\":\"9.9.9\",\"payload\":{payload}}}");
        }

        // ── Круговорот и целостность файла ───────────────────────────────────

        [Test]
        public void SaveThenLoad_RoundTripsThroughRealSerialization()
        {
            _service.Save(Key, new Dto { Name = "Бонрайт", Gold = 42 });

            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);

            Assert.AreEqual(SaveLoadStatus.Ok, loaded.Status);
            Assert.AreEqual("Бонрайт", loaded.Value.Name);
            Assert.AreEqual(42, loaded.Value.Gold);
        }

        [Test]
        public void SaveThenLoad_KeepsVector2_WithoutDrowningInNormalizedRecursion()
        {
            // Регрессия переезда на Newtonsoft: у Vector2 есть свойство normalized, у него — своё
            // normalized. Без конвертера запись позиции сосуда уходит в бесконечное дерево.
            _service.Save(Key, new Dto { Name = "pos", Position = new Vector2(-6f, 1.5f) });

            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);

            Assert.AreEqual(SaveLoadStatus.Ok, loaded.Status);
            Assert.AreEqual(new Vector2(-6f, 1.5f), loaded.Value.Position);
            StringAssert.DoesNotContain("normalized", File.ReadAllText(Path0));
        }

        [Test]
        public void Save_WritesUnderSavesFolder_WhichIsTheSteamCloudContract()
        {
            _service.Save(Key, new Dto { Name = "a", Gold = 1 });

            Assert.IsTrue(File.Exists(Path0), "сейв должен лежать под Saves/ — по этой маске синкает Steam");
        }

        [Test]
        public void Save_LeavesNoTempFileBehind()
        {
            _service.Save(Key, new Dto { Name = "a", Gold = 1 });

            Assert.IsFalse(File.Exists(Temp), "временный файл записи остался на диске");
            Assert.IsTrue(File.Exists(Path0));
        }

        [Test]
        public void SecondSave_KeepsThePreviousVersionAsBackup()
        {
            _service.Save(Key, new Dto { Name = "first", Gold = 1 });
            _service.Save(Key, new Dto { Name = "second", Gold = 2 });

            Assert.IsTrue(File.Exists(Backup), "прежняя версия не отложена в .bak");
            Assert.AreEqual("second", _service.TryLoad<Dto>(Key).Value.Name);
            StringAssert.Contains("first", File.ReadAllText(Backup));
        }

        [Test]
        public void ServiceFiles_StayOutOfTheCloudMask()
        {
            _service.Save(Key, new Dto { Name = "a", Gold = 1 });
            _service.Save(Key, new Dto { Name = "b", Gold = 2 });

            // Маска Auto-Cloud — *.json. Суффикс идёт ПОСЛЕ расширения именно поэтому.
            StringAssert.EndsWith(".json.bak", Backup);
            Assert.IsFalse(Backup.EndsWith(".json", StringComparison.Ordinal),
                "бэкап подпал под облачную маску — в Steam Cloud поедет мусор");
        }

        // ── Версия схемы: три исхода ─────────────────────────────────────────

        [Test]
        public void SaveFromNewerGame_IsRefused_AndLeftUntouchedOnDisk()
        {
            WriteRawEnvelope("2"); // Dto — схема 1
            string before = File.ReadAllText(Path0);

            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);

            Assert.AreEqual(SaveLoadStatus.TooNew, loaded.Status,
                "сейв из более новой версии обязан быть отличим от «сейва нет»");
            Assert.AreEqual("9.9.9", loaded.SavedGameVersion, "версию игры показываем игроку");
            Assert.AreEqual(before, File.ReadAllText(Path0),
                "файл более новой версии тронут — так и теряется прогресс при откате на старый билд");
        }

        [Test]
        public void SaveOfOlderSchema_IsUnsupportedUntilMigrationsExist()
        {
            WriteRawEnvelope("1"); // DtoV3 — схема 3

            SaveLoadResult<DtoV3> loaded = _service.TryLoad<DtoV3>(Key);

            Assert.AreEqual(SaveLoadStatus.Unsupported, loaded.Status);
            Assert.AreEqual(1, loaded.SavedSchemaVersion);
        }

        [Test]
        public void FileWithoutEnvelope_IsTreatedAsCorrupt_NotAsEmptySave()
        {
            // Так выглядит сейв, записанный до появления конверта.
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path0, "{\"Name\":\"old\",\"Gold\":5}");

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(SaveLoadStatus.Corrupted, loaded.Status);
        }

        // ── Повреждение ──────────────────────────────────────────────────────

        [Test]
        public void CorruptSave_IsQuarantined_SoExistsStopsClaimingThereIsASave()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path0, "{ this is not json");

            // Ожидаем ругань в консоли — иначе NUnit засчитает LogError как провал теста.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(SaveLoadStatus.Corrupted, loaded.Status);
            Assert.IsFalse(_service.Exists(Key),
                "Exists продолжает отвечать «сейв есть» — именно из-за этого «Продолжить» оставалась " +
                "на экране и молча ничего не делала");
            Assert.IsTrue(File.Exists(Corrupt), "файл игрока должен быть отложен, а не удалён");
        }

        [Test]
        public void CorruptSave_RecoversFromBackupWhenThereIsOne()
        {
            _service.Save(Key, new Dto { Name = "good", Gold = 7 });
            _service.Save(Key, new Dto { Name = "newer", Gold = 8 }); // теперь .bak = good
            File.WriteAllText(Path0, "}}broken{{");

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(SaveLoadStatus.Ok, loaded.Status, "бэкап был, забег можно было спасти");
            Assert.AreEqual("good", loaded.Value.Name);
            Assert.IsTrue(_service.Exists(Key), "восстановленный сейв снова на месте");
        }

        [Test]
        public void Load_ReportsMissingWhenThereIsNoFile()
        {
            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(Key);

            Assert.AreEqual(SaveLoadStatus.Missing, loaded.Status);
            Assert.IsFalse(_service.Exists(Key));
        }

        [Test]
        public void Delete_RemovesBothTheSaveAndItsBackup()
        {
            _service.Save(Key, new Dto { Name = "a", Gold = 1 });
            _service.Save(Key, new Dto { Name = "b", Gold = 2 });

            _service.Delete(Key);

            Assert.IsFalse(File.Exists(Path0));
            Assert.IsFalse(File.Exists(Backup), "бэкап пережил удаление — «начать заново» оставляет хвост");
        }

        // ── Дерево профилей и гильдий ────────────────────────────────────────

        [Test]
        public void NestedKey_CreatesTheWholeTree_AndRoundTrips()
        {
            _service.Save(NestedKey, new Dto { Name = "вложенный", Gold = 3 });

            SaveLoadResult<Dto> loaded = _service.TryLoad<Dto>(NestedKey);

            Assert.AreEqual(SaveLoadStatus.Ok, loaded.Status);
            Assert.AreEqual("вложенный", loaded.Value.Name);
        }

        [Test]
        public void List_EnumeratesProfilesAndGuilds()
        {
            _service.Save(NestedKey, new Dto { Name = "a", Gold = 1 });

            IReadOnlyList<string> profiles = _service.List("profiles");
            IReadOnlyList<string> guilds   = _service.List("profiles/__test_profile/guilds");

            CollectionAssert.Contains(profiles, "__test_profile");
            CollectionAssert.Contains(guilds, "__test_guild");
        }

        [Test]
        public void List_ReturnsEmptyForUnknownPrefix_InsteadOfThrowing()
        {
            Assert.IsEmpty(_service.List("profiles/nope/guilds"));
        }
    }
}
