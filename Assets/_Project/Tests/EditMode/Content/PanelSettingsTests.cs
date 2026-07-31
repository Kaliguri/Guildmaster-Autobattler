using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Инварианты ассетов <see cref="PanelSettings"/>.
    /// <para>Панель UITK принадлежит АССЕТУ, а не сцене: как только ассет попал в память (открыли стенд,
    /// кликнули по нему в Project, прошлись по нему скриптом), его рантайм-панель регистрируется в общем
    /// списке и участвует в отрисовке — в любой сцене, включая игровую. Поэтому «служебная» настройка
    /// dev-панели способна испортить кадр игры, и живёт этот инвариант не в комментарии у ассета, который
    /// со стороны игры никто не читает, а здесь.</para>
    /// </summary>
    public sealed class PanelSettingsTests
    {
        /// <summary>
        /// Прецедент 2026-08-01: у <c>PreviewPanelSettings</c> (стенд UI) стоял <c>clearColor</c> с серым
        /// цветом. Пустая панель стенда очищала весь буфер и стирала кадр целиком — мир, интерфейс, всё;
        /// экран становился ровно серым. «Иногда» — потому что порядок двух панелей с одинаковым
        /// приоритетом зависит от порядка регистрации: разбудили стенд раньше игровой панели — кадр цел,
        /// позже — стёрт. Фон стенду даёт камера его собственной сцены, панели чистить нечего.
        /// </summary>
        [Test]
        public void ProjectPanelSettings_DoNotClearTheFrame()
        {
            string[] paths = AssetDatabase.FindAssets("t:PanelSettings", new[] { "Assets/_Project" })
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .ToArray();

            Assert.That(paths, Is.Not.Empty, "в проекте не нашлось ни одного PanelSettings — тест смотрит не туда");

            foreach (string path in paths)
            {
                var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                Assert.That(settings.clearColor, Is.False,
                    $"{path}: clearColor стирает кадр во ВСЕХ сценах, где ассет оказался загружен, " +
                    "а не только в своей. Непрозрачный фон даёт камера сцены.");
            }
        }
    }
}
