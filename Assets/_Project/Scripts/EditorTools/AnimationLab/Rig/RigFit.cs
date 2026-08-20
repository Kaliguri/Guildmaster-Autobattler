#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Переставляет узлы вращения В точки, которые задал арт: сустав переезжает туда, где стоит пивот
    /// висящего на нём куска, а всё остальное остаётся ровно там, где стояло.
    ///
    /// Существует потому, что сборка руками — это самая точная информация о том, где на рисунке сустав,
    /// и терять её нельзя. Автор двигает кусок, пока не станет похоже на человека; инструмент забирает
    /// результат и делает его геометрией рига. Обратный порядок — «рисуй под наши координаты» — платится
    /// правкой на каждой итерации арта.
    /// </summary>
    /// <remarks>
    /// Двигать узлы безопасно ровно потому, что клипы рига хранят УГЛЫ: по всем клипам 10–15 кривых
    /// <c>localEulerAnglesRaw</c> и ровно одна кривая позиции (таз). Поедет геометрия поз, а не
    /// авторская работа — но поедет, поэтому дуги ударов после прогона пересматриваются
    /// <see cref="RigSweep"/>, а шаг — <see cref="RigStride"/>.
    /// </remarks>
    public static class RigFit
    {
        /// <summary>Куски, чьи пивоты дальше этого друг от друга, целятся в РАЗНЫЕ суставы.</summary>
        public const float DefaultClusterPixels = 12f;

        public sealed class Move
        {
            public string JointId;
            public Vector3 From, To;
            public float DistancePixels;
            public List<string> Sprites = new List<string>();

            public override string ToString() =>
                $"{JointId}: {DistancePixels:F0} px -> [{string.Join(", ", Sprites)}]";
        }

        /// <summary>
        /// Кусок, чей пивот далеко от того места, куда целится его сустав: он висит на суставе, но
        /// вращаться должен вокруг своей точки. Это не ошибка Fit'а, а заявка на НОВЫЙ узел вращения —
        /// у кисти, например, своего сустава в риге нет вовсе.
        /// </summary>
        public sealed class Orphan
        {
            public string JointId, Sprite;
            public float OffsetPixels;
            public override string ToString() => $"{Sprite} висит на {JointId}, но его пивот в {OffsetPixels:F0} px — " +
                                                 "кандидат на собственный узел вращения";
        }

        public sealed class Report
        {
            public bool Applied;

            /// <summary>Работали с открытым prefab stage — правки живут в редакторе и ждут его сохранения.</summary>
            public bool InStage;
            public readonly List<Move> Moves = new List<Move>();
            public readonly List<Orphan> Orphans = new List<Orphan>();
            public readonly List<string> Skipped = new List<string>();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine((Applied ? "Fit Rig To Art: применено" : "Fit Rig To Art: прогон вхолостую, ничего не изменено")
                              + (InStage ? " (в открытом prefab stage — сохранение за редактором)" : ""));
                foreach (var m in Moves) sb.AppendLine("  переезд  " + m);
                foreach (var o in Orphans) sb.AppendLine("  заявка   " + o);
                foreach (var s in Skipped) sb.AppendLine("  пропуск  " + s);
                if (Moves.Count == 0) sb.AppendLine("  двигать нечего: все объявленные пивоты уже на своих суставах");
                return sb.ToString();
            }
        }

        public static Report Run(string prefabPath, bool dryRun, float clusterPixels = DefaultClusterPixels)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) throw new System.ArgumentException("Префаб не найден: " + prefabPath);

            var profile = RigProbe.FindProfileFor(asset);
            if (profile == null)
                throw new System.ArgumentException($"Нет RigProfile ни для {asset.name}, ни для префабов, вариантом которых он является.");

            var report = new Report { Applied = !dryRun };

            // Открытый prefab stage держит правки В ПАМЯТИ редактора: прочитать префаб с диска значит
            // судить вчерашнюю сборку, а сохранить поверх — стереть то, что автор двигает прямо сейчас.
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            bool useStage = stage != null && stage.assetPath == prefabPath;
            var go = useStage ? stage.prefabContentsRoot : PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // Сверху вниз: перемещение родителя тащит потомков, и хотя мы их возвращаем на место,
                // считать цель ребёнка надо уже по устоявшейся позиции родителя.
                var joints = new List<RigProfile.Joint>(profile.Joints);
                joints.Sort((a, b) => Depth(a.Path).CompareTo(Depth(b.Path)));

                foreach (var joint in joints)
                {
                    var node = go.transform.Find(joint.Path);
                    if (node == null) continue;

                    var anchors = RigAnchors.Collect(go.transform, profile);
                    var declared = new List<RigAnchors.Anchor>();
                    foreach (var a in anchors)
                        if (a.JointId == joint.Id && a.DeclaresPivot) declared.Add(a);
                    if (declared.Count == 0) continue;

                    // Цель задают куски СВОЕЙ кости, а не ближайшие: кисть висит на предплечье за
                    // неимением своего узла и однажды оказалась к локтю ближе, чем само предплечье
                    // (27 px против 30) — «ближайший побеждает» тогда увёл локоть в запястье.
                    var owners = new List<RigAnchors.Anchor>();
                    foreach (var a in declared)
                        if (a.BelongsToBone) owners.Add(a);
                    if (owners.Count == 0) owners = declared;

                    RigAnchors.Anchor nearest = owners[0];
                    foreach (var a in owners)
                        if (a.Offset < nearest.Offset) nearest = a;

                    float ppu = nearest.Visual.sprite != null ? nearest.Visual.sprite.pixelsPerUnit : 1000f;
                    float clusterWorld = clusterPixels / ppu;

                    var cluster = new List<RigAnchors.Anchor>();
                    foreach (var a in declared)
                    {
                        if (Vector3.Distance(a.PivotWorld, nearest.PivotWorld) <= clusterWorld) cluster.Add(a);
                        else report.Orphans.Add(new Orphan
                        {
                            JointId = joint.Id,
                            Sprite = a.SpriteName,
                            OffsetPixels = Vector3.Distance(a.PivotWorld, nearest.PivotWorld) * ppu,
                        });
                    }

                    var target = Vector3.zero;
                    foreach (var a in cluster) target += a.PivotWorld;
                    target /= cluster.Count;
                    target.z = node.position.z;

                    float distance = Vector3.Distance(target, node.position) * ppu;
                    if (distance < 0.5f)
                    {
                        report.Skipped.Add($"{joint.Id}: уже на месте");
                        continue;
                    }

                    var move = new Move { JointId = joint.Id, From = node.position, To = target, DistancePixels = distance };
                    foreach (var a in cluster) move.Sprites.Add(a.SpriteName);
                    report.Moves.Add(move);

                    if (!dryRun) MoveJointKeepingChildren(node, target);
                }

                if (!dryRun)
                {
                    if (useStage) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.scene);
                    else PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                }
            }
            finally { if (!useStage) PrefabUtility.UnloadPrefabContents(go); }

            report.InStage = useStage;
            return report;
        }

        /// <summary>
        /// Двигает узел, оставляя всё, что на нём висит, на прежнем месте в мире.
        ///
        /// Без возврата детей картинка уезжала бы ровно на тот вектор, на который переехал сустав — то
        /// есть инструмент чинил бы разрыв, создавая его заново на уровень ниже.
        /// </summary>
        static void MoveJointKeepingChildren(Transform node, Vector3 target)
        {
            int count = node.childCount;
            var pos = new Vector3[count];
            var rot = new Quaternion[count];
            var children = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                children[i] = node.GetChild(i);
                pos[i] = children[i].position;
                rot[i] = children[i].rotation;
            }

            node.position = target;

            for (int i = 0; i < count; i++)
                children[i].SetPositionAndRotation(pos[i], rot[i]);
        }

        static int Depth(string path)
        {
            int depth = 0;
            foreach (char c in path) if (c == '/') depth++;
            return depth;
        }

        [MenuItem("Alebardium/Animation/Fit Rig To Art (dry run)", priority = 627)]
        static void DryRunSelected() => RunSelected(dryRun: true);

        [MenuItem("Alebardium/Animation/Fit Rig To Art (apply)", priority = 628)]
        static void ApplySelected()
        {
            if (!EditorUtility.DisplayDialog("Fit Rig To Art",
                    "Узлы вращения переедут в точки, заданные пивотами арта. Картинка не изменится, " +
                    "но геометрия рига станет другой — позы клипов поедут.\n\nПрогнать вхолостую сначала?",
                    "Применить", "Отмена"))
                return;
            RunSelected(dryRun: false);
        }

        [MenuItem("Alebardium/Animation/Fit Rig To Art (dry run)", validate = true)]
        [MenuItem("Alebardium/Animation/Fit Rig To Art (apply)", validate = true)]
        static bool RunSelectedValidate() =>
            Selection.activeObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go);

        static void RunSelected(bool dryRun)
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null) { Debug.LogError("Fit Rig To Art: выдели префаб рига."); return; }

            var path = AssetDatabase.GetAssetPath(prefab);
            var report = Run(path, dryRun);
            // Построчно: консоль через MCP отдаёт только первую строку многострочной записи.
            foreach (var line in report.ToString().Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) Debug.Log(line.TrimEnd());
        }
    }
}
#endif
