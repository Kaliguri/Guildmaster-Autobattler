using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation.Audio;
using Guildmaster.Presentation.Design;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Единая модель данных контент-хаба (ТЗ 08 §2): скан всего дата-слоя в записи + граф ссылок в обе
    /// стороны + стат-когорты. Все страницы хаба — вьюхи над этим; сами по AssetDatabase они не ходят.
    /// Строится лениво, кэшируется статически; инвалидация — через <see cref="ContentIndexPostprocessor"/>.
    /// <para>Генерически поверх ТЕКУЩЕЙ стат-системы: когорты итерируют <see cref="StatType"/>, поэтому
    /// будущая переработка статов подхватится без правки индекса.</para>
    /// </summary>
    public static class ContentIndex
    {
        private static List<ContentEntry> _entries;
        private static Dictionary<UnityEngine.Object, ContentEntry> _byAsset;
        private static Dictionary<Type, StatCohort> _cohorts;
        private static StatsConfig _statsConfig;
        private static ClassBalanceConfig _classBalanceConfig;

        /// <summary>Индекс перестроен/инвалидирован — страницы пересобираются.</summary>
        public static event Action Changed;

        public static IReadOnlyList<ContentEntry> Entries { get { EnsureBuilt(); return _entries; } }
        public static StatsConfig StatsConfig { get { EnsureBuilt(); return _statsConfig; } }

        public static ContentEntry Find(UnityEngine.Object asset)
        {
            EnsureBuilt();
            return asset != null && _byAsset.TryGetValue(asset, out var e) ? e : null;
        }

        /// <summary>Когорта агрегатов для концертного типа юнита (RelicData / EnemyData); null, если нет.</summary>
        public static StatCohort Cohort(Type unitType)
        {
            EnsureBuilt();
            return _cohorts.TryGetValue(unitType, out var c) ? c : null;
        }

        /// <summary>Сбросить кэш; следующий доступ пересоберёт. Дёшево — фактическая пересборка ленива.</summary>
        public static void Invalidate()
        {
            _entries = null;
            Changed?.Invoke();
        }

        private static void EnsureBuilt()
        {
            if (_entries == null) Build();
        }

        // ---------------------------------------------------------------- build

        private static void Build()
        {
            _entries = new List<ContentEntry>();
            _byAsset = new Dictionary<UnityEngine.Object, ContentEntry>();

            // Конфиги берём через GameConfig — то есть тот экземпляр, которым играет игра, а не «первый
            // попавшийся ассет типа StatsConfig». Второй такой ассет (эксперимент, ветка баланса) увёл
            // бы stat-context и таблицу Balance на конфиг, которым не играют, а порядок FindAssets не
            // гарантирован — выбор менялся бы между перестройками индекса. Расхождение выглядело бы как
            // цифра в отчёте, а не как ошибка. Ту же ловушку балансный стенд уже прошёл (BalanceAssets).
            GameConfig gameConfig = FindAll<GameConfig>().FirstOrDefault();
            if (gameConfig == null)
            {
                UnityEngine.Debug.LogError("[ContentHub] GameConfig не найден — эффективные статы считать не по чему.");
            }

            _statsConfig = gameConfig != null ? gameConfig.Stats : null;
            _classBalanceConfig = gameConfig != null ? gameConfig.ClassBalance : null;

            // Контент-определения (folder-per-type) — единственный источник домена по типу.
            foreach (var cd in ContentIdUtility.FindAll())
                AddEntry(cd, cd.Id, ContentDomains.GetDomain(cd.GetType()));

            // Спутники дата-слоя (не ContentDefinition). Presentation-типы (AudioCatalog, палитры) — позже (P8/P9).
            foreach (var v in FindAll<AnimationArchetypeData>()) AddEntry(v, v.name, "Visuals");
            foreach (var c in FindAll<StatsConfig>()) AddEntry(c, c.name, "Configs");
            foreach (var c in FindAll<SimTuningConfig>()) AddEntry(c, c.name, "Configs");
            foreach (var c in FindAll<GameConfig>()) AddEntry(c, c.name, "Configs");
            foreach (var a in FindAll<AudioCatalog>()) AddEntry(a, a.name, "Audio");
            foreach (var p in FindAll<CombatColorPalette>()) AddEntry(p, p.name, "Design");

            BuildGraph();
            BuildCohorts();
        }

        private static void AddEntry(UnityEngine.Object asset, string id, string domain)
        {
            if (asset == null || _byAsset.ContainsKey(asset)) return;
            string display = !string.IsNullOrEmpty(asset.name) ? asset.name : id;
            var entry = new ContentEntry(asset, id, domain, display, AssetDatabase.GetAssetPath(asset));
            _entries.Add(entry);
            _byAsset[asset] = entry;
        }

        /// <summary>Граф ссылок: обход object-reference полей каждой записи; ребро — если цель тоже в индексе.</summary>
        private static void BuildGraph()
        {
            foreach (var entry in _entries)
            {
                if (entry.Asset == null) continue;
                using var so = new SerializedObject(entry.Asset);
                var seen = new HashSet<ContentEntry>();
                var p = so.GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var target = p.objectReferenceValue;
                    if (target == null) continue;
                    if (_byAsset.TryGetValue(target, out var te) && te != entry && seen.Add(te))
                    {
                        entry.Uses.Add(te);
                        te.UsedBy.Add(entry);
                    }
                }
            }
        }

        /// <summary>Эффективные статы каждого юнита + агрегаты по когортам (концертный тип).</summary>
        private static void BuildCohorts()
        {
            _cohorts = new Dictionary<Type, StatCohort>();
            var statTypes = (StatType[])Enum.GetValues(typeof(StatType));

            foreach (var e in _entries)
                if (e.Unit != null)
                    e.EffectiveStats = StatMath.BuildEffective(e.Unit, _statsConfig, _classBalanceConfig);

            var byType = new Dictionary<Type, List<ContentEntry>>();
            foreach (var e in _entries)
            {
                if (e.Unit == null) continue;
                if (!byType.TryGetValue(e.Type, out var list)) byType[e.Type] = list = new List<ContentEntry>();
                list.Add(e);
            }

            foreach (var kv in byType)
            {
                var cohort = new StatCohort { Size = kv.Value.Count };
                foreach (var st in statTypes)
                {
                    var vals = kv.Value.Select(x => x.Effective(st)).ToList();
                    cohort.Set(st, Aggregate(vals));
                }
                _cohorts[kv.Key] = cohort;
            }
        }

        private static StatCohort.Agg Aggregate(List<float> values)
        {
            if (values == null || values.Count == 0) return default;
            float min = values[0], max = values[0], sum = 0f;
            foreach (var v in values) { if (v < min) min = v; if (v > max) max = v; sum += v; }
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            float median = (n % 2 == 1) ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) * 0.5f;
            return new StatCohort.Agg(sum / n, median, min, max, n);
        }

        private static IEnumerable<T> FindAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null);
    }
}
