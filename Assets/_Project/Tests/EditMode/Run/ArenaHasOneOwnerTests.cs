using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// У арены ровно один владелец: её печёт мир, а бой берёт готовую из предка.
    /// </summary>
    /// <remarks>
    /// <b>Почему тестом.</b> Инвариант кросс-файловый и ломается бесшумно: до 02.08.2026 боевой скоуп
    /// строил из авторинга ВТОРУЮ арену и регистрировал её же, при том что комментарий в
    /// <c>WorldLifetimeScope</c> утверждал обратное. Никто не заметил, потому что VContainer отдаёт
    /// ближайшую регистрацию, а значения совпадали — оба искали один и тот же объект в сценах.
    /// Разойтись они могли в первый же день, когда арен станет больше одной.
    /// </remarks>
    public sealed class ArenaHasOneOwnerTests
    {
        // Собрано из кусков намеренно: иначе тест нашёл бы сам себя.
        private static readonly string Search = "FindAnyObjectByType<" + "ArenaLayoutAuthoring>";

        [Test]
        public void OnlyOneScope_BakesTheArena()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts");
            var owners = new List<string>();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                if (File.ReadAllText(file).Contains(Search)) owners.Add(Path.GetFileName(file));

            Assert.AreEqual(1, owners.Count,
                $"Арену печёт больше одного скоупа: {string.Join(", ", owners)}. " +
                "Владелец — WorldLifetimeScope; бой резолвит готовый ArenaLayoutData из предка.");
            Assert.AreEqual("WorldLifetimeScope.cs", owners[0], "И владелец именно мир, а не бой");
        }
    }
}
