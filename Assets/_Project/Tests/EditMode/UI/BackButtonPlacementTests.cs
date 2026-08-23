using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт МЕСТА: «Вернуться» заводится только через <c>BackButton.PlaceOn</c>, и потому всегда
    /// стоит у кромки кадра.
    /// </summary>
    /// <remarks>
    /// <b>Откуда.</b> Разбор Макса 23.08.2026: «вернуться находится не у левого края экрана, как мы
    /// обсуждали. Почему? Особенно это видно в Создать игру… и в Выбор гильдии. В настройках тоже не
    /// корректно. Найди единую причину и исправь».
    ///
    /// <para><b>Единая причина.</b> Место кнопке задаёт <c>gm-button--back</c> — <c>position:
    /// absolute</c> у левого нижнего края. В UI Toolkit всякий элемент по умолчанию <c>relative</c>,
    /// поэтому абсолют считается от НЕПОСРЕДСТВЕННОГО родителя, а не от кадра. Кнопку клали в ряд
    /// действий или в колонку панели — и «кромка кадра» оказывалась кромкой того ящика: в выборе
    /// режима кнопка встала посреди экрана поверх средней карточки, в выборе гильдии и в профиле —
    /// внутри панели, у настроек — над рядом действий.</para>
    ///
    /// <para><b>Почему гейт, а не договор.</b> Правильное место нельзя выразить разметкой: UXML не
    /// ограничивает, куда положить элемент. Оно выражается единственным способом завести кнопку —
    /// методом, который кладёт её в корень экрана. Гейт стережёт, что второго способа не завелось.</para>
    /// </remarks>
    [TestFixture]
    public sealed class BackButtonPlacementTests
    {
        private const string UiDir = "Assets/_Project/UI";
        private const string ScriptsDir = "Assets/_Project/Scripts";
        private const string OwnerFile = "BackButton.cs";

        [Test]
        public void Возврат_не_объявляется_в_разметке()
        {
            var found = new StringBuilder();

            foreach (string path in Directory.GetFiles(UiDir, "*.uxml", SearchOption.AllDirectories).OrderBy(p => p))
            {
                string text = File.ReadAllText(path);
                if (!text.Contains("BackButton")) continue;
                found.Append("\n  ").Append(path.Replace('\\', '/'));
            }

            Assert.That(found.ToString(), Is.Empty,
                "«Вернуться» стоит в разметке — значит её место определяет тот ящик, в который её " +
                "положили, а не кромка кадра. Убери элемент из UXML и позови BackButton.PlaceOn(root, " +
                "onBack, localize) в сборке вида." + found);
        }

        [Test]
        public void Кнопку_возврата_заводит_только_её_собственный_метод()
        {
            var complaints = new List<string>();

            foreach (string path in Directory.GetFiles(ScriptsDir, "*.cs", SearchOption.AllDirectories).OrderBy(p => p))
            {
                if (Path.GetFileName(path) == OwnerFile) continue;

                string text = File.ReadAllText(path);
                string file = path.Replace('\\', '/');

                if (text.Contains("new BackButton(") || text.Contains("new Components.BackButton("))
                    complaints.Add($"\n  {file} — создаёт кнопку сам: место возьмёт от своего родителя");

                if (text.Contains("Q<BackButton>") || text.Contains("Q<Components.BackButton>"))
                    complaints.Add($"\n  {file} — ищет кнопку в разметке: в разметке её больше нет");
            }

            Assert.That(string.Concat(complaints), Is.Empty,
                "Возврат заводится в обход BackButton.PlaceOn:" + string.Concat(complaints));
        }
    }
}
