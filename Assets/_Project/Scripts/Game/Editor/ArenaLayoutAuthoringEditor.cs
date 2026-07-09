using Guildmaster.Core.Arena;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Guildmaster.Game.Editor
{
    /// <summary>
    /// Редактор арены: тянем границы поля и зоны расстановки прямо в Scene-вью за грани
    /// (BoxBoundsHandle), а не набиваем два Vector2. Наследуемся от <see cref="OdinEditor"/>, чтобы
    /// сохранить Odin-инспектор компонента (иначе [CustomEditor] перебил бы его). Хэндлы обновляются
    /// вживую; правка чисел в инспекторе форсит перерисовку сцены, чтобы гизмо не отставало (вики «15» §4).
    /// </summary>
    [CustomEditor(typeof(ArenaLayoutAuthoring))]
    public sealed class ArenaLayoutAuthoringEditor : OdinEditor
    {
        private readonly BoxBoundsHandle _handle = new BoxBoundsHandle
        {
            axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y, // 2D: только XY
        };

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI(); // Odin рисует атрибуты компонента
            // Правка чисел в инспекторе не будит Scene-вью сама по себе — форсим перерисовку,
            // иначе гизмо/хэндлы отстают от значений (жалоба: «при таскании не перерисовывается»).
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
        }

        private void OnSceneGUI()
        {
            var authoring = (ArenaLayoutAuthoring)target;
            Vector3 origin = authoring.transform.position;

            serializedObject.Update();

            DrawBox(origin,
                serializedObject.FindProperty("_boundsCenter"),
                serializedObject.FindProperty("_boundsSize"),
                Color.white, "Арена");

            DrawBox(origin,
                serializedObject.FindProperty("_cameraZoneCenter"),
                serializedObject.FindProperty("_cameraZoneSize"),
                Color.yellow, "Камера");

            SerializedProperty zones = serializedObject.FindProperty("_zones");
            for (int i = 0; i < zones.arraySize; i++)
            {
                SerializedProperty z = zones.GetArrayElementAtIndex(i);
                var side = (DeploymentSide)z.FindPropertyRelative("Side").enumValueIndex;
                var tier = (DeploymentTier)z.FindPropertyRelative("Tier").enumValueIndex;

                DrawBox(origin,
                    z.FindPropertyRelative("Center"),
                    z.FindPropertyRelative("Size"),
                    ZoneColor(side, tier),
                    $"{SideLabel(side)} · {tier}");
            }

            serializedObject.ApplyModifiedProperties(); // с авто-Undo
        }

        private void DrawBox(Vector3 origin, SerializedProperty centerProp, SerializedProperty sizeProp,
            Color color, string label)
        {
            Vector2 center = centerProp.vector2Value;
            Vector2 size   = sizeProp.vector2Value;

            _handle.center = origin + (Vector3)center;
            _handle.size   = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), 0f);

            EditorGUI.BeginChangeCheck();
            Handles.color = color;
            _handle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                centerProp.vector2Value = (Vector2)(_handle.center - origin);
                sizeProp.vector2Value   = new Vector2(_handle.size.x, _handle.size.y);
            }

            Handles.color = color;
            Handles.Label(_handle.center + Vector3.up * (_handle.size.y * 0.5f + 0.35f), label);
        }

        private static string SideLabel(DeploymentSide side) =>
            side == DeploymentSide.Player ? "Игрок" : "Враг";

        private static Color ZoneColor(DeploymentSide side, DeploymentTier tier)
        {
            Color c = side == DeploymentSide.Player
                ? new Color(0.3f, 0.7f, 1f)   // синий — игрок
                : new Color(1f, 0.4f, 0.3f);  // красный — враг
            if (tier == DeploymentTier.Extended) c.a = 0.6f; // extended — полупрозрачным
            return c;
        }
    }
}
