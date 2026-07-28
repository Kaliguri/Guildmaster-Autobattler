using System.IO;
using Guildmaster.Core.Persistence;
using Guildmaster.Game.Services;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Корень данных игрока обязан пережить переименование игры. Имя ещё проходит проверку на товарный
    /// знак, а <c>Application.persistentDataPath</c> собирается из <c>productName</c> — то есть смена
    /// названия после релиза увела бы игру на пустой каталог и сломала маску Steam Auto-Cloud, причём
    /// молча и необратимо. Тест держит эту развязку.
    /// </summary>
    public sealed class GameDataPathTests
    {
        [Test]
        public void Root_DoesNotDependOnTheMarketingName()
        {
            Assert.That(GameDataPath.Root, Does.Not.Contain(Application.productName),
                $"корень данных содержит маркетинговое имя '{Application.productName}' — " +
                "переименование игры унесёт сейвы игроков");
        }

        [Test]
        public void Root_UsesCodeNames()
        {
            string expectedTail = Path.Combine(GameDataPath.CompanyFolder, GameDataPath.ProductFolder);

            Assert.That(GameDataPath.Root, Does.EndWith(expectedTail),
                "корень должен собираться из кодовых имён — их и не переименовываем");
        }

        [Test]
        public void BothStores_LiveUnderTheRoot()
        {
            // Маска Auto-Cloud указывает на Saves/ внутри этого корня; Local/ намеренно рядом, а не внутри.
            Assert.IsTrue(Path.Combine(GameDataPath.Root, JsonFileSaveService.SavesFolder).StartsWith(GameDataPath.Root));
            Assert.IsTrue(Path.Combine(GameDataPath.Root, LocalJsonFileSaveService.LocalFolder).StartsWith(GameDataPath.Root));
            Assert.AreNotEqual(JsonFileSaveService.SavesFolder, LocalJsonFileSaveService.LocalFolder);
        }
    }
}
