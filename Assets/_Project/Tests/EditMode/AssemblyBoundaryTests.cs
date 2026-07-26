using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Границы сборок, которые держат правила проекта механически, а не на дисциплине.
    ///
    /// Первая — локализация. HARD-правило «весь текст через ключи» до сих пор охранялось только
    /// вниманием: <c>Guildmaster.UI</c> ссылалась на <c>Unity.Localization</c>, хотя ни один экран
    /// её не вызывал, — то есть дверь к <c>LocalizationSettings.StringDatabase</c> мимо
    /// <c>ILocalizationService</c> стояла открытой. Ссылку сняли (аудит UA-20): теперь обход
    /// правила — не оплошность на ревью, а ошибка компиляции. Тест держит дверь закрытой.
    ///
    /// Вторая — симуляция не знает о картинке. <c>Guildmaster.Combat</c> ссылается только на
    /// Core и Data; появление там Presentation или UI означало бы, что детерминированный бой
    /// начал зависеть от кадра.
    /// </summary>
    [TestFixture]
    public class AssemblyBoundaryTests
    {
        private static string ReadAsmdef(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"не нашла asmdef по пути {relativePath}");
            return File.ReadAllText(path);
        }

        [Test]
        public void UI_не_видит_Unity_Localization_напрямую()
        {
            string ui = ReadAsmdef("_Project/Scripts/UI/Guildmaster.UI.asmdef");

            Assert.IsFalse(ui.Contains("Unity.Localization"),
                "Guildmaster.UI не должна ссылаться на Unity.Localization: текст берётся только через " +
                "ILocalizationService из Core. Ссылка вернёт возможность звать StringDatabase из экрана.");
        }

        [Test]
        public void Симуляция_не_видит_презентацию()
        {
            string combat = ReadAsmdef("_Project/Scripts/Combat/Guildmaster.Combat.asmdef");

            Assert.IsFalse(combat.Contains("Guildmaster.Presentation"),
                "боевая симуляция обязана быть слепа к отрисовке");
            Assert.IsFalse(combat.Contains("Guildmaster.UI"),
                "боевая симуляция обязана быть слепа к UI");
        }
    }
}
