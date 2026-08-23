using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт МОЛЧАЛИВОГО ОТКАЗА USS: ни один лист темы не импортируется с жалобой парсера.
    /// </summary>
    /// <remarks>
    /// <b>Откуда.</b> Разбор 23.08.2026. Макс на кадрах: «вернуться находится не у левого края
    /// экрана, как мы обсуждали. Почему? … Найди единую причину и исправь, а то все еще вернуться
    /// как-то косит». Причина оказалась одна на все три экрана: в <c>.gm-button--back</c> стояло
    /// <c>left: calc(0px - var(--gm-space-3))</c>, а UI Toolkit не знает <c>calc()</c>. Декларация
    /// отбрасывалась, <c>position: absolute</c> оставался — и кнопка по горизонтали замирала там,
    /// куда её клал поток, то есть по центру. Работал ровно один экран, настройки, где место кнопке
    /// задавало собственное правило в обход общего.
    ///
    /// <para><b>Почему гейт, а не комментарий.</b> Про <c>calc()</c> у нас УЖЕ написано дважды —
    /// в <c>tokens.primitives.uss</c> и в <c>loadout.uss</c>, обоими руками, с пояснением «молча не
    /// применялось». Это не помешало ему появиться в третий раз: комментарий виден только тому, кто
    /// открыл тот файл. Инвариант живёт между файлами, значит его дом — тест.</para>
    ///
    /// <para><b>Он ловит класс, а не случай.</b> Unity жалуется при импорте на любую декларацию,
    /// которую не поняла: неизвестную функцию, неподдерживаемое значение (<c>align-items: baseline</c>
    /// — второй улов того же дня), опечатку в имени свойства. Все они ведут себя одинаково —
    /// правило применяется частично, экран выглядит «почти правильно», и никто не связывает вид с
    /// импортом. Проверка стоит доли секунды и закрывает весь класс разом.</para>
    ///
    /// <para><b>Что гейт НЕ ловит:</b> валидный USS, который просто не даёт нужного вида, и
    /// неизвестный ТОКЕН — <c>var(--нет-такого)</c> парсится синтаксически верно и жалобы не
    /// вызывает. Второе стережёт <c>UiColorPipelineTests</c> и токенные гейты.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UssImportGateTests
    {
        /// <summary>Где живёт наш набор. Вендорские листы под <c>Packages/</c> нас не касаются.</summary>
        private const string ThemeRoot = "Assets/_Project/UI";

        [Test]
        public void Ни_один_лист_темы_не_импортируется_с_жалобой_парсера()
        {
            var complaints = new List<string>();

            foreach (string path in SheetPaths())
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet == null)
                {
                    complaints.Add($"{path}: не грузится как StyleSheet");
                    continue;
                }

                if (Flag(sheet, "importedWithErrors"))
                    complaints.Add($"{path}: импортирован С ОШИБКАМИ");
                else if (Flag(sheet, "importedWithWarnings"))
                    complaints.Add($"{path}: импортирован с предупреждениями");
            }

            Assert.That(complaints, Is.Empty,
                "USS отказывает МОЛЧА: непонятая декларация выбрасывается, соседние применяются, и " +
                "экран выглядит почти правильно.\n" +
                "Точный текст жалобы Unity печатает при переимпорте файла — ткни в него в Project и " +
                "нажми Reimport, жалоба появится в консоли.\n" +
                "Частые причины: calc() (не поддерживается вообще), значение не из списка допустимых " +
                "(align-items: baseline), опечатка в имени свойства.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>Все листы набора, включая вложенные папки компонентов и экранов.</summary>
        private static IEnumerable<string> SheetPaths() =>
            AssetDatabase.FindAssets("t:StyleSheet", new[] { ThemeRoot })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Distinct()
                         .OrderBy(p => p);

        /// <summary>
        /// Флаги импорта у <see cref="StyleSheet"/> внутренние: публичного API «как прошёл импорт» нет.
        /// Отсутствие поля — не провал теста: это смена версии движка, а не поломка набора.
        /// </summary>
        private static bool Flag(StyleSheet sheet, string property)
        {
            PropertyInfo info = typeof(StyleSheet).GetProperty(
                property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return info != null && info.GetValue(sheet) is bool value && value;
        }
    }
}
