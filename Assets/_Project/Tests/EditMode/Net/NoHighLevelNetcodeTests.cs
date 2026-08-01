using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Граница сетевого стека: высокоуровневого netcode в проекте нет и быть не должно.
    /// </summary>
    /// <remarks>
    /// Решение Макса 02.08.2026: играем только в Steam, поэтому работаем с Steam Networking Sockets
    /// напрямую, а NGO/Mirror/FishNet не берём вовсе. Они продают репликацию сетевых объектов, RPC и
    /// спавн — у нас нет ни одного сетевого объекта: бой раздаётся лентой чанками, состояние забега —
    /// логом команд, присутствие — своим пакетом.
    /// <para><b>Почему тестом, а не договорённостью.</b> Инвариант кросс-файловый: вернуть NGO может
    /// кто угодно и незаметно — чужим префабом, пакетом в манифесте, одним <c>using</c> в правке. Тест
    /// падает в тот же день, комментарий не заметит никто.</para>
    /// </remarks>
    public sealed class NoHighLevelNetcodeTests
    {
        // Собрано из кусков намеренно: иначе тест нашёл бы сам себя и краснел бы всегда.
        private static readonly string Namespace = "Unity" + ".Netcode";

        private static readonly string[] ForbiddenPackages =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.multiplayer.tools",
            "com.unity.multiplayer.center",
        };

        [Test]
        public void ProjectCode_DoesNotReferenceHighLevelNetcode()
        {
            string root = Path.Combine(Application.dataPath, "_Project");
            var offenders = new List<string>();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.EndsWith("NoHighLevelNetcodeTests.cs")) continue;
                if (File.ReadAllText(file).Contains(Namespace)) offenders.Add(Relative(file));
            }

            Assert.IsEmpty(offenders,
                $"Высокоуровневый netcode вернулся в проект: {string.Join(", ", offenders)}. " +
                "Мы работаем со Steam напрямую через свой INetTransport; если понадобилась репликация " +
                "объектов — это разговор с Максом, а не молчаливая правка.");
        }

        [Test]
        public void Manifest_DoesNotBringNetcodePackages()
        {
            string manifest = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            Assert.IsTrue(File.Exists(manifest), "манифест пакетов не найден");

            string text = File.ReadAllText(manifest);
            foreach (string package in ForbiddenPackages)
                Assert.IsFalse(text.Contains(package),
                    $"Пакет {package} вернулся в манифест. Он тянет за собой стек, от которого мы ушли " +
                    "02.08.2026, и платит за него каждым domain reload.");
        }

        [Test]
        public void Scenes_DoNotCarryNetworkObjects()
        {
            string scenes = Path.Combine(Application.dataPath, "_Project", "Scenes");
            if (!Directory.Exists(scenes)) return;

            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(scenes, "*.unity", SearchOption.AllDirectories))
                if (File.ReadAllText(file).Contains(Namespace)) offenders.Add(Relative(file));

            Assert.IsEmpty(offenders,
                $"В сценах лежат объекты высокоуровневого netcode: {string.Join(", ", offenders)}");
        }

        private static string Relative(string path) =>
            path.Replace(Application.dataPath, "Assets").Replace('\\', '/');
    }
}
