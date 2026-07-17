using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Авторинг ЗНАЧЕНИЙ контента (баланс) — write-сторона к read-инструментам (SimBench, Content Hub «Balance»).
    /// Сосед <see cref="ContentCrudService"/>: тот владеет жизненным циклом ассета (create/duplicate/delete + id),
    /// этот — правкой полей внутри ассета (статы, кулдауны, поля эффектов). Всё через <see cref="SerializedObject"/>
    /// + Undo; каждая правка возвращает <see cref="Change"/> (было→стало) для аудита. id/схему НЕ трогает.
    /// Editor-only, рантайм-SO не мутирует (правка в редакторе = авторинг, не игровая мутация).
    /// </summary>
    public static class ContentEditService
    {
        /// <summary>Запись об одной правке: ассет, поле, было→стало, применилась ли (иначе — почему пропущено).</summary>
        public readonly struct Change
        {
            public readonly string Asset;
            public readonly string Field;
            public readonly float Before;
            public readonly float After;
            public readonly bool Applied;
            public readonly string Note;

            public Change(string asset, string field, float before, float after, bool applied, string note)
            {
                Asset = asset; Field = field; Before = before; After = after; Applied = applied; Note = note;
            }

            public override string ToString()
            {
                var c = CultureInfo.InvariantCulture;
                return Applied
                    ? $"{Asset}.{Field}: {Before.ToString("0.###", c)} → {After.ToString("0.###", c)}"
                    : $"{Asset}.{Field}: SKIP ({Note})";
            }
        }

        // ---------------------------------------------------------------- queries

        /// <summary>Все ассеты типа <typeparamref name="T"/> (стабильный порядок по пути).</summary>
        public static List<T> LoadAll<T>() where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++) paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            paths.Sort(StringComparer.Ordinal);
            var result = new List<T>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(paths[i]);
                if (a != null) result.Add(a);
            }
            return result;
        }

        /// <summary>Найти контент по id (<c>domain.name</c>) или, как запасной путь, по имени ассета.</summary>
        public static T Resolve<T>(string idOrName) where T : ContentDefinition
        {
            foreach (T a in LoadAll<T>())
            {
                if (a.Id == idOrName || a.name == idOrName) return a;
            }
            return null;
        }

        // ---------------------------------------------------------------- stat edits (_stats)

        /// <summary>Умножить значение модификатора стата <paramref name="stat"/> на <paramref name="factor"/> (первое совпадение в <c>_stats</c>).</summary>
        public static Change ScaleStat(UnitData unit, StatType stat, float factor)
        {
            SerializedProperty mod = FindStatModifier(unit, stat, out SerializedObject so);
            if (mod == null) return Skip(unit, "stat:" + stat, "нет модификатора этого стата в _stats");
            SerializedProperty val = mod.FindPropertyRelative("Value");
            float before = val.floatValue;
            float after = before * factor;
            val.floatValue = after;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(unit);
            return Record(unit, stat + ".Value", before, after);
        }

        /// <summary>Задать значение модификатора стата (заменить существующий с любым Op или добавить новый c <paramref name="op"/>).</summary>
        public static Change SetStat(UnitData unit, StatType stat, ModifierOp op, float value)
        {
            var so = new SerializedObject(unit);
            SerializedProperty stats = so.FindProperty("_stats");
            for (int i = 0; i < stats.arraySize; i++)
            {
                SerializedProperty el = stats.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("Stat").enumValueIndex == (int)stat)
                {
                    SerializedProperty val = el.FindPropertyRelative("Value");
                    float before = val.floatValue;
                    el.FindPropertyRelative("Op").enumValueIndex = (int)op;
                    val.floatValue = value;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(unit);
                    return Record(unit, stat + ".Value", before, value);
                }
            }
            // нет — добавить новый модификатор
            int idx = stats.arraySize;
            stats.InsertArrayElementAtIndex(idx);
            SerializedProperty added = stats.GetArrayElementAtIndex(idx);
            added.FindPropertyRelative("Stat").enumValueIndex = (int)stat;
            added.FindPropertyRelative("Op").enumValueIndex = (int)op;
            added.FindPropertyRelative("Value").floatValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(unit);
            return Record(unit, stat + " (new)", float.NaN, value);
        }

        // ---------------------------------------------------------------- generic field

        /// <summary>Задать float-поле по пути SerializedProperty (например <c>_movingAttackSpeedPenaltyPct</c>).</summary>
        public static Change SetFloat(ScriptableObject asset, string propertyPath, float value)
        {
            var so = new SerializedObject(asset);
            SerializedProperty p = so.FindProperty(propertyPath);
            if (p == null || p.propertyType != SerializedPropertyType.Float)
                return Skip(asset, propertyPath, "нет float-поля по пути");
            float before = p.floatValue;
            p.floatValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return Record(asset, propertyPath, before, value);
        }

        /// <summary>Прибавить к float-полю (например кулдаун +1).</summary>
        public static Change AddFloat(ScriptableObject asset, string propertyPath, float delta)
        {
            var so = new SerializedObject(asset);
            SerializedProperty p = so.FindProperty(propertyPath);
            if (p == null || p.propertyType != SerializedPropertyType.Float)
                return Skip(asset, propertyPath, "нет float-поля по пути");
            float before = p.floatValue;
            float after = before + delta;
            p.floatValue = after;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return Record(asset, propertyPath, before, after);
        }

        // ---------------------------------------------------------------- ability cooldown (_abilities[id]._baseCooldown)

        /// <summary>Прибавить к базовому кулдауну способности с данным id.</summary>
        public static Change AddAbilityCooldown(UnitData unit, string abilityId, float delta)
        {
            SerializedProperty cd = FindAbilityCooldown(unit, abilityId, out SerializedObject so);
            if (cd == null) return Skip(unit, "ability:" + abilityId, "нет способности с таким id");
            float before = cd.floatValue;
            float after = before + delta;
            cd.floatValue = after;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(unit);
            return Record(unit, abilityId + ".cooldown", before, after);
        }

        // ---------------------------------------------------------------- effect component field (по имени, без знания пути)

        /// <summary>
        /// Задать float-поле <paramref name="fieldName"/> в первом компоненте эффекта, где оно есть (например
        /// <c>_internalCooldownSeconds</c> у <c>BulwarkComponent</c> — «кулдаун щита»). Не нужно знать путь массива.
        /// </summary>
        public static Change SetEffectComponentFloat(EffectData effect, string fieldName, float value)
        {
            var so = new SerializedObject(effect);
            SerializedProperty comps = so.FindProperty("_components");
            if (comps == null) return Skip(effect, fieldName, "у эффекта нет _components");
            for (int i = 0; i < comps.arraySize; i++)
            {
                SerializedProperty field = FindRelativeByName(comps.GetArrayElementAtIndex(i), fieldName);
                if (field != null && field.propertyType == SerializedPropertyType.Float)
                {
                    float before = field.floatValue;
                    field.floatValue = value;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(effect);
                    return Record(effect, fieldName, before, value);
                }
            }
            return Skip(effect, fieldName, "нет float-поля с таким именем в компонентах");
        }

        // ---------------------------------------------------------------- commit + log

        /// <summary>Сбросить все правки на диск.</summary>
        public static void Save() => AssetDatabase.SaveAssets();

        /// <summary>
        /// Записать журнал правок в <c>BalanceReports/balance_changes_*.md</c> (append-friendly отдельными файлами).
        /// Возвращает путь. Аудит «что и когда крутили» — опора цикла баланса.
        /// </summary>
        public static string WriteChangeLog(IEnumerable<Change> changes, string title)
        {
            string root = Directory.GetParent(Application.dataPath)!.FullName;
            string dir = Path.Combine(root, "BalanceReports");
            Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.Append("# Balance changes — ").AppendLine(title);
            sb.Append("_").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).AppendLine("_").AppendLine();
            sb.AppendLine("| Asset | Field | Before | After | Status |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            var c = CultureInfo.InvariantCulture;
            foreach (Change ch in changes)
            {
                sb.Append("| ").Append(ch.Asset).Append(" | ").Append(ch.Field).Append(" | ")
                  .Append(float.IsNaN(ch.Before) ? "—" : ch.Before.ToString("0.###", c)).Append(" | ")
                  .Append(ch.After.ToString("0.###", c)).Append(" | ")
                  .Append(ch.Applied ? "ok" : "SKIP: " + ch.Note).AppendLine(" |");
            }

            string path = Path.Combine(dir, "balance_changes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", c) + ".md");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        // ---------------------------------------------------------------- internals

        private static SerializedProperty FindStatModifier(UnitData unit, StatType stat, out SerializedObject so)
        {
            so = new SerializedObject(unit);
            SerializedProperty stats = so.FindProperty("_stats");
            if (stats == null) return null;
            for (int i = 0; i < stats.arraySize; i++)
            {
                SerializedProperty el = stats.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("Stat").enumValueIndex == (int)stat) return el;
            }
            return null;
        }

        private static SerializedProperty FindAbilityCooldown(UnitData unit, string abilityId, out SerializedObject so)
        {
            so = new SerializedObject(unit);
            SerializedProperty ab = so.FindProperty("_abilities");
            if (ab == null) return null;
            for (int i = 0; i < ab.arraySize; i++)
            {
                SerializedProperty el = ab.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("_id").stringValue == abilityId)
                    return el.FindPropertyRelative("_baseCooldown");
            }
            return null;
        }

        private static SerializedProperty FindRelativeByName(SerializedProperty element, string fieldName)
        {
            SerializedProperty copy = element.Copy();
            SerializedProperty end = element.GetEndProperty();
            bool enter = true;
            while (copy.NextVisible(enter) && !SerializedProperty.EqualContents(copy, end))
            {
                enter = false;
                if (copy.name == fieldName) return copy.Copy();
            }
            return null;
        }

        private static Change Record(UnityEngine.Object asset, string field, float before, float after)
            => new Change(asset.name, field, before, after, true, null);

        private static Change Skip(UnityEngine.Object asset, string field, string note)
        {
            Debug.LogWarning($"[ContentEditService] пропуск {asset.name}.{field}: {note}");
            return new Change(asset.name, field, float.NaN, float.NaN, false, note);
        }
    }
}
