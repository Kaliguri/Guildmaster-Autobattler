using System.Collections.Generic;
using System.Linq;
using Guildmaster.UI.Components;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт самого перечня: он обязан быть непротиворечивым ДО того, как по нему судят USS.
    /// </summary>
    /// <remarks>
    /// Реестр — источник для трёх потребителей сразу (гейт состояний, контактный лист, звук
    /// интерфейса), поэтому опечатка в нём тихо разъезжается по всем троим: дубль блока даёт две
    /// записи в витрине, точка в имени класса — вечно красный гейт, а интерактивная запись без
    /// требуемых состояний превращает гейт в декорацию.
    /// </remarks>
    [TestFixture]
    public sealed class UiComponentRegistryTests
    {
        [Test]
        public void Перечень_не_пустой()
        {
            Assert.That(UiComponentRegistry.All, Is.Not.Empty,
                "Пустой реестр делает гейт состояний зелёным при любом состоянии дерева.");
        }

        [Test]
        public void Блок_объявлен_один_раз()
        {
            IEnumerable<string> duplicates = UiComponentRegistry.All
                .GroupBy(e => e.Block)
                .Where(g => g.Count() > 1)
                .Select(g => $"  {g.Key} — {g.Count()} записи");

            Assert.IsEmpty(duplicates.ToList(),
                "Один класс — одна запись: иначе витрина покажет элемент дважды, а гейт проверит его дважды.\n" +
                string.Join("\n", duplicates));
        }

        [Test]
        public void Класс_записан_без_точки_и_псевдокласса()
        {
            var complaints = new List<string>();

            foreach (UiComponentEntry entry in UiComponentRegistry.All)
            {
                foreach (string cls in new[] { entry.Block }.Concat(entry.Variants))
                {
                    if (cls.StartsWith("."))  complaints.Add($"  {entry.Label}: «{cls}» — с точкой, нужен голый класс");
                    if (cls.Contains(":"))    complaints.Add($"  {entry.Label}: «{cls}» — с псевдоклассом, состояния живут в Required");
                    if (!cls.StartsWith("gm-")) complaints.Add($"  {entry.Label}: «{cls}» — не наш префикс");
                }
            }

            Assert.IsEmpty(complaints,
                "Класс в реестре пишется так, как его вешает код: голым именем с префиксом gm-.\n" +
                string.Join("\n", complaints));
        }

        [Test]
        public void Вариант_принадлежит_своему_блоку()
        {
            var complaints = new List<string>();

            foreach (UiComponentEntry entry in UiComponentRegistry.All)
            {
                foreach (string variant in entry.Variants)
                {
                    if (!variant.StartsWith(entry.Block + "--"))
                        complaints.Add($"  {entry.Label}: «{variant}» не является модификатором «{entry.Block}»");
                }
            }

            Assert.IsEmpty(complaints,
                "Модификатор BEM пишется от своего блока: block--modifier. Чужой вариант в записи означает,\n" +
                "что элемент в витрине будет собран не из того, что живёт в игре.\n" +
                string.Join("\n", complaints));
        }

        [Test]
        public void Интерактивный_требует_наведения_и_нажатия()
        {
            IEnumerable<string> complaints = UiComponentRegistry.All
                .Where(e => e.IsInteractive)
                .Where(e => !e.Required.HasFlag(UiElementState.Hover) || !e.Required.HasFlag(UiElementState.Active))
                .Select(e => $"  {e.Label} ({e.Block}) — {e.Required}");

            Assert.IsEmpty(complaints.ToList(),
                "Элемент, принимающий указатель, обязан отвечать хотя бы на наведение и нажатие.\n" +
                "Требование ниже этого превращает гейт в отчёт о том, что мы ничего не требуем.\n" +
                string.Join("\n", complaints));
        }

        [Test]
        public void Список_интерактивных_совпадает_с_перечнем()
        {
            List<string> expected = UiComponentRegistry.All
                .Where(e => e.IsInteractive)
                .Select(e => e.Block)
                .ToList();

            CollectionAssert.AreEquivalent(expected, UiComponentRegistry.InteractiveBlocks,
                "InteractiveBlocks кэшируется при загрузке типа и читается звуком интерфейса на каждое " +
                "движение указателя. Разъехавшись с перечнем, он делает элементы молчащими — ровно тот " +
                "дефект, ради которого реестр и заводился.");
        }
    }
}
