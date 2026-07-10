using System.Reflection;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor.Migrations
{
    /// <summary>
    /// Одноразовая миграция Фазы 4, пакет 3 (вики «13» §3.2): создать <see cref="AIPresetData"/> из inline
    /// AI каждого юнита-кита и назначить в <c>UnitData.AiPreset</c>. Профиль копируется глубоко (JSON),
    /// поэтому <c>Ai</c> начинает читаться из пресета БЕЗ смены значений (checksum не меняется).
    /// Идемпотентна: юнит с уже назначенным пресетом пропускается.
    /// ВЫПОЛНЕНО: пакет 3. Оставлена по правилу 0.8; не удалять.
    /// </summary>
    public static class Phase4Package3AiPresetMigration
    {
        private const string PresetDir = "Assets/_Project/ScriptableObjects/AiPresets";

        [MenuItem("Tools/Guildmaster/Migrations/Phase 4 - Package 3 (AI presets)")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(PresetDir))
                AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "AiPresets");

            FieldInfo aiField      = FindField(typeof(UnitData), "_ai");
            FieldInfo presetField  = FindField(typeof(UnitData), "_aiPreset");
            FieldInfo profileField = FindField(typeof(AIPresetData), "_profile");
            FieldInfo idField      = FindField(typeof(ContentDefinition), "_id");

            int created = 0;
            foreach (ContentDefinition def in ContentIdUtility.FindAll())
            {
                if (def is not UnitData unit) continue;
                if (presetField.GetValue(unit) != null) continue; // уже мигрирован

                var inlineAi = (AIProfile)aiField.GetValue(unit);
                if (inlineAi == null) continue;

                // Глубокая копия профиля через JSON (private [SerializeField] поля сериализуются).
                var copy = JsonUtility.FromJson<AIProfile>(JsonUtility.ToJson(inlineAi));

                var preset = ScriptableObject.CreateInstance<AIPresetData>();
                profileField.SetValue(preset, copy);
                idField.SetValue(preset, ContentDomains.MakeId(typeof(AIPresetData), unit.name));

                string path = AssetDatabase.GenerateUniqueAssetPath($"{PresetDir}/{unit.name}.asset");
                AssetDatabase.CreateAsset(preset, path);

                presetField.SetValue(unit, preset);
                EditorUtility.SetDirty(unit);
                EditorUtility.SetDirty(preset);
                created++;
                Debug.Log($"[Phase4Package3AiPresetMigration] {unit.name} → {preset.Id} ({path})");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ContentDatabaseSync.Sync(verbose: true);
            Debug.Log($"[Phase4Package3AiPresetMigration] Done: {created} AI presets created & assigned.");
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
