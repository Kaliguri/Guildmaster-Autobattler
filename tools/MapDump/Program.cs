using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Guildmaster.Core.Random;
using Guildmaster.Guild;

namespace Guildmaster.Tools.MapDump
{
    /// <summary>
    /// Дамп карт акта для стенда Лаборатории: гоняет настоящий <see cref="MapGenerator"/> вне Unity и
    /// пишет пачку сгенерированных карт одним JSON.
    /// <para>Зачем вообще: композицию карты (воздух между узлами, место под имена зон, читаемость
    /// областей) надо проверять глазами до того, как она уедет в игру. Стенд рисует, но рисовать он
    /// обязан НАШУ карту — иначе решения принимаются по красивой чужой картинке.</para>
    /// <para>Пачка, а не одна карта: живой кнопки «сгенерируй ещё» у статического сайта нет, а
    /// шестьдесят сидов в файле неотличимы от неё на глаз. Сид пишется в дамп, поэтому кривую карту
    /// можно назвать числом и воспроизвести в редакторе.</para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var opts = Options.Parse(args);
                var config = ActConfigYaml.Read(Path.Combine(opts.ProjectRoot, opts.ConfigPath));
                var style = MapStyleYaml.Read(Path.Combine(opts.ProjectRoot, opts.StylePath),
                                              Path.Combine(opts.ProjectRoot, opts.NodePrefabPath));
                var profiles = Profile.ReadAll(Path.Combine(opts.ProjectRoot, opts.ProfilesPath));

                // Сиды общие на все профили: сравнение имеет смысл только на одной и той же карте.
                var seeds = new ulong[opts.Count];
                for (int i = 0; i < opts.Count; i++) seeds[i] = opts.FirstSeed + (ulong)i;

                foreach (Profile profile in profiles)
                {
                    profile.Apply(config, style);
                    profile.Maps = new List<DumpedMap>(seeds.Length);
                    foreach (ulong seed in seeds) profile.Maps.Add(DumpedMap.Generate(seed, profile.Config));
                }

                string json = Serialize(opts, seeds, profiles);
                string outPath = Path.Combine(opts.ProjectRoot, opts.OutPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, json);

                Console.WriteLine($"Профилей: {profiles.Count} ({string.Join(", ", profiles.ConvertAll(p => p.Id))})");
                Console.WriteLine($"Карт в каждом: {seeds.Length} (сиды {seeds[0]}..{seeds[seeds.Length - 1]})");
                Console.WriteLine($"Файл: {opts.OutPath} ({new FileInfo(outPath).Length / 1024} КБ)");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }

        /// <summary>
        /// Компактный JSON: узлы тройками чисел, рёбра парами индексов, тип — индексом в общем словаре.
        /// Читаемый формат с ключами на каждый узел раздул бы файл вчетверо, а он лежит в репозитории.
        /// </summary>
        private static string Serialize(Options opts, ulong[] seeds, List<Profile> profiles)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();

                // Дата снимка: дамп — единственные данные сайта, которые не читаются с диска на лету,
                // поэтому «когда сняли» надо видеть на странице. Иначе правку генератора без прогона
                // скрипта нечем отличить от свежей карты.
                w.WriteString("generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

                w.WriteStartArray("source");
                w.WriteStringValue(opts.ConfigPath.Replace('\\', '/'));
                w.WriteStringValue(opts.StylePath.Replace('\\', '/'));
                w.WriteStringValue(opts.NodePrefabPath.Replace('\\', '/'));
                w.WriteStringValue(opts.ProfilesPath.Replace('\\', '/'));
                w.WriteEndArray();

                w.WriteStartArray("seeds");
                foreach (ulong seed in seeds) w.WriteNumberValue(seed);
                w.WriteEndArray();

                // Словарь типов идёт из enum, а не из руками набранного списка: дописанный в игре тип
                // обязан приехать в стенд сам, иначе подпись узла начнёт врать.
                w.WriteStartArray("nodeTypes");
                foreach (string name in Enum.GetNames(typeof(MapNodeType))) w.WriteStringValue(name);
                w.WriteEndArray();

                w.WriteStartArray("profiles");
                foreach (Profile profile in profiles) profile.Write(w);
                w.WriteEndArray();

                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>Одна сгенерированная карта в виде, пригодном для отрисовки: узлы и рёбра по индексам.</summary>
    internal sealed class DumpedMap
    {
        private ulong _seed;
        private MapNode[] _nodes;
        private List<(int From, int To)> _edges;

        public static DumpedMap Generate(ulong seed, MapGenConfig config)
        {
            var rng = new XorShiftRng(seed);
            MapState state = MapGenerator.Generate(rng, config);

            var index = new Dictionary<string, int>(state.Nodes.Length);
            for (int i = 0; i < state.Nodes.Length; i++) index[state.Nodes[i].Id] = i;

            var edges = new List<(int, int)>();
            for (int i = 0; i < state.Nodes.Length; i++)
                foreach (string to in state.Nodes[i].Edges)
                {
                    if (!index.TryGetValue(to, out int j))
                        throw new InvalidOperationException($"Ребро в несуществующий узел '{to}' (сид {seed}).");
                    edges.Add((i, j));
                }

            return new DumpedMap { _seed = seed, _nodes = state.Nodes, _edges = edges };
        }

        public void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteNumber("seed", _seed);

            w.WriteStartArray("nodes");
            foreach (MapNode node in _nodes)
            {
                w.WriteStartArray();
                w.WriteNumberValue(node.Floor);
                w.WriteNumberValue(node.Row);
                w.WriteNumberValue((int)node.Type);
                w.WriteEndArray();
            }
            w.WriteEndArray();

            w.WriteStartArray("edges");
            foreach ((int from, int to) in _edges)
            {
                w.WriteStartArray();
                w.WriteNumberValue(from);
                w.WriteNumberValue(to);
                w.WriteEndArray();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
    }

    /// <summary>
    /// Профиль карты: именованный набор переопределений поверх ассетов — «а если шаг больше, а веер уже».
    /// <para>Профиль <c>asset</c> переопределений не имеет и обязан быть первым: он точка отсчёта, и
    /// сравнивать надо с тем, что стоит в игре, а не с другим экспериментом.</para>
    /// <para>Все профили генерируются на ОДНИХ И ТЕХ ЖЕ сидах. Иначе разговор «эта раскладка лучше»
    /// превращается в спор о том, кому какая карта попалась.</para>
    /// </summary>
    internal sealed class Profile
    {
        public string Id = "";
        public string Title = "";
        public string Note = "";
        public MapGenConfig Config;
        public Dictionary<string, double> Style;
        public List<DumpedMap> Maps = new List<DumpedMap>();

        private Dictionary<string, int> _configPatch = new Dictionary<string, int>();
        private Dictionary<string, double> _stylePatch = new Dictionary<string, double>();

        public static List<Profile> ReadAll(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Не нашла профили: {path}");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("profiles", out JsonElement list))
                throw new InvalidDataException($"В {Path.GetFileName(path)} нет массива 'profiles'.");

            var result = new List<Profile>();
            foreach (JsonElement item in list.EnumerateArray())
            {
                var profile = new Profile
                {
                    Id = Text(item, "id"),
                    Title = Text(item, "title"),
                    Note = item.TryGetProperty("note", out JsonElement note) ? note.GetString() ?? "" : "",
                };

                if (item.TryGetProperty("config", out JsonElement cfgPatch))
                    foreach (JsonProperty p in cfgPatch.EnumerateObject())
                        profile._configPatch[p.Name] = p.Value.GetInt32();

                if (item.TryGetProperty("style", out JsonElement stylePatch))
                    foreach (JsonProperty p in stylePatch.EnumerateObject())
                        profile._stylePatch[p.Name] = p.Value.GetDouble();

                result.Add(profile);
            }

            if (result.Count == 0) throw new InvalidDataException("Список профилей пуст.");
            if (result[0].Id != "asset")
                throw new InvalidDataException("Первым профилем обязан идти 'asset' — точка отсчёта без переопределений.");
            if (result[0]._configPatch.Count > 0 || result[0]._stylePatch.Count > 0)
                throw new InvalidDataException("У профиля 'asset' не может быть переопределений: он и есть то, что в игре.");

            return result;
        }

        /// <summary>Копия ассетных чисел с наложенным патчем. Именно копия: базу правят все профили подряд.</summary>
        public void Apply(MapGenConfig assetConfig, Dictionary<string, double> assetStyle)
        {
            Config = assetConfig.Validated();          // Validated() возвращает клон — этого и хватает
            foreach (var patch in _configPatch)
            {
                switch (patch.Key)
                {
                    case "Columns":         Config.Columns         = patch.Value; break;
                    case "EdgeColumnWidth": Config.EdgeColumnWidth = patch.Value; break;
                    case "EdgeColumns":     Config.EdgeColumns     = patch.Value; break;
                    case "MinColumnWidth":  Config.MinColumnWidth  = patch.Value; break;
                    case "MaxColumnWidth":  Config.MaxColumnWidth  = patch.Value; break;
                    case "MaxEdgesPerNode": Config.MaxEdgesPerNode = patch.Value; break;
                    default: throw new InvalidDataException(
                        $"Профиль '{Id}': поле конфига '{patch.Key}' не существует. Опечатка тихо не пройдёт.");
                }
            }
            Config = Config.Validated();               // патч мог вывести числа за границы

            Style = new Dictionary<string, double>(assetStyle);
            foreach (var patch in _stylePatch)
            {
                if (!Style.ContainsKey(patch.Key))
                    throw new InvalidDataException($"Профиль '{Id}': поля стиля '{patch.Key}' нет в дампе MapStyle.");
                Style[patch.Key] = patch.Value;
            }
        }

        public void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("id", Id);
            w.WriteString("title", Title);
            w.WriteString("note", Note);

            w.WriteStartObject("config");
            w.WriteNumber("columns", Config.Columns);
            w.WriteNumber("edgeColumnWidth", Config.EdgeColumnWidth);
            w.WriteNumber("edgeColumns", Config.EdgeColumns);
            w.WriteNumber("minColumnWidth", Config.MinColumnWidth);
            w.WriteNumber("maxColumnWidth", Config.MaxColumnWidth);
            w.WriteNumber("maxEdgesPerNode", Config.MaxEdgesPerNode);
            w.WriteEndObject();

            w.WriteStartObject("style");
            foreach (var pair in Style) w.WriteNumber(pair.Key, pair.Value);
            w.WriteEndObject();

            w.WriteStartArray("maps");
            foreach (DumpedMap map in Maps) map.Write(w);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        private static string Text(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || value.GetString() is not string text || text.Length == 0)
                throw new InvalidDataException($"У профиля нет обязательного поля '{name}'.");
            return text;
        }
    }

    /// <summary>Аргументы командной строки. Всё, кроме корня проекта, имеет разумный дефолт.</summary>
    internal sealed class Options
    {
        public string ProjectRoot = ".";
        public string ConfigPath = "Assets/_Project/ScriptableObjects/Configs/ActConfig.asset";
        public string StylePath = "Assets/_Project/ScriptableObjects/Configs/MapStyle.asset";
        public string NodePrefabPath = "Assets/_Project/Prefabs/Map/MapNode.prefab";
        public string ProfilesPath = "tools/MapDump/profiles.json";
        public string OutPath = "docs/lab/data/act-maps.json";
        public int Count = 60;
        public ulong FirstSeed = 1000;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                string Next() => i + 1 < args.Length ? args[++i]
                    : throw new ArgumentException($"У аргумента {key} нет значения.");

                switch (key)
                {
                    case "--project": o.ProjectRoot = Next(); break;
                    case "--config":  o.ConfigPath  = Next(); break;
                    case "--style":   o.StylePath   = Next(); break;
                    case "--profiles": o.ProfilesPath = Next(); break;
                    case "--out":     o.OutPath     = Next(); break;
                    case "--count":   o.Count       = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--seed":    o.FirstSeed   = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException($"Неизвестный аргумент: {key}");
                }
            }
            if (o.Count < 1) throw new ArgumentException("--count меньше единицы: дампить нечего.");
            return o;
        }
    }

    /// <summary>
    /// Чтение <c>MapGenConfig</c> из YAML-ассета <c>ActConfig</c> без Unity.
    /// <para><b>Почему из ассета, а не из дефолтов кода:</b> играет то, что лежит в ассете. Совпадают
    /// они сегодня или нет — дампер обязан показывать ту карту, которую увидит игрок.</para>
    /// <para><b>Почему парсер строгий:</b> он падает на незнакомом ключе и на неожиданном отступе,
    /// вместо того чтобы молча взять дефолт. Тихо пропущенное поле конфига даёт правдоподобную, но
    /// чужую карту — а по ней принимаются решения о показе.</para>
    /// </summary>
    internal static class ActConfigYaml
    {
        public static MapGenConfig Read(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Не нашла конфиг акта: {path}");

            var cfg = new MapGenConfig
            {
                Zones = Array.Empty<ZoneRule>(),
                Anchors = Array.Empty<AnchorRule>(),
            };
            var zones = new List<ZoneRule>();
            var anchors = new List<AnchorRule>();
            var weights = new List<NodeTypeWeight>();

            string[] lines = File.ReadAllLines(path);
            int start = Array.FindIndex(lines, l => l.TrimEnd() == "  _map:");
            if (start < 0) throw new InvalidDataException($"В {path} нет блока '_map:' — ассет не от ActConfig?");

            // Куда складывать поля текущей строки: у списков Zones и Anchors разный набор ключей.
            var section = Section.Root;
            var zone = new ZoneRule();
            var anchor = new AnchorRule();
            bool hasZone = false, hasAnchor = false;

            void FlushZone()
            {
                if (!hasZone) return;
                zone.Weights = weights.ToArray();
                weights.Clear();
                zones.Add(zone);
                zone = new ZoneRule();
                hasZone = false;
            }
            void FlushAnchor()
            {
                if (!hasAnchor) return;
                anchors.Add(anchor);
                anchor = new AnchorRule();
                hasAnchor = false;
            }

            for (int i = start + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (raw.Trim().Length == 0) continue;

                int indent = raw.Length - raw.TrimStart(' ').Length;
                if (indent < 4) break;                       // блок _map кончился

                string text = raw.TrimStart(' ');
                bool isItem = text.StartsWith("- ", StringComparison.Ordinal);
                if (isItem) { text = text.Substring(2); indent += 2; }

                int colon = text.IndexOf(':');
                if (colon < 0) throw new InvalidDataException($"Строка {i + 1}: ожидала 'ключ: значение', получила '{raw}'.");
                string key = text.Substring(0, colon).Trim();
                string value = text.Substring(colon + 1).Trim();

                switch (indent)
                {
                    case 4:                                   // поля самого MapGenConfig
                        FlushZone(); FlushAnchor();
                        switch (key)
                        {
                            case "Columns":         cfg.Columns         = Int(value, i); break;
                            case "EdgeColumnWidth": cfg.EdgeColumnWidth = Int(value, i); break;
                            case "EdgeColumns":     cfg.EdgeColumns     = Int(value, i); break;
                            case "MinColumnWidth":  cfg.MinColumnWidth  = Int(value, i); break;
                            case "MaxColumnWidth":  cfg.MaxColumnWidth  = Int(value, i); break;
                            case "MaxEdgesPerNode": cfg.MaxEdgesPerNode = Int(value, i); break;
                            case "Zones":           section = Section.Zones;   break;
                            case "Anchors":         section = Section.Anchors; break;
                            default: throw new InvalidDataException(
                                $"Строка {i + 1}: поле '{key}' дампер не знает. Конфиг изменился — обнови ActConfigYaml, " +
                                "иначе стенд нарисует карту по неполным данным.");
                        }
                        break;

                    case 6:                                   // элемент Zones/Anchors и его поля
                        if (section == Section.Zones)
                        {
                            if (isItem) { FlushZone(); hasZone = true; }
                            switch (key)
                            {
                                case "FromFloor": zone.FromFloor = Int(value, i); break;
                                case "ToFloor":   zone.ToFloor   = Int(value, i); break;
                                case "Weights":   break;      // веса приедут следующими строками
                                default: throw new InvalidDataException($"Строка {i + 1}: у ZoneRule нет поля '{key}'.");
                            }
                        }
                        else if (section == Section.Anchors)
                        {
                            if (isItem) { FlushAnchor(); hasAnchor = true; }
                            switch (key)
                            {
                                case "Floor": anchor.Floor = Int(value, i); break;
                                case "Type":  anchor.Type  = NodeType(value, i); break;
                                case "Width": anchor.Width = Int(value, i); break;
                                default: throw new InvalidDataException($"Строка {i + 1}: у AnchorRule нет поля '{key}'.");
                            }
                        }
                        else throw new InvalidDataException($"Строка {i + 1}: элемент списка вне Zones/Anchors.");
                        break;

                    case 8:                                   // NodeTypeWeight внутри зоны
                        if (section != Section.Zones) throw new InvalidDataException($"Строка {i + 1}: веса вне Zones.");
                        if (isItem) weights.Add(new NodeTypeWeight());
                        if (weights.Count == 0) throw new InvalidDataException($"Строка {i + 1}: вес без элемента списка.");
                        var w = weights[weights.Count - 1];
                        switch (key)
                        {
                            case "Type":   w.Type   = NodeType(value, i); break;
                            case "Weight": w.Weight = Int(value, i); break;
                            default: throw new InvalidDataException($"Строка {i + 1}: у NodeTypeWeight нет поля '{key}'.");
                        }
                        weights[weights.Count - 1] = w;
                        break;

                    default:
                        throw new InvalidDataException($"Строка {i + 1}: неожиданный отступ {indent} в '{raw}'.");
                }
            }

            FlushZone();
            FlushAnchor();

            cfg.Zones = zones.ToArray();
            cfg.Anchors = anchors.ToArray();
            return cfg;
        }

        private enum Section { Root, Zones, Anchors }

        private static int Int(string value, int line) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : throw new InvalidDataException($"Строка {line + 1}: '{value}' — не целое число.");

        /// <summary>Тип узла лежит в YAML индексом enum: Unity сериализует перечисления числом.</summary>
        private static MapNodeType NodeType(string value, int line)
        {
            int raw = Int(value, line);
            if (!Enum.IsDefined(typeof(MapNodeType), raw))
                throw new InvalidDataException($"Строка {line + 1}: тип узла {raw} вне MapNodeType.");
            return (MapNodeType)raw;
        }
    }

    /// <summary>
    /// Числа показа карты из <c>MapStyle</c>-ассета: шаги сетки и поля листа.
    /// <para><b>Строгость здесь обратная</b> той, что у <see cref="ActConfigYaml"/>, и намеренно. Конфиг
    /// акта читается ЦЕЛИКОМ, поэтому новое поле в нём — повод упасть: пропустив его молча, дампер
    /// сгенерирует чужую карту. Из стиля же берётся горсть полей из полусотни, и остальные дампера не
    /// касаются — падать на них значило бы ломать инструмент каждой правкой цвета. Зато исчезновение
    /// нужного поля — ошибка: значит показ переехал, и стенд об этом ещё не знает.</para>
    /// </summary>
    internal static class MapStyleYaml
    {
        /// <summary>
        /// Радиус узла: он живёт не в стиле, а в префабе узла — там же, где его спрайт и зона хвата.
        /// Дампится потому, что от него зависит, тесно ли выглядит сетка: своя прикидка в рисовалке
        /// была бы вторым владельцем числа и разъехалась бы с игрой молча.
        /// </summary>
        private static readonly (string Key, string Yaml)[] WantedPrefab =
        {
            ("nodeRadius", "_visualRadius"),
        };

        // Ключ в дампе → путь в ассете. Шаги сетки лежат внутри _layout, поля листа — в корне.
        private static readonly (string Key, string Yaml)[] Wanted =
        {
            ("stepX", "StepX"),
            ("stepY", "StepY"),
            ("jitterX", "JitterX"),
            ("jitterY", "JitterY"),
            ("minDistance", "MinDistance"),
            ("relaxIterations", "RelaxIterations"),
            // Сколько этажей держит камера. Без него разговор о «воздухе» уезжает на общий план, где
            // больший шаг только мельчит узлы, — а игрок видит карту рабочим кадром.
            ("floorsInView", "_floorsInView"),
            ("sheetPadX", "_backdropPadding"),
            ("sheetPadY", "_backdropPaddingY"),
            ("dotRadius", "_dotRadius"),
            ("dotSpacing", "_dotSpacing"),
            ("dotClearance", "_dotClearance"),
            ("edgeCurve", "_edgeCurve"),
        };

        public static Dictionary<string, double> Read(string stylePath, string nodePrefabPath)
        {
            Dictionary<string, double> found = ReadKeys(stylePath, Wanted, "стиль карты");
            foreach (var pair in ReadKeys(nodePrefabPath, WantedPrefab, "префаб узла")) found[pair.Key] = pair.Value;
            return found;
        }

        private static Dictionary<string, double> ReadKeys(string path, (string Key, string Yaml)[] wanted, string what)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Не нашла {what}: {path}");

            var found = new Dictionary<string, double>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string text = raw.Trim();
                int colon = text.IndexOf(':');
                if (colon <= 0) continue;

                string key = text.Substring(0, colon).Trim();
                string value = text.Substring(colon + 1).Trim();
                if (value.Length == 0 || value.StartsWith("{", StringComparison.Ordinal)) continue;

                foreach ((string outKey, string yamlKey) in wanted)
                {
                    if (key != yamlKey || found.ContainsKey(outKey)) continue;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        found[outKey] = d;
                }
            }

            foreach ((string outKey, string yamlKey) in wanted)
                if (!found.ContainsKey(outKey))
                    throw new InvalidDataException(
                        $"В {Path.GetFileName(path)} нет поля '{yamlKey}'. Показ карты изменился — поправь MapStyleYaml, " +
                        "иначе стенд будет рисовать по числам, которых в игре уже нет.");

            return found;
        }
    }
}
