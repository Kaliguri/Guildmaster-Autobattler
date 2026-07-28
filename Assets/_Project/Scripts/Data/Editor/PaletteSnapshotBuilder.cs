using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Сборщик снимка палитры: читает <c>tokens.primitives.uss</c> и <c>tokens.semantic.uss</c>,
    /// разворачивает ссылки <c>var(--gm-…)</c> до конкретных цветов и складывает результат в
    /// <see cref="GuildmasterPalette"/> — чтобы мир (карта, VFX, перекрасчик) читал ту же палитру,
    /// что и интерфейс, а не свою копию рядом.
    /// <para>Направление намеренно одностороннее: USS — источник, ассет — снимок. Обратная сборка
    /// («поправить ассет, обновить USS») означала бы двух владельцев цвета, ради устранения которых
    /// всё и затевалось.</para>
    /// </summary>
    public static class PaletteSnapshotBuilder
    {
        public const string PrimitivesPath = "Assets/_Project/UI/Theme/tokens.primitives.uss";
        public const string SemanticPath   = "Assets/_Project/UI/Theme/tokens.semantic.uss";
        public const string AssetPath      = "Assets/_Project/ScriptableObjects/Configs/GuildmasterPalette.asset";

        private static readonly Regex Declaration = new Regex(@"(--gm-[a-z0-9-]+)\s*:\s*([^;]+);");
        private static readonly Regex Rgb         = new Regex(@"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)");
        private static readonly Regex VarRef      = new Regex(@"var\(\s*(--gm-[a-z0-9-]+)\s*\)");

        [MenuItem("Alebardium/Дизайн-система/Пересобрать палитру", priority = 210)]
        public static void Rebuild()
        {
            GuildmasterPalette.Entry[] entries = Collect(out string problem);
            if (entries == null)
            {
                Debug.LogError($"[PaletteSnapshotBuilder] - {problem}");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<GuildmasterPalette>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.SetEntries(entries);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log($"[PaletteSnapshotBuilder] - палитра пересобрана: {entries.Length} токенов → {AssetPath}");
        }

        /// <summary>
        /// Прочитать оба яруса токенов и развернуть ссылки. Возвращает null и текст в
        /// <paramref name="problem"/>, если файл не найден или ссылка ведёт в никуда — тест зовёт
        /// этот же путь, поэтому молчать здесь нельзя.
        /// </summary>
        public static GuildmasterPalette.Entry[] Collect(out string problem)
        {
            problem = null;

            var raw = new Dictionary<string, string>();
            foreach (string path in new[] { PrimitivesPath, SemanticPath })
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(), path);
                if (!File.Exists(full))
                {
                    problem = $"нет файла токенов: {path}";
                    return null;
                }

                foreach (Match m in Declaration.Matches(File.ReadAllText(full)))
                    raw[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }

            var result = new List<GuildmasterPalette.Entry>(raw.Count);
            foreach (KeyValuePair<string, string> kv in raw)
            {
                if (!TryResolve(kv.Key, raw, out Color color, out string why)) continue;   // не цвет (размер, шрифт) — пропускаем
                if (why != null)
                {
                    problem = why;
                    return null;
                }
                result.Add(new GuildmasterPalette.Entry { Token = kv.Key, Color = color });
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Token, b.Token));   // стабильный порядок: иначе ассет «меняется» на ровном месте
            return result.ToArray();
        }

        /// <summary>
        /// Развернуть значение токена до цвета. Цепочка <c>var()</c> проходится с потолком глубины:
        /// циклическая ссылка в USS — опечатка, а не повод повесить редактор.
        /// </summary>
        private static bool TryResolve(string token, Dictionary<string, string> raw, out Color color, out string problem)
        {
            problem = null;
            color   = default;

            string value = raw[token];
            for (int depth = 0; depth < 8; depth++)
            {
                Match rgb = Rgb.Match(value);
                if (rgb.Success)
                {
                    byte P(int i) => byte.Parse(rgb.Groups[i].Value, CultureInfo.InvariantCulture);
                    float a = rgb.Groups[4].Success
                        ? float.Parse(rgb.Groups[4].Value, CultureInfo.InvariantCulture)
                        : 1f;
                    color = new Color32(P(1), P(2), P(3), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                    return true;
                }

                Match reference = VarRef.Match(value);
                if (!reference.Success) return false;   // не цветовой токен (отступ, размер шрифта)

                if (!raw.TryGetValue(reference.Groups[1].Value, out value))
                {
                    problem = $"токен {token} ссылается на {reference.Groups[1].Value}, которого нет в палитре";
                    return true;
                }
            }

            problem = $"токен {token}: ссылки var() закольцевались";
            return true;
        }
    }
}
