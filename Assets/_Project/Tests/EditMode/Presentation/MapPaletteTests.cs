using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Guildmaster.Presentation.Map;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Карта акта красится теми же примитивами палитры, что и интерфейс. Владелец палитры один —
    /// <c>tokens.primitives.uss</c> (там живёт HARD-правило «тёплый свет»), а <c>MapStyle</c> держит
    /// маленький производный набор и называет исходный токен в подсказке каждого поля.
    /// <para>Подсказка сама себя не проверяет, и три цвета уже уехали: подложка узла, его обод и светлый
    /// конец рампы иконок разошлись с токенами, которые в этих подсказках названы (аудит 2026-07-26, T-7).
    /// Тест делает подсказку исполняемой: разошлось — красное, а не «на глаз вроде похоже».</para>
    /// </summary>
    [TestFixture]
    public class MapPaletteTests
    {
        private const string TokensPath = "Assets/_Project/UI/Theme/tokens.primitives.uss";
        private const string StylePath  = "Assets/_Project/ScriptableObjects/Configs/MapStyle.asset";

        /// <summary>Цвет карты → токен палитры, названный в его подсказке.</summary>
        private static readonly (string Field, string Token)[] Bindings =
        {
            ("NodeBacking",   "--gm-ink-600"),
            ("NodeRim",       "--gm-brass-500"),
            ("CurrentMarker", "--gm-brass-200"),
            ("IconShadow",    "--gm-ink-700"),
            ("IconLight",     "--gm-parchment-100"),
            ("PathTravelled", "--gm-brass-500"),
            ("PathAvailable", "--gm-brass-200"),
        };

        [Test]
        public void Цвета_карты_совпадают_с_токенами_палитры()
        {
            Dictionary<string, Color32> tokens = ReadTokens();
            var style = AssetDatabase.LoadAssetAtPath<MapStyle>(StylePath);
            Assert.IsNotNull(style, $"нет ассета стиля карты: {StylePath}");

            foreach ((string field, string token) in Bindings)
            {
                Assert.IsTrue(tokens.ContainsKey(token),
                    $"в палитре нет токена {token} — подсказка поля {field} указывает в никуда");

                Color32 want = tokens[token];
                Color32 got  = ColorOf(style, field);

                // Через байты, а не float: в ассете цвет лежит нормализованным, и точное равенство
                // долей ловило бы шум последнего знака вместо настоящего расхождения.
                Assert.AreEqual((want.r, want.g, want.b), (got.r, got.g, got.b),
                    $"MapStyle.{field} разошёлся с {token}: подсказка обещает rgb({want.r},{want.g},{want.b}), " +
                    $"а в ассете rgb({got.r},{got.g},{got.b}). Палитра — единственный владелец цвета.");
            }
        }

        private static Color32 ColorOf(MapStyle style, string property)
        {
            var prop = typeof(MapStyle).GetProperty(property);
            Assert.IsNotNull(prop, $"в MapStyle нет свойства {property} — обнови список Bindings");
            return (Color)prop.GetValue(style);
        }

        private static Dictionary<string, Color32> ReadTokens()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), TokensPath);
            Assert.IsTrue(File.Exists(path), $"нет файла палитры: {TokensPath}");

            var result = new Regex(@"(--gm-[a-z0-9-]+):\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)")
                .Matches(File.ReadAllText(path));

            var tokens = new Dictionary<string, Color32>();
            foreach (Match m in result)
            {
                byte P(int i) => byte.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                tokens[m.Groups[1].Value] = new Color32(P(2), P(3), P(4), 255);
            }

            Assert.Greater(tokens.Count, 0, "палитра прочиталась пустой — изменился формат tokens.primitives.uss");
            return tokens;
        }
    }
}
