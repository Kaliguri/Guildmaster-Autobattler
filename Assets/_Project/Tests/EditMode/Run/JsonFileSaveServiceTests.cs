using System;
using System.IO;
using Guildmaster.Game.Services;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Настоящий дисковый бэкенд сейва. Остальные тесты забега подставляют самодельные двойники
    /// <c>ISaveService</c>, которые держат объект в памяти по ссылке — поэтому ни сериализацию, ни
    /// повреждение файла, ни прерванную запись не видел никто (аудит 2026-07-26: TS-10, TS-21).
    /// </summary>
    public sealed class JsonFileSaveServiceTests
    {
        [Serializable]
        public sealed class Dto
        {
            public string Name;
            public int Gold;
        }

        private const string Key = "__test_run_save";

        private static string Path0    => System.IO.Path.Combine(Application.persistentDataPath, Key + ".json");
        private static string Backup   => Path0 + ".bak";
        private static string Temp     => Path0 + ".tmp";
        private static string Corrupt  => Path0 + ".corrupt";

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
        }

        [Test]
        public void SaveThenLoad_RoundTripsThroughRealSerialization()
        {
            _service.Save(Key, new Dto { Name = "Бонрайт", Gold = 42 });

            Dto loaded = _service.Load<Dto>(Key);

            Assert.IsNotNull(loaded);
            Assert.AreEqual("Бонрайт", loaded.Name);
            Assert.AreEqual(42, loaded.Gold);
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
            Assert.AreEqual("second", _service.Load<Dto>(Key).Name);
            StringAssert.Contains("first", File.ReadAllText(Backup));
        }

        [Test]
        public void CorruptSave_IsQuarantined_SoExistsStopsClaimingThereIsASave()
        {
            File.WriteAllText(Path0, "{ this is not json");

            // Ожидаем ругань в консоли — иначе NUnit засчитает LogError как провал теста.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Dto loaded = _service.Load<Dto>(Key);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(loaded, "битый сейв не должен притворяться загруженным");
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
            Dto loaded = _service.Load<Dto>(Key);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(loaded, "бэкап был, забег можно было спасти");
            Assert.AreEqual("good", loaded.Name);
            Assert.IsTrue(_service.Exists(Key), "восстановленный сейв снова на месте");
        }

        [Test]
        public void Load_ReturnsDefaultWhenThereIsNoFile()
        {
            Assert.IsNull(_service.Load<Dto>(Key));
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
    }
}
