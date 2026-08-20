using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Гейт закона видимости для стаков: компонент эффекта решает исход по СНИМКУ на начало тика
    /// (<c>ctx.Stacks</c>), а не по живому числу (<c>ctx.Effect.Stacks</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Зачем гейт, а не комментарий.</b> Обе половины правила видны только вместе: снимок живёт
    /// в <c>EffectContext.Stacks</c>, а нарушают его в чужом файле — в компоненте, где <c>ctx.Effect</c>
    /// лежит под рукой и выглядит тем же самым числом. Прецедентов уже два: «Угли» развели зеркало на
    /// тике 300, а «Изморозь» несла ту же дыру в обоих порогах до 2026-08-07 и не была покрыта ничем.</para>
    /// <para><b>Что гейт ловит и чего не ловит.</b> Ловит прямую форму <c>ctx.Effect.Stacks</c> — ровно ту,
    /// которой оба раза и ошибались. Обращение через локальную (<c>var eff = ctx.Effect; eff.Stacks</c>)
    /// он не видит и видеть не должен: так пишут управление сходом стаков (<c>RemoveStacks</c> и проверка
    /// остатка сразу после неё), где живое число — единственно верное. Полного статического анализа у нас
    /// нет; гейт закрывает наивный путь, которым в этот класс ошибок и попадают.</para>
    /// </remarks>
    public sealed class EffectStacksVisibilityGateTests
    {
        /// <summary>
        /// Файлы, которым чтение живого числа разрешено, с причиной. Пустой список — правило без
        /// исключений; запись сюда обязана нести обоснование в комментарии рядом.
        /// </summary>
        private static readonly HashSet<string> Allowed = new HashSet<string>();

        [Test]
        public void EffectComponents_JudgeByTickStartSnapshot_NotLiveStacks()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts", "Combat", "Effects", "Components");
            Assert.IsTrue(Directory.Exists(root), $"Папка компонентов эффектов не найдена: {root}");

            var live = new Regex(@"\.Effect\.Stacks\b");
            var offenders = new List<string>();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (Allowed.Contains(name)) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (live.IsMatch(lines[i])) offenders.Add($"{name}:{i + 1} — {lines[i].Trim()}");
            }

            Assert.IsEmpty(offenders,
                "Компонент решает по ЖИВОМУ числу стаков вместо снимка начала тика — исход станет зависеть " +
                "от порядка юнитов в обходе, и зеркало разойдётся. Читать ctx.Stacks:\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
