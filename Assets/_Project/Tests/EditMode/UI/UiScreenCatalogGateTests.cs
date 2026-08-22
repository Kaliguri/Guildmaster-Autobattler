using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт КАДРА: у каждого экрана игры есть запись в каталоге превью, а значит и снимок.
    /// </summary>
    /// <remarks>
    /// <b>Откуда.</b> Разбор 23.08.2026: три самых частых класса претензий по интерфейсу — реф не
    /// отработан, метрика внутри элемента не сходится, кусок экрана молча пропал — не ловятся ни
    /// одним из десяти статических гейтов и видны только на кадре целого экрана. Кадр снимается по
    /// записям <c>UiPreviewCatalog</c>: экрана нет в каталоге — нет и снимка, и его регрессию снова
    /// первым увидит Макс.
    ///
    /// <para><b>Почему разбор текстом, а не рефлексией.</b> Каталог живёт в
    /// <c>Guildmaster.DevTools</c> под <c>UNITY_EDITOR</c> и тестовой сборке не виден. Тот же приём,
    /// что у гейтов размера и типографики: правило проверяется по исходнику.</para>
    ///
    /// <para><b>Исключения поимённо и с причиной</b> — молчаливое послабление означало бы, что через
    /// месяц никто не вспомнит, почему экран без кадра считается нормой.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UiScreenCatalogGateTests
    {
        private const string ScreensDir = "Assets/_Project/UI/Screens";
        private const string CatalogPath = "Assets/_Project/Scripts/DevTools/UiPreviewCatalog.cs";

        /// <summary>
        /// Экраны, которым кадр не положен, и почему.
        /// </summary>
        private static readonly Dictionary<string, string> Exempt = new()
        {
            ["DevBattleBrowser.uxml"] = "дев-инструмент: живёт на полке F2, игроку не показывается",
            ["DevLogScreen.uxml"]     = "дев-инструмент: полка логов, игроку не показывается",
            ["ContinueScreen.uxml"]   = "долг: собирается инлайном внутри метода MenuRouter, вынести и снять кадр",
        };

        [Test]
        public void Каждый_экран_игры_есть_в_каталоге_превью()
        {
            string[] screens = Directory.GetFiles(ScreensDir, "*.uxml", SearchOption.TopDirectoryOnly);
            Assert.That(screens, Is.Not.Empty, $"В {ScreensDir} не нашлось ни одного .uxml — проверь путь.");

            string catalog = File.ReadAllText(CatalogPath);
            var missing = new StringBuilder();

            foreach (string path in screens.OrderBy(p => p))
            {
                string file = Path.GetFileName(path);
                if (Exempt.ContainsKey(file)) continue;
                if (catalog.Contains(file)) continue;

                missing.Append("\n  ").Append(file);
            }

            Assert.That(missing.ToString(), Is.Empty,
                "Эти экраны нельзя снять кадром: их нет в UiPreviewCatalog. Заведи запись «id → сборка со " +
                "стендовыми данными» — и экран попадёт в прогон Alebardium → UI → Screen Sheet. Если кадр " +
                "экрану не положен, впиши его в Exempt С ПРИЧИНОЙ." + missing);
        }

        /// <summary>
        /// Исключение, переставшее быть нужным, — это ложь в списке причин.
        /// </summary>
        [Test]
        public void Список_исключений_не_держит_лишнего()
        {
            var stale = new StringBuilder();

            foreach (KeyValuePair<string, string> pair in Exempt)
            {
                string path = Path.Combine(ScreensDir, pair.Key);
                if (!File.Exists(path)) stale.Append("\n  ").Append(pair.Key).Append(" — такого экрана больше нет");
            }

            string catalog = File.ReadAllText(CatalogPath);
            foreach (KeyValuePair<string, string> pair in Exempt)
            {
                if (catalog.Contains(pair.Key))
                    stale.Append("\n  ").Append(pair.Key).Append(" — уже есть в каталоге, исключение лишнее");
            }

            Assert.That(stale.ToString(), Is.Empty,
                "Список исключений разошёлся с деревом. Убери строки ниже:" + stale);
        }
    }
}
