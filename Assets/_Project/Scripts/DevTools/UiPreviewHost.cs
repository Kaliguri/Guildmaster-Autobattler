using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Хост UI-стенда: висит на <see cref="UIDocument"/> в сцене <c>UiPreview.unity</c>. На входе в Play
    /// собирает целевой экран через <see cref="UiPreviewCatalog"/> со стендовыми данными (реальный контент
    /// из <c>ContentDatabase</c>, без боя и бута) в рантайм-панель UITK.
    /// <para>Цель берётся из <c>SessionState["gm.uiPreview.target"]</c> (её выставляют перед входом в Play),
    /// иначе — <see cref="_defaultTarget"/>. Снятие в PNG — отдельным шагом уже в Play (панель рисуется
    /// только в игровом лупе), а не этим компонентом: под автоматизацией надёжнее покадровый
    /// <c>EditorApplication.Step()</c> + чтение <c>PanelSettings.targetTexture</c>, чем корутина с
    /// ожиданием кадров. Editor-only.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiPreviewHost : MonoBehaviour
    {
        [Tooltip("Цель по умолчанию, если стенд запущен вручную (не через выставленный SessionState).")]
        [SerializeField] private string _defaultTarget = "dev-picker";

        private void Start()
        {
#if UNITY_EDITOR
            var doc = GetComponent<UIDocument>();
            if (doc == null || doc.rootVisualElement == null)
            {
                Debug.LogWarning($"[UiPreviewHost] doc={(doc != null)} root={(doc != null && doc.rootVisualElement != null)}");
                return;
            }

            string target = UnityEditor.SessionState.GetString("gm.uiPreview.target", _defaultTarget);
            UiPreviewCatalog.Build(target, doc.rootVisualElement);
#endif
        }
    }
}
