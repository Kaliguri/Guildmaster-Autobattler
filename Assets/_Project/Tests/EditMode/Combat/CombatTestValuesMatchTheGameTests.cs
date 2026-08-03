using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Фикстура боевых тестов обязана совпадать с тем, на чём идёт игра. Иначе весь боевой ярус считает
    /// по своим числам и остаётся зелёным ровно тогда, когда баланс уехал (аудит 2026-07-26, TS-17).
    /// </summary>
    public sealed class CombatTestValuesMatchTheGameTests
    {
        // Боевой скоуп живёт ПРЕФАБОМ, а не объектом сцены: с 02.08.2026 он рождается на каждый бой и
        // умирает вместе с ним, поэтому в сцене его нет и быть не может.
        private const string BattleScopePrefabPath = "Assets/_Project/Prefabs/Systems/BattleScope.prefab";

        [Test]
        public void ArmorK_MatchesTheShippedStatsConfig()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(StatsConfig)}");
            Assert.AreEqual(1, guids.Length, "Ожидается ровно один StatsConfig.");

            var cfg = AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

            Assert.AreEqual(cfg.ArmorConstantK, CombatTestValues.ArmorK, 1e-6f,
                "Константа брони в тестах разошлась с ассетом: бой считает митигейт по другому числу, " +
                "чем весь боевой тест-ярус");
        }

        [Test]
        public void CellSize_MatchesTheBattleScopePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleScopePrefabPath);
            Assert.IsNotNull(prefab, $"Префаб боевого скоупа не найден: {BattleScopePrefabPath}. " +
                                     "Из него рождается каждый бой — без него игра боя не откроет.");

            SerializedProperty field = null;
            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "CombatLifetimeScope") continue;
                field = new SerializedObject(c).FindProperty("_spatialHashCellSize");
                break;
            }

            Assert.IsNotNull(field, "В префабе боевого скоупа не найден CombatLifetimeScope с _spatialHashCellSize");
            Assert.AreEqual(field.floatValue, CombatTestValues.CellSize, 1e-6f,
                "Размер ячейки хэша в тестах разошёлся с боевым скоупом: соседство юнитов в тестах " +
                "считается не так, как в бою");
        }
    }
}
