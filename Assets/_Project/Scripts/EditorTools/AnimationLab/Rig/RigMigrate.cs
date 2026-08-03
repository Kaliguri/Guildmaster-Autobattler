#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Renames rig nodes and carries every path-shaped reference along with them: animation clip
    /// bindings, avatar mask entries and the generic avatar.
    ///
    /// None of those references complain when they go stale. A mask entry pointing nowhere is ignored,
    /// and a curve whose path no longer resolves simply stops animating — which is how four attack
    /// clips lost their sword after one rename in the Hierarchy, without a single console line.
    /// </summary>
    public static class RigMigrate
    {
        /// <summary>One node to rename, addressed by the path it has BEFORE the run.</summary>
        public struct Rename
        {
            public string NodePath;
            public string NewName;

            public Rename(string nodePath, string newName) { NodePath = nodePath; NewName = newName; }
        }

        /// <summary>
        /// A path mapping for references that went stale BEFORE this run — a node renamed by hand
        /// leaves clips pointing at a path the prefab can no longer explain, so the prefab cannot
        /// supply the mapping and it has to be stated.
        /// </summary>
        public struct Alias
        {
            public string From;
            public string To;

            public Alias(string from, string to) { From = from; To = to; }
        }

        public sealed class Options
        {
            public string PrefabPath;
            public List<Rename> Renames = new List<Rename>();
            public List<Alias> Aliases = new List<Alias>();
            /// <summary>Folders scanned for clips that animate this rig.</summary>
            public string[] ClipFolders = new string[0];
            public string[] MaskPaths = new string[0];
            /// <summary>Generic avatar to rebuild: it describes the hierarchy, so a rename invalidates it.</summary>
            public string AvatarPath;
            /// <summary>Report what would change without writing anything.</summary>
            public bool DryRun;

            /// <summary>
            /// Произвольная перестройка дерева, применяемая ПОСЛЕ переименований: перевесить узел,
            /// схлопнуть пару, удалить опустевший контейнер.
            ///
            /// Живёт здесь, а не в отдельном инструменте, потому что перенос путей строится сравнением
            /// «дерево до» и «дерево после» — и он одинаков для переименования и для перестройки. Второй
            /// конвейер миграции означал бы два владельца одного правила, и разошлись бы они молча:
            /// клипы починил бы один, маски — другой.
            /// </summary>
            public System.Action<Transform> Restructure;
        }

        public sealed class Report
        {
            public readonly List<string> Lines = new List<string>();
            public int NodesRenamed;
            public int ClipsChanged;
            public int BindingsMoved;
            public int MaskEntriesMoved;
            public int StaleBefore;
            public int StaleAfter;
            public bool AvatarRebuilt;

            public override string ToString()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"nodes renamed: {NodesRenamed}   clips changed: {ClipsChanged}   bindings moved: {BindingsMoved}");
                sb.AppendLine($"mask entries moved: {MaskEntriesMoved}   avatar rebuilt: {AvatarRebuilt}");
                sb.AppendLine($"stale paths: {StaleBefore} before -> {StaleAfter} after");
                foreach (var line in Lines) sb.AppendLine("  " + line);
                return sb.ToString();
            }
        }

        /// <summary>Renames the nodes and repoints clips, masks and the avatar in one pass.</summary>
        public static Report Run(Options options)
        {
            if (options == null) throw new System.ArgumentNullException(nameof(options));
            var report = new Report();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(options.PrefabPath);
            if (prefab == null)
                throw new System.ArgumentException($"No prefab at '{options.PrefabPath}'.");

            var clips = LoadClips(options.ClipFolders);
            var masks = LoadMasks(options.MaskPaths);
            report.StaleBefore = CountStalePaths(prefab, clips, masks, report.Lines, "stale before: ");

            var map = BuildRenameMap(options, report);
            foreach (var alias in options.Aliases)
            {
                if (string.IsNullOrEmpty(alias.From)) continue;
                map[alias.From] = alias.To;
            }

            if (map.Count == 0)
            {
                report.Lines.Add("nothing to remap");
                return report;
            }

            foreach (var clip in clips) MoveClipBindings(clip, map, report, options.DryRun);
            foreach (var mask in masks) MoveMaskEntries(mask, map, report, options.DryRun);

            if (!options.DryRun && !string.IsNullOrEmpty(options.AvatarPath))
                report.AvatarRebuilt = RebuildAvatar(options.PrefabPath, options.AvatarPath, report);

            if (!options.DryRun)
            {
                AssetDatabase.Refresh();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(options.PrefabPath);
            }
            report.StaleAfter = CountStalePaths(prefab, clips, masks, report.Lines, "stale after: ");
            return report;
        }

        /// <summary>
        /// Applies the renames to the prefab and returns the resulting old-path -> new-path mapping
        /// for the whole hierarchy, so a renamed parent carries its entire subtree.
        /// </summary>
        static Dictionary<string, string> BuildRenameMap(Options options, Report report)
        {
            var map = new Dictionary<string, string>();
            if (options.Renames.Count == 0 && options.Restructure == null) return map;

            var contents = PrefabUtility.LoadPrefabContents(options.PrefabPath);
            try
            {
                var root = contents.transform;

                // Resolve every target first: a rename higher up would invalidate the paths below it.
                var targets = new List<(Transform node, string newName)>(options.Renames.Count);
                foreach (var rename in options.Renames)
                {
                    var node = root.Find(rename.NodePath);
                    if (node == null)
                    {
                        report.Lines.Add($"rename skipped, node not found: {rename.NodePath}");
                        continue;
                    }
                    targets.Add((node, rename.NewName));
                }

                var before = MapPaths(root);
                foreach (var (node, newName) in targets)
                {
                    if (node.name == newName) continue;
                    node.name = newName;
                    report.NodesRenamed++;
                }
                int restructured = 0;
                if (options.Restructure != null)
                {
                    options.Restructure(root);
                    restructured = 1;
                }

                var after = MapPaths(root);

                foreach (var pair in before)
                {
                    // Узел, которого в дереве больше нет, карту не пополняет: его путь мёртв, и
                    // переносить привязки некуда — это ловит счётчик stale after.
                    if (!after.TryGetValue(pair.Key, out string newPath)) continue;
                    if (newPath != pair.Value) map[pair.Value] = newPath;
                }

                if (!options.DryRun && (report.NodesRenamed > 0 || restructured > 0))
                    PrefabUtility.SaveAsPrefabAsset(contents, options.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return map;
        }

        static Dictionary<Transform, string> MapPaths(Transform root)
        {
            var paths = new Dictionary<Transform, string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t == root) continue;
                paths[t] = AnimationUtility.CalculateTransformPath(t, root);
            }
            return paths;
        }

        /// <summary>Exact match first, then the deepest renamed ancestor.</summary>
        public static string Remap(string path, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (map.TryGetValue(path, out string exact)) return exact;

            string bestFrom = null, bestTo = null;
            foreach (var pair in map)
            {
                if (path.Length <= pair.Key.Length) continue;
                if (!path.StartsWith(pair.Key, System.StringComparison.Ordinal)) continue;
                if (path[pair.Key.Length] != '/') continue;
                if (bestFrom != null && pair.Key.Length <= bestFrom.Length) continue;
                bestFrom = pair.Key;
                bestTo = pair.Value;
            }
            return bestFrom == null ? path : bestTo + path.Substring(bestFrom.Length);
        }

        static void MoveClipBindings(AnimationClip clip, Dictionary<string, string> map, Report report, bool dryRun)
        {
            var moves = new List<(EditorCurveBinding from, EditorCurveBinding to, AnimationCurve curve)>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                string newPath = Remap(binding.path, map);
                if (newPath == binding.path) continue;
                var moved = binding;
                moved.path = newPath;
                moves.Add((binding, moved, AnimationUtility.GetEditorCurve(clip, binding)));
            }

            var objectMoves = new List<(EditorCurveBinding from, EditorCurveBinding to, ObjectReferenceKeyframe[] keys)>();
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                string newPath = Remap(binding.path, map);
                if (newPath == binding.path) continue;
                var moved = binding;
                moved.path = newPath;
                objectMoves.Add((binding, moved, AnimationUtility.GetObjectReferenceCurve(clip, binding)));
            }

            int total = moves.Count + objectMoves.Count;
            if (total == 0) return;

            report.ClipsChanged++;
            report.BindingsMoved += total;
            report.Lines.Add($"{clip.name}: {total} bindings moved");
            if (dryRun) return;

            // Clear every old binding before writing the new ones: a rename can map two paths onto
            // one another, and interleaving the two halves would drop a curve.
            foreach (var move in moves) AnimationUtility.SetEditorCurve(clip, move.from, null);
            foreach (var move in objectMoves) AnimationUtility.SetObjectReferenceCurve(clip, move.from, null);
            foreach (var move in moves) AnimationUtility.SetEditorCurve(clip, move.to, move.curve);
            foreach (var move in objectMoves) AnimationUtility.SetObjectReferenceCurve(clip, move.to, move.keys);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
        }

        static void MoveMaskEntries(AvatarMask mask, Dictionary<string, string> map, Report report, bool dryRun)
        {
            int moved = 0;
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                string newPath = Remap(path, map);
                if (newPath == path) continue;
                if (!dryRun) mask.SetTransformPath(i, newPath);
                moved++;
            }
            if (moved == 0) return;

            report.MaskEntriesMoved += moved;
            report.Lines.Add($"{mask.name}: {moved} entries moved");
            if (dryRun) return;

            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssetIfDirty(mask);
        }

        /// <summary>
        /// Rebuilds the generic avatar from the current hierarchy. Masks on a generic rig are silently
        /// inert without a valid avatar, so a rename that skips this step disables every layer above 0.
        /// </summary>
        static bool RebuildAvatar(string prefabPath, string avatarPath, Report report)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var built = AvatarBuilder.BuildGenericAvatar(contents, "");
                if (built == null || !built.isValid)
                {
                    report.Lines.Add("avatar rebuild FAILED — built avatar is not valid");
                    if (built != null) Object.DestroyImmediate(built);
                    return false;
                }

                var existing = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(built, avatarPath);
                    report.Lines.Add($"avatar created at {avatarPath}");
                    return true;
                }

                // CopySerialized carries the name across too, and an avatar that loses its name stops
                // being found by the importer — keep it by hand.
                string keepName = existing.name;
                EditorUtility.CopySerialized(built, existing);
                existing.name = keepName;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                Object.DestroyImmediate(built);
                report.Lines.Add($"avatar rebuilt in place ({avatarPath})");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Clip bindings and mask entries whose path no longer resolves against the rig. Unity reports
        /// none of these, so this is the only way to notice.
        /// </summary>
        public static int CountStalePaths(GameObject prefab, IList<AnimationClip> clips, IList<AvatarMask> masks,
                                          List<string> details = null, string detailPrefix = "")
        {
            int stale = 0;
            var root = prefab.transform;

            foreach (var clip in clips)
            {
                int clipStale = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path.Length == 0) continue;
                    if (root.Find(binding.path) == null) clipStale++;
                }
                if (clipStale == 0) continue;
                stale += clipStale;
                details?.Add($"{detailPrefix}{clip.name} has {clipStale} bindings pointing nowhere");
            }

            foreach (var mask in masks)
            {
                int maskStale = 0;
                for (int i = 0; i < mask.transformCount; i++)
                {
                    string path = mask.GetTransformPath(i);
                    if (path.Length == 0) continue;
                    if (root.Find(path) == null) maskStale++;
                }
                if (maskStale == 0) continue;
                stale += maskStale;
                details?.Add($"{detailPrefix}{mask.name} has {maskStale} entries pointing nowhere");
            }

            return stale;
        }

        public static List<AnimationClip> LoadClips(string[] folders)
        {
            var clips = new List<AnimationClip>();
            if (folders == null || folders.Length == 0) return clips;
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", folders))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null) clips.Add(clip);
            }
            return clips;
        }

        public static List<AvatarMask> LoadMasks(string[] paths)
        {
            var masks = new List<AvatarMask>();
            if (paths == null) return masks;
            foreach (string path in paths)
            {
                var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
                if (mask != null) masks.Add(mask);
            }
            return masks;
        }
    }
}
#endif
