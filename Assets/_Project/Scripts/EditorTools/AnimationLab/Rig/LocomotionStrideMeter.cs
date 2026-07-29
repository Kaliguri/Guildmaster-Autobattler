#if UNITY_EDITOR
using System.Collections.Generic;
using Guildmaster.Presentation;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Measures how much GROUND a locomotion clip covers per second, and writes that number onto the unit
    /// view that plays it.
    ///
    /// <b>Why this exists.</b> <see cref="UnitView"/> paces the legs by distance travelled: it divides the
    /// unit's real speed by the clip's own "native" speed, so the feet never slide no matter how fast the
    /// simulation moves the body. That native speed is a property OF THE CLIP — stride length times steps
    /// per cycle, divided by cycle length — and until now it was a hand-typed number shared by walk and
    /// sprint alike. Sprint has a longer stride in a shorter cycle, so the shared number ran it at roughly
    /// twice the rate the ground justified, and the legs blurred. Measured, not guessed, and measured here
    /// so that editing a stride in the recipe updates the pacing by rerunning this rather than by
    /// remembering to.
    ///
    /// The stride is read as the horizontal travel of each foot relative to the unit's root: while a foot
    /// is planted the body moves over it by exactly that much, and a cycle contains two such steps.
    /// </summary>
    public static class LocomotionStrideMeter
    {
        const string ViewPrefab = "Assets/_Project/Prefabs/Units/UnitView_BoneStandart.prefab";
        const string ClipFolder = "Assets/_Project/Prefabs/Bones/";

        // Clip behind each state, and the field on UnitView that carries its pace.
        static readonly (string clip, string field)[] Clips =
        {
            ("Walk",   "_runUnitsPerSecond"),
            ("Sprint", "_sprintUnitsPerSecond"),
        };

        // Feet, as named on the rig. Both are measured and averaged: a cycle that leads with one leg makes
        // them differ slightly, and neither of the two is the "right" one.
        static readonly string[] FootNodes = { "Rotation Point (Ankle)" };

        const int SamplesPerSecond = 120;   // twice the clip's own rate: the extreme of a stride is a point

        [MenuItem("Alebardium/Animation/Measure Locomotion Stride", priority = 610)]
        public static void Measure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefab);
            if (prefab == null) { Debug.LogError($"[StrideMeter] no prefab at {ViewPrefab}"); return; }

            // Именно InstantiatePrefab, а не Instantiate: замер обязан вернуться В ПРЕФАБ, а обычная копия
            // частью префаб-инстанса не является, и Apply на ней бросает.
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) { Debug.LogError("[StrideMeter] не удалось создать инстанс префаба."); return; }

            var log = new System.Text.StringBuilder("Locomotion stride, measured off the clips:\n");
            try
            {
                var view = instance.GetComponentInChildren<UnitView>(true);
                if (view == null) { Debug.LogError("[StrideMeter] the view prefab carries no UnitView."); return; }

                var serialized = new SerializedObject(view);
                // Тот самый Animator, который играет клипы В БОЮ — а не первый попавшийся в иерархии.
                // На скелетном префабе их два: этот и наследственный на узле спрайта, оставшийся от
                // покадровой сборки. Клипы адресуют пути от корня скелета, поэтому сэмплинг «не того»
                // Animator тихо не двигает ничего — замер выходит нулевым, а причина не видна.
                var animator = serialized.FindProperty("_animator")?.objectReferenceValue as Animator;
                if (animator == null)
                {
                    Debug.LogError("[StrideMeter] у вида не разведён _animator — играть клипы нечем.");
                    return;
                }

                // Ноги ищем от корня ВИДА, а не от Animator: где именно сидит Animator — вопрос сборки
                // префаба, и привязываться к нему значит ломаться от любой перестановки узлов.
                List<Transform> feet = FindFeet(instance.transform);
                if (feet.Count == 0)
                {
                    Debug.LogError("[StrideMeter] no ankle nodes on the rig. Узлы вида: " + Dump(instance.transform));
                    return;
                }

                // Сначала МЕРЯЕМ, и только потом пишем. Порядок не косметический: сэмплинг клипа идёт
                // через AnimationMode, который сам двигает трансформы инстанса, и SerializedObject,
                // созданный до него, свою запись до префаба не доносит — числа уходили нулями.
                var measured = new List<(string field, float perSecond)>();
                foreach (var (clipName, fieldName) in Clips)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + clipName + ".anim");
                    if (clip == null) { Debug.LogWarning($"[StrideMeter] нет клипа {clipName}"); continue; }

                    float stride = MeasureStride(animator.gameObject, animator.transform, feet, clip);
                    // Two steps to a cycle: the recipe writes both legs into every locomotion clip.
                    float perSecond = clip.length > 0f ? stride * 2f / clip.length : 0f;
                    measured.Add((fieldName, perSecond));

                    // Построчно, а не одним сообщением: многострочный лог консоль показывает первой строкой,
                    // и замер, ушедший в ноль, выглядел как замер, которого не было.
                    log.AppendLine($"  {clipName}: шаг {stride:F3} ед, цикл {clip.length:F3} с → {perSecond:F2} ед/с");
                }

                var writer = new SerializedObject(view);
                foreach (var (field, perSecond) in measured)
                {
                    SerializedProperty property = writer.FindProperty(field);
                    if (property == null) { Debug.LogError($"[StrideMeter] нет поля {field} на UnitView"); continue; }
                    property.floatValue = perSecond;
                }
                writer.ApplyModifiedPropertiesWithoutUndo();

                // Пишем ровно тот компонент, который замеряли, а не весь инстанс: Apply целиком утащил бы
                // в префаб и позу, оставленную сэмплингом клипа.
                PrefabUtility.ApplyObjectOverride(view, ViewPrefab, InteractionMode.AutomatedAction);
                AssetDatabase.SaveAssetIfDirty(prefab);   // иначе правка живёт до первого domain reload
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                if (instance != null) Object.DestroyImmediate(instance);
                foreach (string line in log.ToString().Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) Debug.Log(line.Trim());
            }
        }

        // Horizontal travel of a foot relative to the root, averaged over both legs. While the foot is
        // planted the body passes over it by exactly this distance — that IS the step.
        static float MeasureStride(GameObject go, Transform root, List<Transform> feet, AnimationClip clip)
        {
            int samples = Mathf.Max(2, Mathf.RoundToInt(clip.length * SamplesPerSecond));
            var min = new float[feet.Count];
            var max = new float[feet.Count];
            for (int i = 0; i < feet.Count; i++) { min[i] = float.MaxValue; max[i] = float.MinValue; }

            AnimationMode.StartAnimationMode();
            for (int s = 0; s <= samples; s++)
            {
                float t = clip.length * s / samples;
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(go, clip, t);
                AnimationMode.EndSampling();

                for (int i = 0; i < feet.Count; i++)
                {
                    float x = root.InverseTransformPoint(feet[i].position).x * root.lossyScale.x;
                    if (x < min[i]) min[i] = x;
                    if (x > max[i]) max[i] = x;
                }
            }
            AnimationMode.StopAnimationMode();

            float total = 0f;
            for (int i = 0; i < feet.Count; i++) total += max[i] - min[i];
            return total / feet.Count;
        }

        // Имена узлов вида одной строкой — чтобы «ног не нашлось» отвечало, что там нашлось вместо них.
        static string Dump(Transform root)
        {
            var names = new List<string>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) names.Add(node.name);
            return string.Join(", ", names);
        }

        static List<Transform> FindFeet(Transform root)
        {
            var found = new List<Transform>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                foreach (string name in FootNodes)
                    if (node.name == name) found.Add(node);
            return found;
        }
    }
}
#endif
