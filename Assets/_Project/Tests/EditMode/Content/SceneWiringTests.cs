using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Разводка сцен: обязательные serialized-ссылки скоупов и совпадение общих ассетов между сценами.
    /// Аудит 2026-07-26 (RC-1): в CoreScene — единственной сцене, которая шипится — три конфига стояли
    /// пустыми, а инвариант «ТОТ ЖЕ ассет, что в CombatLifetimeScope» жил в <c>[Tooltip]</c>, то есть его
    /// не мог проверить никто. Дефект не видно ни в play-mode (CombatSystemsScene заполнена), ни в тестах
    /// (те строят конфиги через CreateInstance). Проверяем то, что реально грузится в билде.
    /// </summary>
    public sealed class SceneWiringTests
    {
        /// <summary>
        /// Поля, пустота которых — баг разводки, а не «пусто = дефолт». Опциональные сюда НЕ вносим:
        /// у <c>_audioCatalog</c> есть осознанный фолбэк (тишина вместо падения), и он документирован.
        /// </summary>
        private static readonly Dictionary<string, string[]> Required = new()
        {
            ["RootLifetimeScope"] = new[]
            {
                "_contentDatabase",   // без него ContentRegistry падает прямо в Configure
                "_gameConfig",
                "_statsConfig",       // потребитель — IUnitStatPreview: пусто = панель инвентаря врёт
                "_classBalanceConfig",
                "_actConfig",         // владелец параметров карты акта (T-5), фолбэк — второй владелец
            },
            ["CombatLifetimeScope"] = new[]
            {
                "_statsConfig",
                "_classBalanceConfig",
            },
            ["UiRootBootstrap"] = new[]
            {
                // Экраны забега. Пусто = MenuRouter отказывается показать шаг (теперь громко, аудит фолбэков
                // 2026-07-26, п.1), а раньше молча выполнял колбэк УСПЕХА: узел засчитывался сам, награда
                // пропускалась, главное меню закрывало игру. Ловим здесь, до билда.
                "_pauseScreen",
                "_settingsScreen",
                "_loadoutScreen",
                "_rewardScreen",
                "_eventScreen",            // он же кадр прощания узла
                "_continueScreen",
                "_shopScreen",
                "_chestScreen",
                "_campScreen",             // стоит якорем на этажах 8 и 13 КАЖДОГО акта — мимо не пройти
                "_outcomeScreen",
                "_mainMenuScreen",
                "_titleCardScreen",
                "_runModeBar",
                "_loadoutInventoryScreen",
                "_arcanaCard",
                // _loadoutHubScreen СЮДА НЕ ВНОСИМ: старый хаб помечен к удалению (волна 2 аудита кода).
            },
            ["CombatPresenter"] = new[]
            {
                // Единственный владелец цветов HP и щита (T-12/T-13). Пусто = бар и боевые цифры
                // рисуются цветом материала, и это молча, поэтому ловим здесь.
                "_colorPalette",
            },
        };

        /// <summary>Ассеты, которые обязаны совпадать во всех сценах, где вообще объявлены.</summary>
        private static readonly string[] SharedAcrossScenes = { "_statsConfig", "_classBalanceConfig" };

        [Test]
        public void EveryBuildScene_ExistsOnDisk()
        {
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Assert.IsNotNull(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path),
                    $"Сцена включена в билд, но её нет на диске: {entry.path}");
            }
        }

        [Test]
        public void RequiredSerializedReferences_AreAssignedInEveryBuildScene()
        {
            var missing = new List<string>();

            ForEachBuildScene((scene, component, so) =>
            {
                if (!Required.TryGetValue(component.GetType().Name, out string[] fields)) return;

                foreach (string field in fields)
                {
                    SerializedProperty p = so.FindProperty(field);
                    if (p == null)
                    {
                        missing.Add($"{scene.name} / {component.GetType().Name}: поля {field} больше нет — " +
                                    "обнови реестр Required в этом тесте");
                        continue;
                    }

                    if (p.objectReferenceValue == null)
                        missing.Add($"{scene.name} / {component.GetType().Name}.{field} не назначено");
                }
            });

            Assert.IsEmpty(missing, "Пустые обязательные ссылки:\n" + string.Join("\n", missing));
        }

        [Test]
        public void SharedConfigs_AreTheSameAssetInEveryScene()
        {
            // поле → (сцена/компонент → ассет)
            var seen = new Dictionary<string, List<(string where, Object asset)>>();

            ForEachBuildScene((scene, component, so) =>
            {
                foreach (string field in SharedAcrossScenes)
                {
                    SerializedProperty p = so.FindProperty(field);
                    if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) continue;

                    if (!seen.TryGetValue(field, out var list))
                        seen[field] = list = new List<(string, Object)>();

                    list.Add(($"{scene.name}/{component.GetType().Name}", p.objectReferenceValue));
                }
            });

            foreach (KeyValuePair<string, List<(string where, Object asset)>> pair in seen)
            {
                Object first = pair.Value[0].asset;
                foreach ((string where, Object asset) in pair.Value)
                {
                    Assert.AreSame(first, asset,
                        $"{pair.Key} расходится между сценами: " +
                        string.Join(", ", pair.Value.Select(v => $"{v.where}={(v.asset == null ? "NULL" : v.asset.name)}")) +
                        ". Обе сцены должны смотреть на один ассет — иначе панель инвентаря считает не то, что бой.");
                }
            }
        }

        /// <summary>
        /// Проходит по сценам билда, отдавая каждый компонент со скоупом.
        /// <para>Сцены открываются как PREVIEW — в изолированном состоянии, невидимом для иерархии редактора.
        /// Обычный <c>OpenScene</c>+<c>CloseScene</c> здесь не годится: тест трогал бы сцены, открытые у
        /// разработчика, а закрытие активной сцены способно подвесить прогон.</para>
        /// </summary>
        private static void ForEachBuildScene(System.Action<Scene, Component, SerializedObject> visit)
        {
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(entry.path);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        if (!Required.ContainsKey(component.GetType().Name) &&
                            !component.GetType().Name.EndsWith("LifetimeScope")) continue;

                        visit(scene, component, new SerializedObject(component));
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }
    }
}
