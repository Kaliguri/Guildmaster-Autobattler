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
