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
                    bool narrow = floor <= cfg.EdgeColumns || floor > lastFloor - cfg.EdgeColumns;
                    if (narrow)
                        Assert.AreEqual(cfg.EdgeColumnWidth, widthOf[floor],
                            $"Горловина: этаж {floor} (сид {seed}).");
                    else
                        Assert.GreaterOrEqual(widthOf[floor], 5,
                            $"Середина: этаж {floor} должен быть широким, а не {widthOf[floor]} (сид {seed}).");
                }
            }
        }
    }
}
