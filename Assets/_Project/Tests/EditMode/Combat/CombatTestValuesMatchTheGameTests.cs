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
        private const string CombatScenePath = "Assets/_Project/Scenes/CombatSystemsScene.unity";

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
        public void CellSize_MatchesTheBattleScopeInTheScene()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(CombatScenePath);
            try
            {
                SerializedProperty field = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Component c in root.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c.GetType().Name != "CombatLifetimeScope") continue;
                        field = new SerializedObject(c).FindProperty("_spatialHashCellSize");
                        break;
                    }
                    if (field != null) break;
                }

                Assert.IsNotNull(field, "В боевой сцене не найден CombatLifetimeScope с _spatialHashCellSize");
                Assert.AreEqual(field.floatValue, CombatTestValues.CellSize, 1e-6f,
                    "Размер ячейки хэша в тестах разошёлся со сценой: соседство юнитов в тестах считается " +
                    "не так, как в бою");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
