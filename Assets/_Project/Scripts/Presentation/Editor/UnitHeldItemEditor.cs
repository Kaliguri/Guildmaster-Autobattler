using Guildmaster.Presentation.Body;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Presentation.Editor
{
    /// <summary>
    /// Редактор предмета в руке: объявленный вылет тянется МЫШЬЮ за остриё, а не набивается двумя
    /// числами. Тот же приём, что у зоны расстановки арены (<c>ArenaLayoutAuthoringEditor</c>) — там
    /// границы поля тянут за грани.
    /// </summary>
    /// <remarks>
    /// Длина и угол остаются числами на компоненте — хэндл их не заменяет, а редактирует: перерисовка
    /// арта не должна двигать вылет, и ровно поэтому он объявляется, а не выводится из спрайта. Здесь
    /// же рядом рисуется остриё ПО МЕШУ, чтобы расхождение было видно в тот момент, когда его создают,
    /// а не через месяц на упавшем гейте <c>DeclaredReachTests</c>.
    /// </remarks>
    [CustomEditor(typeof(UnitHeldItem))]
    public sealed class UnitHeldItemEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            // Правка чисел в инспекторе сама по себе Scene-вью не будит — иначе хэндл отстаёт от значений.
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        private void OnSceneGUI()
        {
            var item = (UnitHeldItem)target;
            Transform grip = item.transform;

            serializedObject.Update();
            SerializedProperty lengthProp = serializedObject.FindProperty("_declaredLength");
            SerializedProperty axisProp   = serializedObject.FindProperty("_declaredAxisDeg");
            if (lengthProp == null || axisProp == null) return;

            // Ноль означает «не объявлено» и работает переходный замер по мешу. Хэндл в этом режиме не
            // рисуем: он записал бы число молча, и переходный режим кончился бы незаметно для автора.
            if (lengthProp.floatValue <= 0f)
            {
                Handles.color = new Color(1f, 0.5f, 0.3f);
                Handles.Label(grip.position + Vector3.up * 0.06f,
                    "вылет не объявлен — «Замерить вылет по мешу» в контекстном меню компонента");
                return;
            }

            Vector3 local = Quaternion.Euler(0f, 0f, axisProp.floatValue) * (Vector3.right * lengthProp.floatValue);
            Vector3 tip   = grip.TransformPoint(local);

            Handles.color = Color.yellow;
            Handles.DrawLine(grip.position, tip);
            Handles.Label(tip + Vector3.up * 0.05f,
                $"вылет {lengthProp.floatValue:0.###} м, ось {axisProp.floatValue:0.#}°");

            EditorGUI.BeginChangeCheck();
            float size = HandleUtility.GetHandleSize(tip) * 0.09f;
            Vector3 moved = Handles.FreeMoveHandle(tip, size, Vector3.zero, Handles.SphereHandleCap);

            if (!EditorGUI.EndChangeCheck()) return;

            // Мышь двигает МИРОВОЕ остриё, а храним мы локальные длину и угол: пересчитываем обратно
            // через узел-хват, поэтому поворот кости и масштаб фигуры учитываются сами.
            Vector3 fromGrip = grip.InverseTransformPoint(moved);
            fromGrip.z = 0f;
            if (fromGrip.sqrMagnitude < 1e-8f) return;

            Undo.RecordObject(item, "Вылет предмета");
            lengthProp.floatValue = fromGrip.magnitude;
            axisProp.floatValue   = Mathf.Atan2(fromGrip.y, fromGrip.x) * Mathf.Rad2Deg;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
