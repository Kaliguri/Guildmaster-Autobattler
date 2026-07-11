using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Odin-инспектор для всех <see cref="ContentDefinition"/>-наследников: id-drawer (read-only + Edit Id,
    /// вики «13» §2.3), пикеры полиморфных <c>[SerializeReference]</c>-полей (напр. компоненты
    /// <see cref="EffectData"/>) и блок локализации (вики «13» §6) — поля Name/Desc по локалям, читающие/
    /// пишущие таблицу <c>Content</c> по ключам <c>{id}.name</c>/<c>{id}.desc</c>, и «Create missing keys».
    /// Табличная логика — в <see cref="ContentLocalization"/>; тут только GUI.
    /// </summary>
    [CustomEditor(typeof(ContentDefinition), editorForChildClasses: true)]
    public sealed class ContentDefinitionEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (target is ContentDefinition def) DrawLocalization(def);
        }

        private static void DrawLocalization(ContentDefinition def)
        {
            IReadOnlyList<string> suffixes = ContentLocalization.RequiredSuffixes(def);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Localization — Content", EditorStyles.boldLabel);

            if (ContentLocalization.Collection == null)
            {
                EditorGUILayout.HelpBox("String Table 'Content' not found. Run localization setup.", MessageType.Warning);
                return;
            }
            if (suffixes.Count == 0)
            {
                EditorGUILayout.LabelField($"{def.GetType().Name} is not localized.", EditorStyles.miniLabel);
                return;
            }
            if (string.IsNullOrEmpty(def.Id))
            {
                EditorGUILayout.HelpBox("Assign an id first — localization keys derive from it.", MessageType.Info);
                return;
            }

            var locales = LocalizationEditorSettings.GetLocales();
            foreach (string suffix in suffixes)
            {
                string key = ContentLocalization.KeyFor(def, suffix);
                EditorGUILayout.LabelField(key, EditorStyles.miniBoldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var locale in locales)
                    {
                        string code = locale.Identifier.Code;
                        EditorGUI.BeginChangeCheck();
                        string current = ContentLocalization.GetValue(code, key) ?? string.Empty;
                        string next = EditorGUILayout.TextField(code, current);
                        if (EditorGUI.EndChangeCheck())
                        {
                            ContentLocalization.SetValue(code, key, next);
                            AssetDatabase.SaveAssets();
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            IReadOnlyList<string> missing = ContentLocalization.MissingKeys(def);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(missing.Count == 0))
                    if (GUILayout.Button($"Create missing keys — asset ({missing.Count})"))
                        ContentLocalization.CreateMissingKeys(def);

                if (GUILayout.Button("Create missing keys — domain"))
                {
                    int created = ContentLocalization.CreateMissingKeysForDomain(def.GetType());
                    Debug.Log($"[Content] Created {created} missing localization key(s) for domain '{ContentDomains.GetDomain(def.GetType())}'.");
                }
            }
        }
    }
}
