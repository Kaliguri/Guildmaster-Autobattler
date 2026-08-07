using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Сцепка «код → таблица UI»: каждый ключ <c>ui.*</c>, который код зовёт литералом, объявлен в
    /// таблице и имеет русский текст.
    /// </summary>
    /// <remarks>
    /// <para><b>Зачем.</b> Для звука такой страж есть с самого начала
    /// (<c>AudioCoverageTests.EveryKeyCalledFromCode_ResolvesToAnEvent</c>) и работает; для текста
    /// аналога не завели, и правило «ключи закладываются сразу, EN + RU» держалось только на
    /// дисциплине. К 07.08.2026 три десятка ключей, которыми пользуется живой код, не были заведены
    /// нигде: весь экран Настроек, весь экран Профиля и — хуже всего — единицы измерения, которые
    /// текут в КАЖДОЕ показанное число стата. Экраны при этом выглядят исправными, потому что
    /// вызывающий подставляет русский литерал вторым аргументом.</para>
    /// <para><b>Почему таблицы читаются с диска, а не через API локализации.</b> Гейт проверяет то,
    /// что лежит в репозитории и уедет в билд. Заодно он не тащит в тестовую сборку ссылку на пакет
    /// ради двух полей — и работает без открытого редактора, которым таблицы правятся.</para>
    /// <para><b>Почему со списком известных дыр.</b> Это храповик: он фиксирует сегодняшнее
    /// положение и не даёт ему ухудшаться. Завести все ключи разом нельзя — таблицы правятся только
    /// редакторным API, правка YAML их рвёт. Список обязан дойти до нуля, и лишняя запись в нём
    /// краснит <see cref="KnownGaps_ContainNothingAlreadyDeclared"/>, так что забыть вычеркнуть
    /// заведённый ключ не выйдет.</para>
    /// </remarks>
    public sealed class UiLocalizationCoverageTests
    {
        /// <summary>
        /// Ключи, которые код зовёт, но в таблице их нет. Снимок 07.08.2026, замер этим же гейтом:
        /// пятьдесят три. Заведённый ключ ОБЯЗАН быть убран отсюда — список должен дойти до нуля.
        /// </summary>
        /// <remarks>
        /// Порядок заведения по цене промаха: <c>ui.unit.*</c> течёт в каждое показанное число стата,
        /// то есть это не экран, а весь слой описаний; дальше Настройки и Профиль — целые экраны без
        /// единого ключа; остальное точечно.
        /// </remarks>
        private static readonly HashSet<string> KnownGaps = new HashSet<string>
        {
            // Единицы измерения — текут во ВСЕ описания статов, а не в один экран.
            "ui.unit.percent", "ui.unit.seconds", "ui.unit.per_second",

            // Экран Настроек целиком.
            "ui.settings.volume_master", "ui.settings.volume_music", "ui.settings.volume_sfx",
            "ui.settings.window_mode", "ui.settings.window_mode.windowed",
            "ui.settings.window_mode.borderless", "ui.settings.window_mode.exclusive",
            "ui.settings.resolution", "ui.settings.refresh_rate", "ui.settings.refresh_rate.locked",

            // Экран Профиля целиком.
            "ui.profile.title", "ui.profile.identity", "ui.profile.name.steam",
            "ui.profile.name.hint.steam", "ui.profile.name.hint.own", "ui.profile.color",
            "ui.profile.cursor", "ui.profile.slots", "ui.profile.create", "ui.profile.save",
            "ui.profile.back", "ui.profile.delete", "ui.profile.delete.title",

            // Экран новой игры.
            "ui.newgame.title", "ui.newgame.mode.campaign", "ui.newgame.mode.grounds",
            "ui.newgame.mode.pvp", "ui.newgame.hint.campaign", "ui.newgame.hint.grounds",
            "ui.newgame.hint.pvp", "ui.newgame.lobby", "ui.newgame.lobby.no_steam", "ui.newgame.back",

            // Выбор гильдии.
            "ui.guilds.title", "ui.guilds.caption", "ui.guilds.new", "ui.guilds.in_run", "ui.guilds.back",

            // Двор (экран-заглушка) и мелочь по экранам.
            "ui.hub.title", "ui.hub.stub", "ui.hub.start_run",
            "ui.boot.hint", "ui.boot.loading", "ui.confirm.cancel", "ui.outcome.continue",
            "ui.menu.invite",

            // Диалог потери соединения — зона коопа, который сейчас в работе.
            "ui.coop.lost.continue", "ui.coop.lost.invite", "ui.coop.lost.join", "ui.coop.lost.to_menu",
        };

        private static string TablesDir =>
            Path.Combine(Application.dataPath, "_Project", "Localization", "Tables");

        [Test]
        public void EveryUiKeyCalledFromCode_IsDeclaredInTheTable()
        {
            IReadOnlyDictionary<long, string> declared = DeclaredKeys();
            var known = new HashSet<string>(declared.Values);

            var missing = new SortedDictionary<string, string>();
            foreach ((string key, string file) in CalledUiKeys())
            {
                if (KnownGaps.Contains(key)) continue;
                if (!known.Contains(key)) missing[key] = file;
            }

            Assert.IsEmpty(missing,
                "Код зовёт ключи UI, которых нет в таблице. В игре это молчаливый фолбэк на русский " +
                "литерал: экран выглядит рабочим и на английском остаётся русским.\n  "
                + string.Join("\n  ", missing.Select(p => $"{p.Key}  ({p.Value})")));
        }

        [Test]
        public void EveryDeclaredUiKey_HasRussianText()
        {
            IReadOnlyDictionary<long, string> declared = DeclaredKeys();
            IReadOnlyDictionary<long, string> ru = LocalizedValues("UI_ru.asset");

            var empty = declared
                .Where(p => !KnownGaps.Contains(p.Value))
                .Where(p => !ru.TryGetValue(p.Key, out string v) || string.IsNullOrWhiteSpace(v))
                .Select(p => p.Value)
                .OrderBy(k => k)
                .ToList();

            Assert.IsEmpty(empty,
                "Ключ объявлен, но русского текста у него нет. Объявленный пустой ключ хуже " +
                "незаведённого: валидатор считает его найденным.\n  " + string.Join("\n  ", empty));
        }

        /// <summary>Список известных дыр не гниёт: заведённому ключу в нём не место.</summary>
        [Test]
        public void KnownGaps_ContainNothingAlreadyDeclared()
        {
            var known = new HashSet<string>(DeclaredKeys().Values);
            var stale = KnownGaps.Where(known.Contains).OrderBy(k => k).ToList();

            Assert.IsEmpty(stale,
                "Эти ключи уже заведены — вычеркни их из KnownGaps, иначе список перестанет " +
                "что-либо значить:\n  " + string.Join("\n  ", stale));
        }

        // --- Чтение таблиц с диска ---

        /// <summary>Объявленные ключи таблицы UI: id записи → ключ (из <c>UI Shared Data.asset</c>).</summary>
        private static IReadOnlyDictionary<long, string> DeclaredKeys()
        {
            string path = Path.Combine(TablesDir, "UI Shared Data.asset");
            Assert.IsTrue(File.Exists(path), $"Общая таблица UI не найдена: {path}");

            var map = new Dictionary<long, string>();
            long id = 0;
            foreach (string line in File.ReadLines(path))
            {
                Match m = Regex.Match(line, @"^\s*-?\s*m_Id:\s*(\d+)\s*$");
                if (m.Success) { id = long.Parse(m.Groups[1].Value); continue; }

                m = Regex.Match(line, @"^\s*m_Key:\s*(.+?)\s*$");
                if (m.Success && id != 0) { map[id] = Unquote(m.Groups[1].Value); id = 0; }
            }

            Assert.IsNotEmpty(map, "В общей таблице UI не разобрано ни одного ключа — гейт молчал бы всегда.");
            return map;
        }

        /// <summary>Значения одной локали: id записи → строка (пустая, если текста нет).</summary>
        private static IReadOnlyDictionary<long, string> LocalizedValues(string fileName)
        {
            string path = Path.Combine(TablesDir, fileName);
            Assert.IsTrue(File.Exists(path), $"Таблица локали не найдена: {path}");

            var map = new Dictionary<long, string>();
            long id = 0;
            foreach (string line in File.ReadLines(path))
            {
                Match m = Regex.Match(line, @"^\s*-?\s*m_Id:\s*(\d+)\s*$");
                if (m.Success) { id = long.Parse(m.Groups[1].Value); continue; }

                m = Regex.Match(line, @"^\s*m_Localized:\s*(.*?)\s*$");
                if (m.Success && id != 0) { map[id] = Unquote(m.Groups[1].Value); id = 0; }
            }
            return map;
        }

        /// <summary>Снять кавычки YAML — содержимое строки нас интересует только на «пусто или нет».</summary>
        private static string Unquote(string raw)
        {
            if (raw.Length >= 2 && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\'')))
                return raw.Substring(1, raw.Length - 2);
            return raw;
        }

        // --- Сбор ключей из кода ---

        /// <summary>
        /// Ключи <c>ui.*</c>, которые код зовёт строковым литералом. Звуковые события пропускаются:
        /// они тоже начинаются с <c>ui.</c>, но живут в аудио-каталоге, и за них отвечает
        /// <c>AudioCoverageTests</c>.
        /// </summary>
        private static IEnumerable<(string Key, string File)> CalledUiKeys()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts");
            var literal = new Regex("\"(" + Regex.Escape(ContentKeys.UiKeyPrefix) + "[a-z0-9_.]+)\"");
            var seen = new HashSet<string>();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                foreach (Match m in literal.Matches(File.ReadAllText(file)))
                {
                    string key = m.Groups[1].Value;
                    if (key.EndsWith(".ui")) continue;      // звуковое событие, не текст
                    if (seen.Add(key)) yield return (key, name);
                }
            }
        }
    }
}
