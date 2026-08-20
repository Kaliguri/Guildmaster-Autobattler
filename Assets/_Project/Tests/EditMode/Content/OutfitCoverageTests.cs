using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Облачение адресует части рига СТРОКОЙ, и это шов между тремя вещами сразу: ассетом облачения,
    /// иерархией префаба вида и конвенцией имён рига. Ни одна из сторон не узнает о поломке сама —
    /// строка, которой в риге нет, просто не найдёт часть и промолчит.
    /// <para>
    /// Цена молчания видна на заказе, ради которого облачение и заведено: «щит только у Защитника»
    /// делается строкой <c>Weapon_L_Shield_Art</c> с пустым спрайтом. Опечатка в имени — и щит
    /// остаётся у всех, а выглядит это как «код не работает», хотя данные просто говорят не о том.
    /// </para>
    /// </summary>
    public sealed class OutfitCoverageTests
    {
        private static IEnumerable<UnitData> Units()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UnitData",
                     new[] { "Assets/_Project/ScriptableObjects" }))
            {
                var unit = AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(guid));
                if (unit != null) yield return unit;
            }
        }

        /// <summary>Имена узлов-рисунков в префабе вида — то, чем облачение вправе адресовать часть.</summary>
        private static HashSet<string> PartNames(GameObject viewPrefab)
        {
            var names = new HashSet<string>();
            if (viewPrefab == null) return names;
            foreach (var sr in viewPrefab.GetComponentsInChildren<SpriteRenderer>(true))
                names.Add(sr.name);
            return names;
        }

        /// <summary>
        /// Каждая строка облачения обязана назвать часть, которая в риге ЕСТЬ. Проверяется против
        /// префаба того юнита, который это облачение носит: одно и то же облачение на разных ригах —
        /// законный случай, но каждый носитель обязан такую часть иметь.
        /// </summary>
        [Test]
        public void EveryOutfitPiece_NamesAPartThatExistsOnItsWearersRig()
        {
            var complaints = new List<string>();

            foreach (UnitData unit in Units())
            {
                OutfitData outfit = unit.Outfit;
                if (outfit == null) continue;

                HashSet<string> parts = PartNames(unit.ViewPrefab);
                Assert.That(parts, Is.Not.Empty,
                    $"{unit.Id}: у префаба вида нет ни одной части со спрайтом — проверять облачение не на чем.");

                foreach (OutfitPiece piece in outfit.Pieces)
                {
                    if (string.IsNullOrEmpty(piece.Part))
                    {
                        complaints.Add($"{outfit.name}: строка с пустым ИМЕНЕМ части (носитель {unit.Id}). " +
                                       "Пустое имя не адресует ничего — если часть нужно спрятать, имя " +
                                       "обязательно, а пустым оставляется СПРАЙТ.");
                        continue;
                    }

                    if (!parts.Contains(piece.Part))
                        complaints.Add($"{outfit.name}: части '{piece.Part}' нет в риге носителя {unit.Id} " +
                                       $"({unit.ViewPrefab?.name}). Есть: {string.Join(", ", parts.OrderBy(n => n))}");
                }
            }

            Assert.That(complaints, Is.Empty,
                "Облачение называет части, которых в риге нет:\n  " + string.Join("\n  ", complaints));
        }

        /// <summary>
        /// Щит носит тот, у кого есть механика блока щитом, и только он. Инвариант живёт между данными
        /// (облачение) и китом (эффект блока) — разойтись они могут молча в обе стороны: боец с «Оплотом»
        /// без щита в руке и лучник со щитом за спиной выглядят одинаково «нормально» в инспекторе.
        /// </summary>
        [Test]
        public void ShieldIsWornByThoseWhoBlockWithIt_AndByNobodyElse()
        {
            const string ShieldPart = "Weapon_L_Shield_Art";

            // GUID'ы эффектов-«Оплотов»: блок ЩИТОМ, а не парированием. Признак механический — новый
            // щитоносец попадёт под правило сам, без правки списка имён. У брузера тоже блок, но это
            // парирующая стойка, и щита в ней нет — отсюда второе условие по имени файла.
            var bulwarkGuids = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:EffectData",
                     new[] { "Assets/_Project/ScriptableObjects/Effects" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Bulwark", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (System.IO.File.ReadAllText(path)
                        .IndexOf("BlockComponent", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                bulwarkGuids.Add(guid);
            }

            Assert.That(bulwarkGuids, Is.Not.Empty, "Не нашлось ни одного эффекта-«Оплота» с блоком — " +
                                                    "правило «щит у того, кто им блокирует» проверять не на чем.");

            var wrong = new List<string>();
            foreach (UnitData unit in Units())
            {
                string yaml = System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(unit));
                bool blocksWithShield = bulwarkGuids.Any(yaml.Contains);

                // Прячет ли облачение щит: строка про щит с пустым спрайтом.
                bool hidesShield = unit.Outfit != null
                                   && unit.Outfit.TryResolve(ShieldPart, out Sprite s) && s == null;

                if (blocksWithShield && hidesShield)
                    wrong.Add($"{unit.Id}: блокирует «Оплотом», но облачение прячет щит");
                if (!blocksWithShield && !hidesShield)
                    wrong.Add($"{unit.Id}: щитом не блокирует, но щит на нём остался");
            }

            Assert.That(wrong, Is.Empty,
                "Щит и механика блока разошлись:\n  " + string.Join("\n  ", wrong));
        }
    }
}
