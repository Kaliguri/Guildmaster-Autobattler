using System;
using Guildmaster.UI;
using Guildmaster.UI.Components;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Лента режимов забега. Тема возвращалась дважды и с разных сторон: сперва «Тактика» и «Компендиум»
    /// висели рабочими на вид чипами с пустым обработчиком (нажатие проглатывалось молча), потом я убрала
    /// их совсем — и вместе с ними пропало место заявленных режимов в ряду. Верная середина — чип стоит,
    /// но погашен: игрок видит, что режим будет, и не обманывается насчёт «уже работает».
    /// <para>Гашение — классом, не <c>SetEnabled(false)</c>: HARD-правило ui-feedback требует, чтобы
    /// недоступное оставалось живым на наведение и нажатие.</para>
    /// </summary>
    [TestFixture]
    public class RunModeBarTests
    {
        private const string Uxml       = "Assets/_Project/UI/Screens/RunModeBar.uxml";
        private const string MutedClass = "gm-chip--muted";

        private static RunModeBarView Build(out VisualTreeAsset asset)
        {
            asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Uxml);
            Assert.IsNotNull(asset, $"нет разметки ленты режимов: {Uxml}");
            return new RunModeBarView(
                asset, key => null,
                onMap: () => { }, onBattle: () => { }, onInventory: () => { },
                onMenu: () => { }, onStart: () => { });
        }

        private static Chip ChipOf(RunModeBarView bar, string name)
        {
            var chip = bar.Root.Q<Chip>("mode-" + name);
            Assert.IsNotNull(chip, $"в ленте нет чипа режима '{name}'");
            return chip;
        }

        [Test]
        public void Все_пять_режимов_стоят_в_ленте()
        {
            RunModeBarView bar = Build(out _);

            foreach (string mode in new[] { "map", "battle", "inventory", "tactics", "compendium" })
                ChipOf(bar, mode);
        }

        [Test]
        public void Режимы_без_экрана_погашены_но_не_выключены()
        {
            RunModeBarView bar = Build(out _);

            foreach (string mode in new[] { "tactics", "compendium" })
            {
                Chip chip = ChipOf(bar, mode);
                Assert.IsTrue(chip.ClassListContains(MutedClass),
                    $"'{mode}': режима ещё нет — чип обязан читаться погашенным");
                Assert.IsTrue(chip.enabledSelf,
                    $"'{mode}': гасим видом, а не выключением — правило ui-feedback");
            }
        }

        [Test]
        public void Рабочие_режимы_не_погашены()
        {
            RunModeBarView bar = Build(out _);

            foreach (string mode in new[] { "map", "battle", "inventory" })
                Assert.IsFalse(ChipOf(bar, mode).ClassListContains(MutedClass),
                    $"'{mode}': рабочий режим не должен выглядеть недоступным");
        }

        [Test]
        public void Подсветка_не_садится_на_режим_без_экрана()
        {
            // SetActiveMode обходит только те чипы, за которыми есть экран: подсвечивать «Тактику»
            // нечем, и попытка не должна ни падать, ни зажигать погашенный таб.
            RunModeBarView bar = Build(out _);

            bar.SetActiveMode("tactics");

            Assert.IsFalse(ChipOf(bar, "tactics").ClassListContains("gm-chip--active"),
                "погашенный режим не становится активным");
        }
    }
}
