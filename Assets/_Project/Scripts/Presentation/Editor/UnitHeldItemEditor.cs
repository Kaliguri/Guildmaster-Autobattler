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
            if (lengthProp == null) return;

            // Ноль означает «не объявлено» и работает переходный замер по мешу. Хэндл в этом режиме не
            // рисуем: он записал бы число молча, и переходный режим кончился бы незаметно для автора.
            if (lengthProp.floatValue <= 0f)
            {
                Handles.color = new Color(1f, 0.5f, 0.3f);
                Handles.Label(grip.position + Vector3.up * 0.06f,
                    "вылет не объявлен — «Замерить вылет по мешу» в контекстном меню компонента");
                return;
            }

            // Направление берётся с РИСУНКА, поэтому хэндл тянет только длину — вдоль клинка и никуда
            // больше. Свобода увести остриё вбок была ровно тем, чем ею и воспользовались: ось уехала на
            // 33° от нарисованного клинка и увела за собой дугу за клинком (07.08.2026).
            if (!item.TryGetDeclaredTip(out Vector3 tip))
            {
                Handles.color = new Color(1f, 0.5f, 0.3f);
                Handles.Label(grip.position + Vector3.up * 0.06f,
                    "рабочая часть не разведена или у неё пуст спрайт — направление вылета брать неоткуда");
                return;
            }

            Handles.color = Color.yellow;
            Handles.DrawLine(grip.position, tip);
            Handles.Label(tip + Vector3.up * 0.05f, $"вылет {lengthProp.floatValue:0.###} м");

            EditorGUI.BeginChangeCheck();
            float size = HandleUtility.GetHandleSize(tip) * 0.09f;
            Vector3 moved = Handles.FreeMoveHandle(tip, size, Vector3.zero, Handles.SphereHandleCap);

            if (!EditorGUI.EndChangeCheck()) return;

            // Мышь двигает МИРОВОЕ остриё, а храним мы длину: проецируем сдвиг на ось клинка, поэтому
            // увести вылет вбок нельзя в принципе, а поворот кости и масштаб фигуры учитываются сами.
            if (!item.TryGetReachDirection(out Vector3 dirLocal)) return;

            Vector3 fromGrip = grip.InverseTransformPoint(moved);
            fromGrip.z = 0f;

            float along = Vector3.Dot(fromGrip, dirLocal);
            if (along <= 1e-4f) return;   // за хват вылет не заводим: это уже не длина, а зеркало

            Undo.RecordObject(item, "Вылет предмета");
            lengthProp.floatValue = along;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
