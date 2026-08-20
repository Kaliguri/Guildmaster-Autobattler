using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Главный инвариант аудита 06.08.2026: <b>симуляция не читает анимационный клип</b>.
    /// <para>
    /// До перевода тайминг удара выводился из разметки клипа — числа кадров и позиций маркеров, — и это
    /// доходило до <c>CombatPositioning.CanLandWindup</c>, то есть до решения «идти или бить». Правка
    /// маркера на два кадра двигала баланс всему ростеру, и узнать об этом было неоткуда: ни один тест
    /// не падал, ни одна строка не менялась.
    /// </para>
    /// <para>
    /// Инвариант живёт между тремя вещами (клип, данные, сим) и потому не может жить в комментарии:
    /// комментарий виден одной стороне шва, а нарушит его другая. Проверка текстовая, потому что
    /// возврат зависимости выглядит как безобидная строка вроде <c>visual.AttackHitFrame</c> — она
    /// компилируется, работает и молчит.
    /// </para>
    /// </summary>
    public sealed class SimulationReadsNoClipTests
    {
        private const string CombatRoot = "Assets/_Project/Scripts/Combat";

        /// <summary>Всё, чем симуляция могла бы дотянуться до разметки анимации.</summary>
        private static readonly string[] Forbidden =
        {
            "AnimationClip",        // сам движковый объект
            "ClipMarkers",          // читалка маркеров
            "AttackFrameCount",     // число кадров клипа
            "AttackHitFrame",       // кадр контакта
            "AttackHitPositions",   // позиции контактов серии
        };

        [Test]
        public void CombatAssembly_NeverTouchesAnimationClips()
        {
            Assert.That(Directory.Exists(CombatRoot), Is.True, $"Нет папки боевой сборки: {CombatRoot}");

            var complaints = (
                from file in Directory.GetFiles(CombatRoot, "*.cs", SearchOption.AllDirectories)
                let text = File.ReadAllText(file)
                let lines = text.Split('\n')
                from i in Enumerable.Range(0, lines.Length)
                let line = lines[i]
                // Комментарии не в счёт: там о снятой зависимости как раз и написано.
                where !line.TrimStart().StartsWith("//") && !line.TrimStart().StartsWith("///")
                from token in Forbidden
                where line.Contains(token)
                select $"{file.Replace('\\', '/')}:{i + 1} — {token}: {line.Trim()}"
            ).ToList();

            Assert.That(complaints, Is.Empty,
                "Симуляция снова читает разметку анимации. Тайминг удара ОБЪЯВЛЯЕТСЯ в " +
                "AnimationArchetypeData (WindupShare, ContactShares) и замеряется офлайн; клип — показ.\n  "
                + string.Join("\n  ", complaints));
        }
    }
}
