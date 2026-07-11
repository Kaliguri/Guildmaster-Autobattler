using System.Collections.Generic;
using System.Reflection;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor.Migrations
{
    /// <summary>
    /// Одноразовая миграция Фазы 4, пакет 2/3 (вики «13» §3.4): заполнить <c>StatsConfig.Defaults</c>
    /// базой «типичного юнита» и переписать стат-блоки реликвий как ОТЛИЧИЯ от базы (Flat-дельты),
    /// СОХРАНЯЯ текущие эффективные статы (base + delta = прежнее абсолютное). Дельта ≈ 0 → модификатор
    /// дропается (юнит наследует базу). База — стартовая, Макс тюнит вручную дальше.
    /// ВЫПОЛНЕНО: пакет 3. Оставлена по правилу 0.8; не удалять.
    /// </summary>
    public static class Phase4Package3StatsBaseMigration
    {
        // База «обычного юнита» (мой выбор; округлённая, Макс подправит). Только сквозные боевые статы —
        // ресурс/снаряд/магброня архетип-специфичны и остаются модификаторами реликвий.
        private static readonly (StatType stat, float value)[] Base =
        {
            (StatType.MaxHP,            120f),
            (StatType.AutoAttackDamage, 12f),
            (StatType.AttackSpeed,      1f),
            (StatType.AttackRange,      1.5f),
            (StatType.MoveSpeed,        3f),
            (StatType.PhysArmor,        4f),
        };

        private const float Epsilon = 1e-4f;

        [MenuItem("Tools/Guildmaster/Migrations/Phase 4 - Package 3 (StatsConfig base + relic diffs)")]
        public static void Run()
        {
            WriteStatsConfigDefaults();
            RewriteRelicsAsDiffs();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Phase4Package3StatsBaseMigration] Done: StatsConfig base written, relics rewritten as diffs.");
        }

        private static void WriteStatsConfigDefaults()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(StatsConfig)}");
            if (guids.Length == 0) { Debug.LogError("[StatsBase] StatsConfig not found"); return; }

            var cfg = AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var so = new SerializedObject(cfg);
            SerializedProperty defs = so.FindProperty("_defaults");
            defs.arraySize = Base.Length;
            for (int i = 0; i < Base.Length; i++)
            {
                SerializedProperty el = defs.GetArrayElementAtIndex(i);
                // StatType — contiguous 0..29, поэтому enumValueIndex == (int)stat.
                el.FindPropertyRelative("Stat").enumValueIndex = (int)Base[i].stat;
                el.FindPropertyRelative("Value").floatValue = Base[i].value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cfg);
        }

        private static void RewriteRelicsAsDiffs()
        {
            var baseMap = new Dictionary<StatType, float>();
            foreach (var b in Base) baseMap[b.stat] = b.value;

            FieldInfo statsField = FindField(typeof(UnitData), "_stats");

            foreach (ContentDefinition def in ContentIdUtility.FindAll())
            {
                if (def is not UnitData unit) continue;
                var mods = (StatModifier[])statsField.GetValue(unit);
                if (mods == null) continue;

                var result = new List<StatModifier>(mods.Length);
                foreach (StatModifier m in mods)
                {
                    if (m.Op == ModifierOp.Flat && baseMap.TryGetValue(m.Stat, out float bas))
                    {
                        float delta = m.Value - bas;
                        if (Mathf.Abs(delta) > Epsilon)
                            result.Add(new StatModifier(m.Stat, ModifierOp.Flat, delta));
                        // delta ≈ 0 → дропаем (наследует базу)
                    }
                    else
                    {
                        result.Add(m); // не-базовый стат или не-Flat — как есть
                    }
                }

                statsField.SetValue(unit, result.ToArray());
                EditorUtility.SetDirty(unit);
            }
        }

        private static FieldInfo FindField(System.Type type, string name)
        {
            for (System.Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (fi != null) return fi;
            }
            return null;
        }
    }
}
