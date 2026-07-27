using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Пакет балансных правок из файла: применяется целиком и целиком же откатывается.
    /// </summary>
    /// <remarks>
    /// Заведено под вопрос «а что если попробовать вариант Б»: примерка варианта не должна означать
    /// ручное запоминание десятка исходных чисел. Применение пишет рядом ОБРАТНЫЙ пресет
    /// (<c>*.undo.json</c>) из абсолютных значений «как было» — откат это просто применение обратного
    /// файла, без хитрой машинерии и без веры в то, что с тех пор ничего не трогали.
    /// <para>Правки идут через <see cref="ContentEditService"/> — тот же SerializedObject + Undo и тот
    /// же <c>Change</c>-аудит, что у одиночной правки. Пакет только собирает их в один заход.</para>
    /// <example>
    /// <code>
    /// {
    ///   "title": "BAL-001 вариант 2: Друид становится Дальником",
    ///   "edits": [
    ///     { "op": "scaleStat", "asset": "Druid", "stat": "Power", "factor": 0.58 },
    ///     { "op": "setStat",   "asset": "BaseRelic", "stat": "MaxHP", "modOp": "Override", "value": 2000 },
    ///     { "op": "setFloat",  "asset": "BulwarkShield", "path": "_baseDuration", "value": 0.5 },
    ///     { "op": "scaleStat", "cohort": { "class": "Tank" }, "stat": "MaxHP", "factor": 1.1 }
    ///   ]
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public static class ContentEditBatch
    {
        /// <summary>
        /// Применить пресет. Возвращает список правок (включая пропущенные — с причиной) и пишет
        /// рядом обратный пресет плюс обычный change-log.
        /// </summary>
        public static List<ContentEditService.Change> Apply(string presetPath)
        {
            var changes = new List<ContentEditService.Change>();
            if (!File.Exists(presetPath))
            {
                Debug.LogError($"[ContentEditBatch] Пресет не найден: {presetPath}");
                return changes;
            }

            JObject preset;
            try
            {
                preset = JObject.Parse(File.ReadAllText(presetPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ContentEditBatch] Пресет не разобрался: {e.Message}");
                return changes;
            }

            string title = (string)preset["title"] ?? Path.GetFileNameWithoutExtension(presetPath);
            var undo = new JArray();

            foreach (JToken token in preset["edits"] ?? new JArray())
            {
                if (token is JObject edit) ApplyEdit(edit, changes, undo);
            }

            ContentEditService.Save();

            WriteUndo(presetPath, title, undo);
            string log = ContentEditService.WriteChangeLog(changes, title);
            Debug.Log($"[ContentEditBatch] «{title}»: правок {changes.Count}. Журнал: {log}");
            return changes;
        }

        // ---------------------------------------------------------------- одна правка

        private static void ApplyEdit(JObject edit, List<ContentEditService.Change> changes, JArray undo)
        {
            string op = (string)edit["op"];
            if (string.IsNullOrEmpty(op))
            {
                Debug.LogWarning("[ContentEditBatch] Правка без «op» пропущена.");
                return;
            }

            foreach (ScriptableObject target in ResolveTargets(edit))
            {
                switch (op)
                {
                    case "scaleStat": ScaleStat(edit, target, changes, undo); break;
                    case "setStat":   SetStat(edit, target, changes, undo);   break;
                    case "setFloat":  SetFloat(edit, target, changes, undo);  break;
                    case "addFloat":  AddFloat(edit, target, changes, undo);  break;
                    case "addCooldown": AddCooldown(edit, target, changes, undo); break;
                    case "setEffectField": SetEffectField(edit, target, changes, undo); break;
                    default:
                        Debug.LogWarning($"[ContentEditBatch] Неизвестная операция «{op}» — пропущена.");
                        return;
                }
            }
        }

        private static void ScaleStat(JObject edit, ScriptableObject target,
            List<ContentEditService.Change> changes, JArray undo)
        {
            if (!AsUnit(target, out UnitData unit) || !AsStat(edit, out StatType stat)) return;

            float factor = Float(edit["factor"], 1f);
            changes.Add(ContentEditService.ScaleStat(unit, stat, factor));
            if (Mathf.Abs(factor) > 1e-6f)
            {
                undo.Add(Edit("scaleStat", unit.name, new JProperty("stat", stat.ToString()),
                    new JProperty("factor", 1f / factor)));
            }
        }

        private static void SetStat(JObject edit, ScriptableObject target,
            List<ContentEditService.Change> changes, JArray undo)
        {
            if (!AsUnit(target, out UnitData unit) || !AsStat(edit, out StatType stat)) return;

            var modOp = ParseEnum((string)edit["modOp"], ModifierOp.Override);
            ContentEditService.Change change = ContentEditService.SetStat(unit, stat, modOp, Float(edit["value"], 0f));
            changes.Add(change);
            if (change.Applied)
            {
                undo.Add(Edit("setStat", unit.name, new JProperty("stat", stat.ToString()),
                    new JProperty("modOp", modOp.ToString()), new JProperty("value", change.Before)));
            }
        }

        private static void SetFloat(JObject edit, ScriptableObject target,
            List<ContentEditService.Change> changes, JArray undo)
        {
            string path = (string)edit["path"];
            if (string.IsNullOrEmpty(path)) return;

            ContentEditService.Change change = ContentEditService.SetFloat(target, path, Float(edit["value"], 0f));
            changes.Add(change);
            if (change.Applied)
            {
                undo.Add(Edit("setFloat", target.name, new JProperty("path", path),
                    new JProperty("value", change.Before)));
            }
        }

        private static void AddFloat(JObject edit, ScriptableObject target,
            List<ContentEditService.Change> changes, JArray undo)
        {
            string path = (string)edit["path"];
            if (string.IsNullOrEmpty(path)) return;

            float delta = Float(edit["delta"], 0f);
            changes.Add(ContentEditService.AddFloat(target, path, delta));
            undo.Add(Edit("addFloat", target.name, new JProperty("path", path), new JProperty("delta", -delta)));
        }

        private static void AddCooldown(JObject edit, ScriptableObject target,
            List<ContentEditService.Change> changes, JArray undo)
        {
            if (!AsUnit(target, out UnitData unit)) return;

            string ability = (string)edit["ability"];
            float delta = Float(edit["delta"], 0f);
            changes.Add(ContentEditService.AddAbilityCooldown(unit, ability, delta));
            undo.Add(Edit("addCooldown", unit.name, new JProperty("ability", ability),
                new JProperty("delta", -delta)));
        }

        private static void SetEffectField(JObject edit, ScriptableObject target,
            List<ContentEditService.Change> changes, JArray undo)
        {
            if (target is not EffectData effect)
            {
                Debug.LogWarning($"[ContentEditBatch] «{target.name}» — не EffectData, setEffectField пропущен.");
                return;
            }

            string field = (string)edit["field"];
            ContentEditService.Change change = ContentEditService.SetEffectComponentFloat(
                effect, field, Float(edit["value"], 0f));
            changes.Add(change);
            if (change.Applied)
            {
                undo.Add(Edit("setEffectField", effect.name, new JProperty("field", field),
                    new JProperty("value", change.Before)));
            }
        }

        // ---------------------------------------------------------------- разбор целей

        /// <summary>
        /// Кого правим: один ассет по <c>asset</c> или целую когорту по <c>cohort</c>. Когорта
        /// разворачивается в конкретные ассеты ЗДЕСЬ, чтобы обратный пресет содержал поимённый список:
        /// состав когорты со временем меняется, а откат обязан вернуть ровно то, что тронули.
        /// </summary>
        private static List<ScriptableObject> ResolveTargets(JObject edit)
        {
            var targets = new List<ScriptableObject>();

            var name = (string)edit["asset"];
            if (!string.IsNullOrEmpty(name))
            {
                ScriptableObject asset = ContentEditService.Resolve<UnitData>(name)
                                         ?? (ScriptableObject)ContentEditService.Resolve<EffectData>(name);
                if (asset == null) Debug.LogWarning($"[ContentEditBatch] Ассет «{name}» не найден — правка пропущена.");
                else targets.Add(asset);
                return targets;
            }

            if (edit["cohort"] is not JObject cohort)
            {
                Debug.LogWarning("[ContentEditBatch] У правки нет ни «asset», ни «cohort» — пропущена.");
                return targets;
            }

            List<UnitData> units = null;
            if (cohort["class"] != null) units = ContentCohorts.OfClass(ParseEnum((string)cohort["class"], UnitClass.Bruiser));
            else if (cohort["attackType"] != null) units = ContentCohorts.OfAttackType(ParseEnum((string)cohort["attackType"], AttackType.Melee));
            else if (cohort["school"] != null) units = ContentCohorts.OfSchool(ParseEnum((string)cohort["school"], DamageSchool.Physical));
            else if (cohort["creatureType"] != null) units = ContentCohorts.OfCreatureType(ParseEnum((string)cohort["creatureType"], CreatureType.Living));
            else if (cohort["idPrefix"] != null) units = ContentCohorts.WithIdPrefix((string)cohort["idPrefix"]);

            if (units == null) Debug.LogWarning("[ContentEditBatch] Когорта не распознана — правка пропущена.");
            else targets.AddRange(units);
            return targets;
        }

        // ---------------------------------------------------------------- мелочь

        private static bool AsUnit(ScriptableObject target, out UnitData unit)
        {
            unit = target as UnitData;
            if (unit != null) return true;
            Debug.LogWarning($"[ContentEditBatch] «{target.name}» — не UnitData, правка стата пропущена.");
            return false;
        }

        private static bool AsStat(JObject edit, out StatType stat)
        {
            var raw = (string)edit["stat"];
            if (Enum.TryParse(raw, out stat)) return true;
            Debug.LogWarning($"[ContentEditBatch] Неизвестный стат «{raw}» — правка пропущена.");
            return false;
        }

        private static T ParseEnum<T>(string raw, T fallback) where T : struct
            => Enum.TryParse(raw, out T parsed) ? parsed : fallback;

        private static float Float(JToken token, float fallback)
            => token != null && float.TryParse(token.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v) ? v : fallback;

        private static JObject Edit(string op, string asset, params JProperty[] rest)
        {
            var o = new JObject(new JProperty("op", op), new JProperty("asset", asset));
            foreach (JProperty p in rest) o.Add(p);
            return o;
        }

        /// <summary>
        /// Обратный пресет пишется в ОБРАТНОМ порядке: если две правки трогали одно поле, откат
        /// должен разматывать их с конца, иначе вернётся промежуточное значение, а не исходное.
        /// </summary>
        private static void WriteUndo(string presetPath, string title, JArray undo)
        {
            var reversed = new JArray();
            for (int i = undo.Count - 1; i >= 0; i--) reversed.Add(undo[i]);

            var doc = new JObject(
                new JProperty("title", "ОТКАТ: " + title),
                new JProperty("edits", reversed));

            string path = Path.ChangeExtension(presetPath, null) + ".undo.json";
            File.WriteAllText(path, doc.ToString());
            Debug.Log($"[ContentEditBatch] Обратный пресет: {path}");
        }
    }
}
