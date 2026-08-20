using Guildmaster.Presentation;
using Guildmaster.Presentation.Design;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Шов между feel-конфигом и ригом: доли роста, по которым СЧИТАЕТСЯ вес зоны, обязаны совпадать с
    /// якорями, по которым зона БЬЁТСЯ.
    /// </summary>
    /// <remarks>
    /// Инвариант кросс-файловый, поэтому живёт тестом, а не комментарием: конфиг и префаб правятся
    /// порознь и разными людьми, и ни одна из сторон не узнает, что вторая уехала. Разъедутся — зона
    /// начнёт взвешиваться не там, где её в итоге бьют: вес посчитается по груди, а вспышка придёт в
    /// живот, и объяснить это по коду будет нечем.
    /// <para>
    /// Порог в 5% роста выбран как «заметно глазом»: у бойца ростом 1.7 это 8.5 см, то есть половина
    /// головы. Меньшие расхождения неизбежны — конфиг один на всех, а якоря у каждого рига свои.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class ImpactZoneRigTests
    {
        private const string ViewPath = "Assets/_Project/Prefabs/Units/UnitView_BoneStorybook.prefab";
        private const string ConfigPath = "Assets/_Project/ScriptableObjects/Configs/CombatFeelConfig.asset";
        private const float Tolerance = 0.05f;

        [Test]
        public void ZoneHeightsInConfig_MatchAnchorsInRig()
        {
            var view = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath)?.GetComponent<UnitView>();
            var config = AssetDatabase.LoadAssetAtPath<CombatFeelConfig>(ConfigPath);
            Assert.That(view, Is.Not.Null, $"не найден вид {ViewPath}");
            Assert.That(config, Is.Not.Null, $"не найден конфиг {ConfigPath}");

            if (!view.HasAimAnchors)
                Assert.Ignore("якоря зон не расставлены — сверять нечего, работает переходный расчёт по габариту");

            float height = view.FigureHeight;
            float feetY = view.FeetPoint.y;

            Check("голова", view.AimHeadPoint.y, config.ImpactZoneHeadHeight, feetY, height);
            Check("корпус", view.AimBodyPoint.y, config.ImpactZoneBodyHeight, feetY, height);
            Check("ноги",   view.AimLegsPoint.y, config.ImpactZoneLegsHeight, feetY, height);
        }

        private static void Check(string zone, float anchorY, float declared, float feetY, float height)
        {
            float actual = (anchorY - feetY) / height;
            Assert.That(actual, Is.EqualTo(declared).Within(Tolerance),
                $"зона «{zone}»: якорь в риге стоит на {actual:F3} роста, а конфиг считает вес по " +
                $"{declared:F3} — разошлись на {Mathf.Abs(actual - declared) * height * 100f:F1} см. " +
                "Либо подвинь якорь, либо поправь долю в CombatFeelConfig: вес зоны обязан считаться " +
                "там же, где зону бьют.");
        }
    }
}
