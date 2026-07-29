#if UNITY_EDITOR
using System.Collections.Generic;
using Guildmaster.Presentation;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Writes onto the unit view how much GROUND each locomotion clip covers per second.
    ///
    /// <b>Why this exists.</b> <see cref="UnitView"/> paces the legs by distance travelled: it divides the
    /// unit's real speed by the clip's own "native" speed, so the feet never slide no matter how fast the
    /// simulation moves the body. That native speed is a property OF THE CLIP — stride length times steps
    /// per cycle, over cycle length — and it used to be a hand-typed number shared by walk and sprint alike.
    /// Sprint has a longer stride in a shorter cycle, so the shared number ran it at roughly twice the rate
    /// the ground justified, and the legs blurred.
    ///
    /// <b>The measuring itself belongs to <see cref="RigStride"/>.</b> This tool only converts and writes:
    /// the gizmo measures in rig units on the rig prefab, the view plays at the combat prefab's scale, and
    /// two tools measuring the same stride two ways would drift the moment one of them was improved.
    /// </summary>
    public static class LocomotionStrideMeter
    {
        const string ViewPrefab = "Assets/_Project/Prefabs/Units/UnitView_BoneStandart.prefab";
        const string ClipFolder = "Assets/_Project/Prefabs/Bones/";
        const string ProfilePath = ClipFolder + "BoneUnit_Standart_RigProfile.asset";

        // Clip behind each state, and the field on UnitView that carries its pace.
        static readonly (string clip, string field)[] Clips =
        {
            ("Walk",   "_runUnitsPerSecond"),
            ("Sprint", "_sprintUnitsPerSecond"),
        };

        [MenuItem("Alebardium/Animation/Measure Locomotion Stride", priority = 610)]
        public static void Measure()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RigProfile>(ProfilePath);
            if (profile == null) { Debug.LogError($"[StrideMeter] нет профиля рига: {ProfilePath}"); return; }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefab);
            if (prefab == null) { Debug.LogError($"[StrideMeter] нет префаба вида: {ViewPrefab}"); return; }

            var view = prefab.GetComponentInChildren<UnitView>(true);
            if (view == null) { Debug.LogError("[StrideMeter] на префабе вида нет UnitView."); return; }

            // Масштаб берём с того самого Animator, который играет клипы в бою: риг авторится в своих
            // единицах, а на арене живёт увеличенным, и темп нужен в МИРОВЫХ единицах.
            var reader = new SerializedObject(view);
            var animator = reader.FindProperty("_animator")?.objectReferenceValue as Animator;
            if (animator == null) { Debug.LogError("[StrideMeter] у вида не разведён _animator."); return; }

            float scale = Mathf.Abs(animator.transform.lossyScale.x);
            if (scale <= 0f) scale = 1f;

            var log = new List<string> { $"Locomotion pace (масштаб визуала {scale:F3}):" };
            var measured = new List<(string field, float perSecond)>();

            foreach (var (clipName, fieldName) in Clips)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + clipName + ".anim");
                if (clip == null) { Debug.LogWarning($"[StrideMeter] нет клипа {clipName}"); continue; }

                RigStride.Result stride = RigStride.Render(profile, clip);
                float perSecond = stride.UnitsPerSecond * scale;
                measured.Add((fieldName, perSecond));

                log.Add($"  {clipName}: {stride.UnitsPerSecond:F2} ед/с рига × {scale:F2} = {perSecond:F2} ед/с мира");
                foreach (RigStride.Foot foot in stride.Feet)
                    log.Add($"    {foot.Name}: шаг {foot.Stride * scale:F3} мира, на земле {foot.PlantedShare:P0}" +
                            (foot.BelowGround > 0.001f
                                ? $"  <-- под землёй {foot.BelowGround * scale:F3}"
                                : ""));
            }

            var writer = new SerializedObject(view);
            foreach (var (field, perSecond) in measured)
            {
                SerializedProperty property = writer.FindProperty(field);
                if (property == null) { Debug.LogError($"[StrideMeter] нет поля {field} на UnitView"); continue; }
                property.floatValue = perSecond;
            }
            writer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
            AssetDatabase.SaveAssetIfDirty(prefab);   // иначе правка живёт до первого domain reload

            // Построчно, а не одним сообщением: многострочный лог консоль показывает первой строкой, и
            // замер, ушедший в ноль, выглядел как замер, которого не было.
            foreach (string line in log) Debug.Log(line);
        }
    }
}
#endif
