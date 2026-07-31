#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// What a rig is, in terms an animation tool can address: which node is which joint, where each
    /// bone points, and how the thing in the hand is calibrated.
    ///
    /// This is a MEASUREMENT of the prefab, not a second copy of it. The pose belongs to the prefab and
    /// the clips; everything here is either read back from the hierarchy (and refreshed by rebuilding)
    /// or a convention that cannot be measured — a flex sign, a joint limit. Those two kinds are kept
    /// apart on purpose: a rebuild overwrites the measured fields and leaves the authored ones alone.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Animation/Rig Profile", fileName = "RigProfile")]
    public sealed class RigProfile : ScriptableObject
    {
        /// <summary>A joint the tool may rotate, addressed by logical id instead of by path.</summary>
        [System.Serializable]
        public sealed class Joint
        {
            [Tooltip("Логический id сустава: то, что стоит в скобках имени узла, плюс сторона. Например shoulder.R.")]
            public string Id;

            [Tooltip("Путь узла в иерархии рига. Замеряется, руками не правится.")]
            public string Path;

            [Tooltip("Локальный угол в позе покоя (поза префаба). Все команды тула считаются от него.")]
            public float RestZ;

            [Tooltip("Куда смотрит кость в ЛОКАЛЬНЫХ координатах узла, в градусах. -90 = вниз по локальной оси. " +
                     "Замеряется по положению следующего сустава или дальнего конца спрайта.")]
            public float BoneAxisLocal;

            [Tooltip("Длина кости в мировых юнитах — от сустава до его конца. Нужна валидатору отрыва.")]
            public float BoneLength;

            [Tooltip("Знак, при котором сустав СГИБАЕТСЯ (а не разгибается). Это анатомическая конвенция, " +
                     "а не измерение: колено гнётся в -Z, бедро идёт вперёд в +Z. Правится руками, " +
                     "пересборка профиля его не затирает.")]
            public int FlexSign = 1;

            [Tooltip("Предел сгиба от позы покоя, в градусах. 0 = предел не задан. У колена это предел АРТА " +
                     "(60 градусов): дальше прямоугольные сегменты расходятся щелью.")]
            public float FlexLimit;
        }

        /// <summary>An item held by a grip node — a weapon, a shield.</summary>
        [System.Serializable]
        public sealed class HeldItem
        {
            [Tooltip("Логический id предмета: weapon, shield.")]
            public string Id;

            [Tooltip("Путь узла вращения (хвата). Именно он анимируется.")]
            public string GripPath;

            [Tooltip("Путь спрайтового узла предмета. Он несёт калибровочный оффсет и НЕ анимируется.")]
            public string ItemPath;

            [Tooltip("Как называется ориентир предмета: blade — вдоль клинка, top — верх щита. " +
                     "У плоского 2D-щита лицо всегда обращено к камере, поэтому целимся его верхом: " +
                     "именно он задаёт наклон.")]
            public string OrientationName = "blade";

            [Tooltip("Направление ориентира в ЛОКАЛЬНЫХ координатах спрайта предмета, в градусах. " +
                     "90 = вдоль локальной +Y (длинная ось вертикального спрайта).")]
            public float OrientationLocal = 90f;

            [Tooltip("Калибровочный оффсет: локальный угол спрайтового узла. Он существует ровно для того, " +
                     "чтобы ноль узла вращения означал что-то осмысленное.")]
            public float CalibrationZ;

            [Tooltip("Расстояние от точки вращения до торца рукояти, в мировых юнитах. Замер.")]
            public float GripToButt;

            [Tooltip("Расстояние от точки вращения до дальнего конца родительской кости (где кисть). Замер.")]
            public float GripToBoneEnd;

            [Tooltip("Длина предмета вдоль ориентира, в мировых юнитах. Замер.")]
            public float ItemLength;

            /// <summary>
            /// The item's transform, reached from its grip. Addressed by the slice of
            /// <see cref="ItemPath"/> below <see cref="GripPath"/>, because the artwork sits inside a
            /// visual container and is no longer a direct child of the grip.
            /// </summary>
            public Transform Resolve(Transform grip)
            {
                if (grip == null || string.IsNullOrEmpty(ItemPath)) return null;
                if (string.IsNullOrEmpty(GripPath) || !ItemPath.StartsWith(GripPath + "/", System.StringComparison.Ordinal))
                    return grip.Find(System.IO.Path.GetFileName(ItemPath));
                return grip.Find(ItemPath.Substring(GripPath.Length + 1));
            }
        }

        [Tooltip("Риг, который описывает этот профиль. Источник всех замеров.")]
        public GameObject Rig;

        [Tooltip("Суставы рига. Пересобираются кнопкой из иерархии.")]
        public List<Joint> Joints = new List<Joint>();

        [Tooltip("Предметы в руках. Пересобираются кнопкой из иерархии.")]
        public List<HeldItem> Held = new List<HeldItem>();

        public Joint FindJoint(string id)
        {
            foreach (var joint in Joints)
                if (joint.Id == id) return joint;
            return null;
        }

        public HeldItem FindHeld(string id)
        {
            foreach (var item in Held)
                if (item.Id == id) return item;
            return null;
        }
    }

    /// <summary>
    /// Reads a rig and fills a <see cref="RigProfile"/> from it. Every geometric field comes from the
    /// hierarchy, so the profile cannot drift from the prefab without someone noticing on the next
    /// rebuild; the two authored fields (flex sign, flex limit) are carried over instead of reset.
    /// </summary>
    public static class RigProfileBuilder
    {
        /// <summary>Nodes named like "Rotation Point (Elbow)" are joints; the word in brackets is the id.</summary>
        public const string JointPrefix = Presentation.Body.RigNaming.JointPrefix;

        /// <summary>
        /// Bones that carry the body itself rather than a limb, so they never got a "Rotation Point" name:
        /// the pelvis (vertical bob), the spine (lean, and it drags the head and both arms with it) and the
        /// head (its own lag). Without them a clip has no weight shift at all — the unit slides.
        /// </summary>
        static readonly string[] BodyBones = { "Hips", "Torso", "Head" };

        /// <summary>Rebuilds the profile in place and returns a human-readable summary of what it measured.</summary>
        public static string Build(RigProfile profile)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (profile.Rig == null) throw new System.ArgumentException("RigProfile.Rig is not set.");

            string prefabPath = AssetDatabase.GetAssetPath(profile.Rig);
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var root = contents.transform;
                var authored = new Dictionary<string, (int sign, float limit)>();
                foreach (var joint in profile.Joints)
                    authored[joint.Id] = (joint.FlexSign, joint.FlexLimit);

                var joints = new List<RigProfile.Joint>();
                var held = new List<RigProfile.HeldItem>();

                foreach (var node in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (node == root) continue;
                    // Artwork is off limits: a sprite node inside "Visual Part (Head)" is also called
                    // "Head", and reading it as a joint produced a second, bogus 'head' entry.
                    if (RigVisualParts.IsUnderContainer(node)) continue;
                    bool isBodyBone = System.Array.IndexOf(BodyBones, node.name) >= 0;
                    if (!node.name.StartsWith(JointPrefix, System.StringComparison.Ordinal) && !isBodyBone) continue;

                    string label = isBodyBone ? node.name : ExtractLabel(node.name);
                    string id = label.ToLowerInvariant() + (isBodyBone ? "" : SideSuffix(node, root));
                    string path = AnimationUtility.CalculateTransformPath(node, root);

                    if (label == "Grip")
                    {
                        var item = MeasureHeld(node, root, id, path);
                        if (item != null) held.Add(item);
                        // The grip is also a joint: it is the node the tool writes angles to.
                    }

                    var measured = MeasureJoint(node, root, id, path);
                    if (authored.TryGetValue(id, out var carry))
                    {
                        measured.FlexSign = carry.sign;
                        measured.FlexLimit = carry.limit;
                    }
                    joints.Add(measured);
                }

                profile.Joints = joints;
                profile.Held = held;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);

                return Describe(profile);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static RigProfile.Joint MeasureJoint(Transform node, Transform root, string id, string path)
        {
            var joint = new RigProfile.Joint { Id = id, Path = path, RestZ = NormalizeAngle(node.localEulerAngles.z) };

            if (TryFindBoneEnd(node, out Vector3 end))
            {
                var local = node.InverseTransformPoint(end);
                joint.BoneAxisLocal = NormalizeAngle(Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg);
                joint.BoneLength = Vector3.Distance(node.position, end);
            }
            return joint;
        }

        /// <summary>
        /// The far end of the bone this joint drives: the nearest joint down the chain, or — when there
        /// is none — the farthest sprite edge that still belongs to this bone.
        ///
        /// A grip never counts as the end of a bone. It is an attachment point inside the hand, and on
        /// this rig it sits at the forearm's pivot, so treating it as the chain's next joint measured
        /// the elbow bone as 0.0024 units long.
        /// </summary>
        static bool TryFindBoneEnd(Transform node, out Vector3 end)
        {
            Transform next = null;
            float nearest = float.MaxValue;
            foreach (var child in node.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child == node || !IsJoint(child)) continue;
                if (ExtractLabel(child.name) == "Grip") continue;
                float distance = Vector3.Distance(child.position, node.position);
                if (distance < 1e-4f || distance >= nearest) continue;
                nearest = distance;
                next = child;
            }
            if (next != null)
            {
                end = next.position;
                return true;
            }

            // Sprites pivot at their centre, so the far EDGE is where the next limb would attach.
            float farthest = 0f;
            end = node.position;
            foreach (var renderer in node.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
            {
                if (renderer.sprite == null) continue;
                if (BelongsToDeeperJoint(node, renderer.transform)) continue;
                float half = renderer.sprite.bounds.extents.y;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var edge = renderer.transform.TransformPoint(new Vector3(0f, sign * half, 0f));
                    float distance = Vector3.Distance(edge, node.position);
                    if (distance <= farthest) continue;
                    farthest = distance;
                    end = edge;
                }
            }
            return farthest > 1e-4f;
        }

        static bool IsJoint(Transform node) =>
            node.name.StartsWith(JointPrefix, System.StringComparison.Ordinal);

        /// <summary>A sprite below another joint belongs to that joint's bone, not to this one.</summary>
        static bool BelongsToDeeperJoint(Transform joint, Transform sprite)
        {
            for (var t = sprite; t != null && t != joint; t = t.parent)
                if (IsJoint(t)) return true;
            return false;
        }

        static RigProfile.HeldItem MeasureHeld(Transform grip, Transform root, string gripId, string gripPath)
        {
            var renderer = grip.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return null;

            var item = renderer.transform;
            float half = renderer.sprite.bounds.extents.y;
            var butt = item.TransformPoint(new Vector3(0f, -half, 0f));
            var tip = item.TransformPoint(new Vector3(0f, half, 0f));

            // Тип предмета объявляет метка на его кости, а не имя спрайта. Прежняя эвристика
            // (`sprite.name.Contains("shield")`) держалась на том, что предметов ровно два и оба названы
            // по-английски: факел, баклер или лук она бы молча записала в оружие, а рецепты анимации целятся
            // по этому id (Aim("weapon")).
            string id = HeldId(item);
            if (id == null) return null;
            var measured = new RigProfile.HeldItem
            {
                Id = id,
                GripPath = gripPath,
                ItemPath = AnimationUtility.CalculateTransformPath(item, root),
                OrientationName = id == "shield" ? "top" : "blade",
                OrientationLocal = 90f,
                // Read against the grip rather than as a local angle: the artwork lives inside a
                // visual container now, so the calibration sits on a node ABOVE the renderer and a
                // local read would come back a flat zero.
                CalibrationZ = NormalizeAngle(item.eulerAngles.z - grip.eulerAngles.z),
                GripToButt = Vector3.Distance(grip.position, butt),
                ItemLength = Vector3.Distance(butt, tip)
            };

            if (grip.parent != null)
            {
                var boneRenderer = RigVisualParts.FindVisual(grip.parent);
                if (boneRenderer != null && boneRenderer.sprite != null)
                {
                    float boneHalf = boneRenderer.sprite.bounds.extents.y;
                    // Through the renderer's own transform, so a container scaled to fit new art
                    // moves the bone end with it instead of quietly reporting the unscaled length.
                    var boneEnd = boneRenderer.transform.TransformPoint(new Vector3(0f, -boneHalf, 0f));
                    measured.GripToBoneEnd = Vector3.Distance(grip.position, boneEnd);
                }
            }
            return measured;
        }

        /// <summary>"Rotation Point (Elbow)" -> "Elbow".</summary>
        static string ExtractLabel(string nodeName) => Presentation.Body.RigNaming.ExtractLabel(nodeName);

        /// <summary>Side comes from the limb above the joint, so joints keep one name across both sides.</summary>
        static string SideSuffix(Transform node, Transform root) =>
            Presentation.Body.RigNaming.SideSuffix(Presentation.Body.RigNaming.SideOf(node, root));

        /// <summary>
        /// The item's logical id, taken from the declaration on its bone (<c>UnitHeldItem</c>): weapon or
        /// shield. No declaration means no entry — clip recipes aim by this id, and a guessed one would
        /// disagree with the runtime part registry that reads the very same component.
        /// </summary>
        static string HeldId(Transform item)
        {
            var bone = Presentation.Body.RigNaming.BoneOf(item);
            var mark = bone != null ? bone.GetComponent<Presentation.Body.UnitHeldItem>() : null;
            if (mark == null || mark.Kind == Presentation.Body.HeldKind.None)
            {
                Debug.LogError($"[RigProfile] предмет '{(bone != null ? bone.name : item.name)}' сидит в " +
                               "хвате, но не объявлен: повесь на его кость UnitHeldItem (Weapon/Shield). " +
                               "Без объявления предмет не попадёт в профиль, и рецепты Aim(\"weapon\") его " +
                               "не найдут.");
                return null;
            }
            return mark.Kind == Presentation.Body.HeldKind.Shield ? "shield" : "weapon";
        }

        public static float NormalizeAngle(float degrees)
        {
            float a = Mathf.Repeat(degrees, 360f);
            return a > 180f ? a - 360f : a;
        }

        static string Describe(RigProfile profile)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{profile.name}: {profile.Joints.Count} joints, {profile.Held.Count} held items");
            foreach (var joint in profile.Joints)
                sb.AppendLine($"  {joint.Id,-12} rest={joint.RestZ,8:F2} boneAxis={joint.BoneAxisLocal,8:F2} " +
                              $"len={joint.BoneLength:F4} flex={joint.FlexSign:+0;-0} limit={joint.FlexLimit:F0}");
            foreach (var item in profile.Held)
                sb.AppendLine($"  [{item.Id}] orientation={item.OrientationName}@{item.OrientationLocal:F0} " +
                              $"calibration={item.CalibrationZ:F2} gripToButt={item.GripToButt:F4} " +
                              $"gripToBoneEnd={item.GripToBoneEnd:F4} length={item.ItemLength:F4}");
            return sb.ToString();
        }

        [MenuItem("Alebardium/Animation/Rebuild Rig Profile", priority = 610)]
        static void RebuildSelected()
        {
            var profile = Selection.activeObject as RigProfile;
            if (profile == null)
            {
                Debug.LogError("Rebuild Rig Profile: select a RigProfile asset first.");
                return;
            }
            Debug.Log(Build(profile));
        }

        [MenuItem("Alebardium/Animation/Rebuild Rig Profile", validate = true)]
        static bool RebuildSelectedValidate() => Selection.activeObject is RigProfile;
    }
}
#endif
