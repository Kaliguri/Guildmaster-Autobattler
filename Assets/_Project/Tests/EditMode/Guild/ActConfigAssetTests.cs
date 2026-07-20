using System.Linq;
using Guildmaster.Core.Random;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Проверяет КОНФИГ, ПО КОТОРОМУ РЕАЛЬНО ИДЁТ ИГРА, а не дефолты из кода.
    /// <para>Зачем отдельно: дефолты <see cref="MapGenConfig"/> живут в C#, но забег генерируется из
    /// <c>ActConfig.asset</c> — сериализованной копии. Правка дефолтов в коде НЕ меняет ассет: старые
    /// значения остаются, новые поля приезжают пустыми. Ровно так карта и осталась по 2-3 узла на этаже,
    /// когда профиль в коде уже был 5-6 (play-QA Макса, 2026-07-20). Тест ловит этот рассинхрон.</para>
    /// </summary>
    public sealed class ActConfigAssetTests
    {
        private const string AssetPath = "Assets/_Project/ScriptableObjects/Configs/ActConfig.asset";

        private static MapGenConfig LoadPlayedConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ActConfig>(AssetPath);
            Assert.IsNotNull(asset, $"Не найден {AssetPath} — по нему генерируется акт в игре.");
            return asset.ToGenConfig();
        }

        /// <summary>
        /// Ассет несёт ТУ ЖЕ раскладку акта, что канон в коде: глубину, талию (сундук → привалы) и привал
        /// перед боссом. Прежде тест сторожил только ширину — и пропустил ровно тот же рассинхрон второй раз:
        /// привал был дописан в дефолты C#, а игра осталась на старом ассете без единого привала.
        /// </summary>
        [Test]
        public void PlayedConfig_MatchesCanonicalActLayout()
        {
            MapGenConfig cfg = LoadPlayedConfig();
            var canon = new MapGenConfig().Validated();

            Assert.AreEqual(canon.Columns, cfg.Columns, "Глубина акта в ассете разошлась с каноном в коде.");

            foreach (AnchorRule expected in canon.Anchors)
            {
                AnchorRule actual = cfg.Anchors.FirstOrDefault(a => a.Floor == expected.Floor);
                Assert.AreEqual(expected.Type, actual.Type,
                    $"Этаж-якорь {expected.Floor}: в ассете тип {actual.Type}, канон — {expected.Type}.");
                Assert.AreEqual(expected.Width, actual.Width,
                    $"Этаж-якорь {expected.Floor}: ширина в ассете разошлась с каноном.");
            }
        }

        /// <summary>Типы, которые обязаны встречаться в реально играемом акте (иначе иконка есть, а узла нет).</summary>
        [TestCase(MapNodeType.Camp)]
        [TestCase(MapNodeType.Unknown)]
        [TestCase(MapNodeType.Chest)]
        [TestCase(MapNodeType.Shop)]
        public void PlayedConfig_ActuallyGeneratesNodeType(MapNodeType type)
        {
            MapGenConfig cfg = LoadPlayedConfig();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                MapState map = MapGenerator.Generate(new XorShiftRng(seed), cfg);
                if (map.Nodes.Any(n => n.Type == type)) return;
            }
            Assert.Fail($"За 20 сидов ассет не выдал ни одного узла типа {type} — тип есть в коде, но не в игре.");
        }

        [Test]
        public void PlayedConfig_HasWideMiddle()
        {
            MapGenConfig cfg = LoadPlayedConfig();
            Assert.GreaterOrEqual(cfg.MinColumnWidth, 5,
                "Середина акта должна быть широкой (решение Макса: резкий рост до 5-6).");
            Assert.GreaterOrEqual(cfg.MaxColumnWidth, cfg.MinColumnWidth);
        }

        [Test]
        public void PlayedConfig_ProducesNarrowEndsAndWideMiddle()
        {
            MapGenConfig cfg = LoadPlayedConfig();
            int lastFloor = cfg.Columns - 2;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                MapState map = MapGenerator.Generate(new XorShiftRng(seed), cfg);
                var widthOf = map.Nodes.GroupBy(n => n.Floor).ToDictionary(g => g.Key, g => g.Count());

                for (int floor = 1; floor <= lastFloor; floor++)
                {
                    int anchored = AnchorWidth(cfg, floor);
                    bool edge = floor <= cfg.EdgeColumns || floor > lastFloor - cfg.EdgeColumns;

                    if (anchored > 0)
                        Assert.AreEqual(anchored, widthOf[floor],
                            $"Якорный этаж {floor} задаёт свою ширину (сид {seed}).");
                    else if (edge)
                        Assert.AreEqual(cfg.EdgeColumnWidth, widthOf[floor],
                            $"Горловина: этаж {floor} (сид {seed}).");
                    else
                        Assert.That(widthOf[floor], Is.InRange(cfg.MinColumnWidth, cfg.MaxColumnWidth),
                            $"Середина: этаж {floor} обязан лежать в диапазоне конфига (сид {seed}).");
                }
            }
        }

        /// <summary>
        /// Ширина середины — именно ДИАПАЗОН, а не одно число: на разных этажах и сидах должны встречаться
        /// разные значения. Иначе «рандом ширины» тихо выродился бы в константу и никто бы не заметил.
        /// </summary>
        [Test]
        public void PlayedConfig_MiddleWidthActuallyVaries()
        {
            MapGenConfig cfg = LoadPlayedConfig();
            if (cfg.MinColumnWidth == cfg.MaxColumnWidth) Assert.Pass("Диапазон намеренно вырожден в одно число.");

            int lastFloor = cfg.Columns - 2;
            var seen = new System.Collections.Generic.HashSet<int>();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                MapState map = MapGenerator.Generate(new XorShiftRng(seed), cfg);
                var widthOf = map.Nodes.GroupBy(n => n.Floor).ToDictionary(g => g.Key, g => g.Count());

                for (int floor = 1; floor <= lastFloor; floor++)
                {
                    if (AnchorWidth(cfg, floor) > 0) continue;
                    if (floor <= cfg.EdgeColumns || floor > lastFloor - cfg.EdgeColumns) continue;
                    seen.Add(widthOf[floor]);
                }
            }

            Assert.Greater(seen.Count, 1,
                $"Ширина середины не роллится — везде {string.Join(",", seen)}.");
        }

        private static int AnchorWidth(MapGenConfig cfg, int floor)
        {
            if (cfg.Anchors == null) return 0;
            foreach (AnchorRule a in cfg.Anchors)
                if (a.Floor == floor && a.Width > 0) return a.Width;
            return 0;
        }
    }
}
