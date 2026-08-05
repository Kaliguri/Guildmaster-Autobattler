using System.Collections.Generic;
using System.Linq;
using Guildmaster.Presentation;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Кого красит тинт юнита: волосы и всё, что в руках, — и никого больше (решение Макса 05.08.2026).
    ///
    /// Правило держится ДВУМЯ разными признаками: предмет опознаётся хватом, волосы — именем узла
    /// рисунка. Второй признак хрупок по своей природе, и ломается он молча: назови узел «Head_Locks_Art»
    /// — тинт просто перестанет их красить, юнит выйдет на арену с волосами цвета префаба, и ни одной
    /// строки в консоли. Ровно от этого здесь тест.
    /// </summary>
    public class BodyTintScopeTests
    {
        const string ViewFolder = "Assets/_Project/Prefabs/Units";

        static IEnumerable<GameObject> BattleViews()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ViewFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<UnitView>() != null) yield return prefab;
            }
        }

        /// <summary>Тот же признак, что и в <c>SkeletalBodyVisual.TintMask</c>.</summary>
        static bool Painted(in UnitPart part) =>
            part.IsHeld || RigNaming.IsHair(part.Renderer != null ? part.Renderer.name : null);

        /// <summary>
        /// Некрашеная часть остаётся ТОЙ, КАКОЙ нарисована. Белый в <c>SpriteRenderer.color</c> выглядит
        /// нейтральным и им не является: художник красит vertex-цветом прямо в риге (лицо телесным, тело
        /// холодно-серым), и «нейтральный» белый стирает эту работу. Поймано глазами Макса 05.08.2026 —
        /// белое лицо; тестом ловится молча.
        /// </summary>
        [Test]
        public void AnUnpaintedPartKeepsItsAuthoredColour()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Units/UnitView_BoneStorybook.prefab");
            Assert.That(prefab, Is.Not.Null, "Боевой вид Storybook не найден.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var body = instance.GetComponentInChildren<SkeletalBodyVisual>(includeInactive: true);
                Assert.That(body, Is.Not.Null);

                // Авторские цвета до всякой покраски — эталон, с которым сверяемся.
                var authored = body.Renderers.Select(r => r == null ? Color.white : r.color).ToList();

                var tint = new Color(0.2f, 0.9f, 0.4f, 1f);
                body.Apply(new BodyVisualState(tint, 0f, Color.white, 0f, Color.white, 0f, 0f, 0f,
                                               0f, Color.white, 0f, Color.white, PartMask.Empty, 0f));

                IReadOnlyList<UnitPart> parts = body.Parts.Parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    UnitPart part = parts[i];
                    Color now = part.Renderer.color;

                    if (Painted(part))
                        Assert.That((Vector4)now, Is.EqualTo((Vector4)tint).Using(new Vector4Comparer()),
                            $"{part.Renderer.name} обязан принять тинт юнита.");
                    else
                        Assert.That(new Vector3(now.r, now.g, now.b),
                            Is.EqualTo(new Vector3(authored[i].r, authored[i].g, authored[i].b))
                              .Using(new Vector3Comparer()),
                            $"{part.Renderer.name} потерял авторский цвет: был " +
                            $"{authored[i]}, стал {now}. Белый — не нейтральное значение.");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        sealed class Vector3Comparer : IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }

        sealed class Vector4Comparer : IEqualityComparer<Vector4>
        {
            public bool Equals(Vector4 a, Vector4 b) => (a - b).sqrMagnitude < 1e-6f;
            public int GetHashCode(Vector4 v) => v.GetHashCode();
        }

        [Test]
        public void OnlyHairAndHeldItemsTakeTheUnitTint()
        {
            var offenders = new List<string>();
            int checkedRigs = 0;
            bool hairSeenAnywhere = false;

            foreach (GameObject prefab in BattleViews())
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var body = instance.GetComponentInChildren<SkeletalBodyVisual>(includeInactive: true);
                    if (body == null) continue;   // покадровое тело: одна картинка, делить в нём нечего
                    checkedRigs++;

                    IReadOnlyList<UnitPart> parts = body.Parts.Parts;
                    hairSeenAnywhere |= parts.Any(p => RigNaming.IsHair(p.Renderer.name));

                    // Причёска обязательной НЕ объявляется: у дев-болванки её нет, и это законно —
                    // требовать волосы от каждого рига значит красить тест под один кит.
                    // Предметы: у боевого вида есть хотя бы один, иначе тинту нечего красить вовсе.
                    if (!parts.Any(p => p.IsHeld))
                        offenders.Add($"{prefab.name}: ни одного предмета в руках — тинту нечего красить.");

                    // Обратная сторона правила: тело под тинт не попадает. Торс — самая крупная часть,
                    // и если правило поедет, поедет оно первым делом на нём.
                    foreach (UnitPart part in parts)
                    {
                        if (!Painted(part)) continue;
                        string node = part.Renderer.name;
                        if (node.Contains("Torso") || node.Contains("Leg") || node.Contains("Foot")
                            || node.Contains("Face") || node.Contains("Arm"))
                            offenders.Add($"{prefab.name}: {node} принимает тинт, хотя это тело — " +
                                          "красятся только волосы и то, что в руках.");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            Assert.That(checkedRigs, Is.GreaterThan(0), "Не найдено ни одного скелетного боевого вида.");

            // Признак волос живёт ИМЕНЕМ узла, и ломается он молча: переименуй «Head_Hair_Art» — и
            // причёски просто перестанут краситься. Хотя бы один риг в проекте обязан его подтверждать.
            Assert.That(hairSeenAnywhere, Is.True,
                $"Ни в одном виде не нашлось узла волос («{RigNaming.HairToken}» в имени) — либо " +
                "конвенция имён уехала, либо признак больше ничего не опознаёт.");
            Assert.That(offenders, Is.Empty,
                "Тинт юнита красит волосы и то, что в руках:\n  " + string.Join("\n  ", offenders));
        }
    }
}
