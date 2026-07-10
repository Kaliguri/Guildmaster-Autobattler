using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Guildmaster.Data.Definitions;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Одно окно для всего контента (вики «13» §9): дерево по доменам (folder-per-type), поиск, справа —
    /// Odin-инспектор выбранного ассета, тулбар Create / Duplicate / Delete / Validate. Вся логика — в
    /// <see cref="ContentCrudService"/>; окно — тонкая оболочка (сменить на UITK можно без переписи логики).
    /// </summary>
    public sealed class ContentManagerWindow : OdinMenuEditorWindow
    {
        private static readonly Regex IdFormat = new Regex(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.Compiled);

        [MenuItem("Tools/Guildmaster/Content Manager")]
        private static void Open()
        {
            var window = GetWindow<ContentManagerWindow>();
            window.titleContent = new GUIContent("Content Manager");
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(900, 640);
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree(supportsMultiSelect: false);
            tree.Config.DrawSearchToolbar = true;

            foreach (System.Type type in ContentPaths.CreatableTypes)
            {
                string folder = ContentPaths.FolderFor(type);
                if (AssetDatabase.IsValidFolder(folder))
                    tree.AddAllAssetsAtPath(type.Name.Replace("Data", ""), folder, type, includeSubDirectories: true);
            }
            return tree;
        }

        private ContentDefinition Selected => MenuTree?.Selection?.SelectedValue as ContentDefinition;

        protected override void OnBeginDrawEditors()
        {
            SirenixEditorGUI.BeginHorizontalToolbar();
            {
                if (SirenixEditorGUI.ToolbarButton(new GUIContent("Create")))
                    ShowCreateMenu();

                using (new EditorGUI.DisabledScope(Selected == null))
                {
                    if (SirenixEditorGUI.ToolbarButton(new GUIContent("Duplicate")))
                        DuplicateSelected();
                    if (SirenixEditorGUI.ToolbarButton(new GUIContent("Delete")))
                        DeleteSelected();
                    if (SirenixEditorGUI.ToolbarButton(new GUIContent("Validate")))
                        ValidateSelected();
                }

                GUILayout.FlexibleSpace();
                if (SirenixEditorGUI.ToolbarButton(new GUIContent("Sync DB")))
                    ContentDatabaseSync.Sync(verbose: true);
            }
            SirenixEditorGUI.EndHorizontalToolbar();
        }

        private void ShowCreateMenu()
        {
            var menu = new GenericMenu();
            foreach (System.Type type in ContentPaths.CreatableTypes)
            {
                System.Type captured = type;
                menu.AddItem(new GUIContent(type.Name.Replace("Data", "")), false, () =>
                {
                    ContentDefinition asset = ContentCrudService.Create(captured);
                    ForceMenuTreeRebuild();
                    TrySelectMenuItemWithObject(asset);
                    EditorGUIUtility.PingObject(asset);
                });
            }
            menu.ShowAsContext();
        }

        private void DuplicateSelected()
        {
            ContentDefinition copy = ContentCrudService.Duplicate(Selected);
            if (copy == null) return;
            ForceMenuTreeRebuild();
            TrySelectMenuItemWithObject(copy);
        }

        private void DeleteSelected()
        {
            ContentDefinition def = Selected;
            if (def == null) return;

            List<string> usages = ContentCrudService.FindUsages(def);
            if (usages.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Нельзя удалить: есть использования",
                    $"'{def.Id}' используется в {usages.Count} ассет(ах/сценах):\n\n" +
                    string.Join("\n", usages.Take(15)) + (usages.Count > 15 ? "\n…" : ""),
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Удалить контент?",
                    $"Удалить '{def.Id}' ({AssetDatabase.GetAssetPath(def)})?", "Удалить", "Отмена"))
                return;

            ContentCrudService.TryDelete(def, out _);
            ForceMenuTreeRebuild();
        }

        private void ValidateSelected()
        {
            ContentDefinition def = Selected;
            if (def == null) return;

            var issues = new StringBuilder();

            if (string.IsNullOrEmpty(def.Id)) issues.AppendLine("• id пуст");
            else if (!IdFormat.IsMatch(def.Id)) issues.AppendLine($"• id '{def.Id}' не формата domain.name");
            else
            {
                string domain = ContentDomains.GetDomain(def.GetType());
                if (!def.Id.StartsWith(domain + ".")) issues.AppendLine($"• домен id не соответствует типу (ожидался '{domain}.')");
            }

            foreach (ContentDefinition other in ContentIdUtility.FindAll())
                if (other != def && other.Id == def.Id) { issues.AppendLine($"• дубликат id с {AssetDatabase.GetAssetPath(other)}"); break; }

            if (def is EffectData effect && effect.Components != null)
                for (int i = 0; i < effect.Components.Length; i++)
                    if (effect.Components[i] == null) issues.AppendLine($"• null-компонент [{i}] (missing type)");

            EditorUtility.DisplayDialog(
                issues.Length == 0 ? "Валидно" : "Проблемы валидации",
                issues.Length == 0 ? $"'{def.Id}' — без замечаний." : $"'{def.Id}':\n\n{issues}",
                "OK");
        }
    }
}
