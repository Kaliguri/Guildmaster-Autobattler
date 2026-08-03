using System.Collections.Generic;
using System.Linq;
using Guildmaster.ContentHub.Editor;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// У каждого юнита есть тело. Проверка гоняет по РЕАЛЬНОМУ контенту то же правило, что показывает
    /// Doctor в Content Hub (<see cref="ContentValidationService.ValidateUnitVisual"/>), — владелец
    /// правила один, тест лишь приводит к нему весь ростер.
    /// <para>Почему тестом, а не только панелью: Doctor надо открыть, а до 03.08.2026 двадцать юнитов
    /// выходили на арену без визуала и узнавалось это эрроролгом в бою — то есть в самый дорогой момент.
    /// Панель показывает автору, тест не пускает мимо CI; одно без другого не работает.</para>
    /// </summary>
    public sealed class UnitVisualPresenceTests
    {
        private static List<UnitData> AllUnits() =>
            AssetDatabase.FindAssets($"t:{nameof(UnitData)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UnitData>)
                .Where(u => u != null)
                .OrderBy(u => u.name)
                .ToList();

        /// <summary>
        /// Падает ОДНИМ списком, а не на первой находке: раздача тел идёт пачкой, и чинить их по одному
        /// прогону — двадцать кругов вместо одного.
        /// </summary>
        [Test]
        public void EveryUnit_HasVisualAndViewPrefab()
        {
            var broken = new List<string>();
            foreach (UnitData unit in AllUnits())
            foreach (string issue in ContentValidationService.ValidateUnitVisual(unit))
                broken.Add($"{unit.name}: {issue}");

            Assert.IsEmpty(broken,
                $"Юнитов без тела: {broken.Count}.\n{string.Join("\n", broken)}\n" +
                "Временное тело даётся переиспользованием чужого пака (Visuals/*) и его ViewPrefab, " +
                "различитель — своя ступень BodyShade. Результат проверяется листом " +
                "Alebardium/Visuals/Export Unit Visual Catalog.");
        }
    }
}
