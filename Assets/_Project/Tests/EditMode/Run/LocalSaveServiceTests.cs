using System.IO;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Settings;
using Guildmaster.Game.Services;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Машинно-локальное хранилище: настройки дисплея не должны уезжать в Steam Cloud. Инвариант дешёвый,
    /// но ломается молча — путь однажды поправят «для порядка», и чужое разрешение приедет на второй ПК.
    /// </summary>
    public sealed class LocalSaveServiceTests
    {
        private const string Key = "__test_machine";

        private static string SavesRoot => Path.Combine(Application.persistentDataPath, JsonFileSaveService.SavesFolder);
        private static string LocalRoot => Path.Combine(Application.persistentDataPath, LocalJsonFileSaveService.LocalFolder);
        private static string LocalPath => Path.Combine(LocalRoot, Key + ".json");

        private LocalJsonFileSaveService _local;

        [SetUp]
        public void SetUp()
        {
            _local = new LocalJsonFileSaveService();
            _local.Delete(Key);
        }

        [TearDown]
        public void TearDown() => _local.Delete(Key);

        [Test]
        public void LocalStore_WritesOutsideTheCloudMask()
        {
            _local.Save(Key, new DisplaySettings { Width = 1920, Height = 1080 });

            Assert.IsTrue(File.Exists(LocalPath), "локальный файл не там, где обещано");
            Assert.IsFalse(LocalPath.StartsWith(SavesRoot),
                "настройки дисплея попали под Saves/ — Steam Cloud увезёт разрешение на чужую машину");
        }

        [Test]
        public void LocalStore_IsSeparateFromThePlayerStore()
        {
            var cloud = new JsonFileSaveService();
            try
            {
                _local.Save(Key, new DisplaySettings { Width = 1280, Height = 720 });

                Assert.IsFalse(cloud.Exists(Key), "ключ виден облачному хранилищу — каталоги не разделены");
            }
            finally
            {
                cloud.Delete(Key);
            }
        }

        [Test]
        public void DisplaySettings_RoundTrip_KeepsUnsetFieldsUnset()
        {
            // «Не задано» обязано пережить круг: записанное однажды нативное разрешение не переживёт
            // смену монитора, а пустое поле — переживёт.
            _local.Save(Key, new DisplaySettings { Mode = WindowMode.ExclusiveFullscreen });

            SaveLoadResult<DisplaySettings> loaded = _local.TryLoad<DisplaySettings>(Key);

            Assert.AreEqual(SaveLoadStatus.Ok, loaded.Status);
            Assert.AreEqual(WindowMode.ExclusiveFullscreen, loaded.Value.Mode);
            Assert.IsNull(loaded.Value.Width,  "разрешение не задавали — оно должно остаться незаданным");
            Assert.IsNull(loaded.Value.Height);
            Assert.IsNull(loaded.Value.RefreshNumerator);
        }

        [Test]
        public void DisplaySettings_RoundTrip_KeepsRationalRefreshRate()
        {
            // 59.94 Гц = 60000/1001. В double такое не уложить без потери точности — потому и рациональное.
            _local.Save(Key, new DisplaySettings { RefreshNumerator = 60000, RefreshDenominator = 1001 });

            SaveLoadResult<DisplaySettings> loaded = _local.TryLoad<DisplaySettings>(Key);

            Assert.AreEqual(SaveLoadStatus.Ok, loaded.Status);
            Assert.AreEqual(60000u, loaded.Value.RefreshNumerator);
            Assert.AreEqual(1001u, loaded.Value.RefreshDenominator);
        }
    }
}
