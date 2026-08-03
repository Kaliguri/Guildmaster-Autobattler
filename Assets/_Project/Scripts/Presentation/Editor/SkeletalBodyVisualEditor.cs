using System.Collections.Generic;
using System.Text;
using Guildmaster.Presentation.Body;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guildmaster.Presentation.Editor
{
    /// <summary>
    /// Инспектор составного тела: список частей сверху вниз, как слои в графическом редакторе (верхний
    /// рисуется поверх), плюс разводка — порядок, пересборка из иерархии и материал вспышки на все части.
    /// <para>
    /// Зачем инспектор, а не поле на каждом спрайте: порядок отрисовки шестнадцати частей, живущий в
    /// шестнадцати <c>m_SortingOrder</c>, негде увидеть целиком и приходится править по одному. Здесь у
    /// него один владелец и один экран.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(SkeletalBodyVisual))]
    public sealed class SkeletalBodyVisualEditor : UnityEditor.Editor
    {
        // Материал тела: только он несёт _FlashAmount/_Holo/_Outline. Часть на дефолтном спрайтовом
        // материале физически не умеет ни вспыхнуть, ни развоплотиться — и молчит об этом.
        private const string FlashMaterialPath = "Assets/_Project/Art/Materials/MAT_Sprite_HitFlash.mat";
        private const string FlashProperty     = "_FlashAmount";

        private SerializedProperty _parts;
        private SerializedProperty _group;

        private void OnEnable()
        {
            _parts = serializedObject.FindProperty("_parts");
            _group = serializedObject.FindProperty("_group");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Порядок отрисовки: ВЕРХНИЙ в списке рисуется поверх остальных. " +
                "Перетащи части, затем «Применить порядок».", MessageType.None);

            EditorGUILayout.PropertyField(_parts, new GUIContent("Части тела (сверху вниз)"), true);
            EditorGUILayout.PropertyField(_group, new GUIContent("Группа сортировки"));

            serializedObject.ApplyModifiedProperties();

            var body = (SkeletalBodyVisual)target;

            EditorGUILayout.Space();
            DrawActions(body);
            EditorGUILayout.Space();
            DrawReport(body);
        }

        private void DrawActions(SkeletalBodyVisual body)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Применить порядок"))  ApplyOrder(body);
                if (GUILayout.Button("Собрать заново"))     Rebuild(body);
            }

            if (GUILayout.Button("Взять порядок из спрайтов"))
            {
                Undo.RecordObject(body, "Взять порядок частей из спрайтов");
                body.SortByCurrentOrder();
                EditorUtility.SetDirty(body);
            }

            if (GUILayout.Button("Поставить материал вспышки на все части"))
                AssignFlashMaterial(body);

            if (EffectiveGroup(body) == null && GUILayout.Button("Добавить SortingGroup на корень"))
            {
                Undo.AddComponent<SortingGroup>(body.gameObject);
                EditorUtility.SetDirty(body);
            }
        }

        private static void ApplyOrder(SkeletalBodyVisual body)
        {
            var targets = new List<Object>(body.Renderers.Count);
            for (int i = 0; i < body.Renderers.Count; i++)
                if (body.Renderers[i] != null) targets.Add(body.Renderers[i]);

            Undo.RecordObjects(targets.ToArray(), "Применить порядок частей тела");
            body.ApplyOrder();
            for (int i = 0; i < targets.Count; i++) EditorUtility.SetDirty(targets[i]);
        }

        private static void Rebuild(SkeletalBodyVisual body)
        {
            Undo.RecordObject(body, "Собрать части тела заново");
            bool changed = body.RebuildParts();
            EditorUtility.SetDirty(body);
            Debug.Log(changed
                ? $"[SkeletalBodyVisual] {body.name}: список пересобран — {body.Renderers.Count} частей."
                : $"[SkeletalBodyVisual] {body.name}: список уже совпадает с иерархией ({body.Renderers.Count} частей).",
                body);
        }

        // Раскладывает материал тела по всем частям. Именно этого не хватало костяному юниту: все его части
        // сидели на дефолтном спрайтовом материале, поэтому вспышка удара, голограмма смерти и контур каста
        // на нём не работали вовсе — код их честно считал, а рисовать было нечем.
        private static void AssignFlashMaterial(SkeletalBodyVisual body)
        {
            Material mat = LoadFlashMaterial();
            if (mat == null)
            {
                Debug.LogError($"[SkeletalBodyVisual] Материал вспышки не найден ни по пути " +
                               $"'{FlashMaterialPath}', ни поиском по имени — разложить нечего.", body);
                return;
            }

            var targets = new List<Object>(body.Renderers.Count);
            for (int i = 0; i < body.Renderers.Count; i++)
                if (body.Renderers[i] != null) targets.Add(body.Renderers[i]);

            Undo.RecordObjects(targets.ToArray(), "Поставить материал вспышки на части тела");
            int changed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var part = (SpriteRenderer)targets[i];
                if (part.sharedMaterial == mat) continue;
                part.sharedMaterial = mat;
                EditorUtility.SetDirty(part);
                changed++;
            }
            Debug.Log($"[SkeletalBodyVisual] {body.name}: материал вспышки поставлен на {changed} " +
                      $"из {targets.Count} частей.", body);
        }

        private static Material LoadFlashMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath);
            if (mat != null) return mat;

            // Ассет переехал — ищем по имени. Поиск по guid здесь бесполезен: он у копии материала свой.
            string[] guids = AssetDatabase.FindAssets("MAT_Sprite_HitFlash t:Material");
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        // Разбор разводки: то, что молча не работает в рантайме, должно быть видно здесь.
        private void DrawReport(SkeletalBodyVisual body)
        {
            var found = new List<SpriteRenderer>();
            body.CollectParts(found);

            int nulls = 0, dupes = 0, noFlash = 0, wrongLayer = 0;
            var seen = new HashSet<SpriteRenderer>();
            SortingGroup group = EffectiveGroup(body);
            int layer = group != null ? group.sortingLayerID : int.MinValue;

            for (int i = 0; i < body.Renderers.Count; i++)
            {
                SpriteRenderer part = body.Renderers[i];
                if (part == null) { nulls++; continue; }
                if (!seen.Add(part)) dupes++;

                Material mat = part.sharedMaterial;
                if (mat == null || !mat.HasProperty(FlashProperty)) noFlash++;
                if (layer != int.MinValue && part.sortingLayerID != layer) wrongLayer++;
            }

            int missing = 0;
            for (int i = 0; i < found.Count; i++)
                if (!seen.Contains(found[i])) missing++;

            var sb = new StringBuilder();
            sb.AppendLine($"Частей в списке: {body.Renderers.Count}, в иерархии: {found.Count}.");
            if (nulls > 0)      sb.AppendLine($"Потерянных ссылок: {nulls} — «Собрать заново».");
            if (dupes > 0)      sb.AppendLine($"Дублей: {dupes} — «Собрать заново».");
            if (missing > 0)    sb.AppendLine($"Не в списке: {missing} — эти части не красятся и не колются.");
            if (noFlash > 0)    sb.AppendLine($"Без материала вспышки: {noFlash} — не вспыхнут и не развоплотятся.");
            if (wrongLayer > 0) sb.AppendLine($"С чужим слоем сортировки: {wrongLayer} — «Применить порядок».");
            if (group == null)  sb.AppendLine("Нет SortingGroup — Y-сортировка арены не дойдёт до тела.");

            bool clean = nulls == 0 && dupes == 0 && missing == 0 && noFlash == 0 && wrongLayer == 0 && group != null;
            EditorGUILayout.HelpBox(clean ? sb.Append("Разводка целая.").ToString() : sb.ToString(),
                clean ? MessageType.Info : MessageType.Warning);
        }

        // Та же группа, которую возьмёт рантайм: сначала разведённая полем, иначе поиск на себе и в родителях.
        private SortingGroup EffectiveGroup(SkeletalBodyVisual body)
        {
            if (_group != null && _group.objectReferenceValue is SortingGroup wired) return wired;
            var group = body.GetComponent<SortingGroup>();
            return group != null ? group : body.GetComponentInParent<SortingGroup>(true);
        }
    }
}
