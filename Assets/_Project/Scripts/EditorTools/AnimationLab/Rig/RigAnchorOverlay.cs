#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Суставы рига прямо в Scene view: подписанная точка на каждом узле вращения и красная линия к
    /// пивоту куска, если тот уехал со своего сустава.
    ///
    /// Отдельно от <see cref="RigProbe"/> намеренно: проба рендерит ОТЧЁТ — картинку, которую можно
    /// приложить к разговору и сравнить с прошлой. Оверлей отвечает на другой вопрос — «куда я сейчас
    /// тащу этот спрайт», — и отвечать он обязан в тот же момент, когда рука на мыши. Картинка,
    /// которую надо перегенерировать после каждого движения, на этот вопрос не отвечает вовсе.
    /// </summary>
    /// <remarks>
    /// Правило принадлежности куска суставу берётся из <see cref="RigAnchors"/> — тот же обход, что и у
    /// пробы. Иначе оверлей и картинка начали бы расходиться в вердикте, и верить пришлось бы наугад.
    /// </remarks>
    [InitializeOnLoad]
    public static class RigAnchorOverlay
    {
        const string MenuPath = "Alebardium/Animation/Show Rig Anchors In Scene";
        const string PrefKey = "Alebardium.Animation.ShowRigAnchors";

        static RigAnchorOverlay()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, priority = 626)]
        static void Toggle()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, validate = true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void OnSceneGui(SceneView view)
        {
            if (!Enabled) return;

            var root = ResolveRig();
            if (root == null) return;

            var profile = RigProbe.FindProfileFor(PrefabRootAsset(root));
            if (profile == null) return;

            List<RigAnchors.Anchor> anchors;
            try { anchors = RigAnchors.Collect(root.transform, profile); }
            catch (System.Exception) { return; }   // риг может быть на полпути правки — молчим, а не сыплем

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var drawnJoints = new HashSet<Transform>();

            foreach (var anchor in anchors)
            {
                if (drawnJoints.Add(anchor.Joint))
                {
                    Handles.color = JointColor;
                    Handles.DrawSolidDisc(anchor.Joint.position, Vector3.forward, JointRadius(view));
                    Handles.Label(anchor.Joint.position + LabelNudge(view), Caption(anchor.JointId), JointStyle);
                }

                if (!anchor.DeclaresPivot)
                {
                    Handles.color = PlaceholderColor;
                    Handles.DrawSolidDisc(anchor.PivotWorld, Vector3.forward, JointRadius(view) * 0.5f);
                    continue;
                }

                bool off = anchor.OffsetPixels > RigAnchors.DefaultTolerancePixels;
                Handles.color = off ? BadColor : OkColor;
                Handles.DrawSolidDisc(anchor.PivotWorld, Vector3.forward, JointRadius(view) * 0.7f);
                if (!off) continue;

                Handles.DrawLine(anchor.Joint.position, anchor.PivotWorld, 3f);
                Handles.Label(anchor.PivotWorld + LabelNudge(view) * 0.6f,
                              $"{anchor.SpriteName}  {anchor.OffsetPixels:F0} px", BadStyle);
            }
        }

        /// <summary>
        /// Что рисуем: открытую prefab-stage, иначе — корень юнита, внутри которого стоит выделение.
        /// Без второго условия оверлей молчал бы ровно тогда, когда выделен один сустав, а его и таскают.
        /// </summary>
        static GameObject ResolveRig()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) return stage.prefabContentsRoot;

            var selected = Selection.activeGameObject;
            if (selected == null) return null;
            var root = selected.transform;
            while (root.parent != null) root = root.parent;
            return root.gameObject;
        }

        /// <summary>Ассет, по которому ищется профиль: для инстанса — его префаб, для stage — сам ассет.</summary>
        static GameObject PrefabRootAsset(GameObject root)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) return AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);

            var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            return source != null ? source : root;
        }

        // Радиус в мировых единицах, но от размера экрана: риг этого проекта живёт в десятых долях
        // юнита, и точка фиксированного мирового размера либо закрывает половину руки, либо исчезает.
        static float JointRadius(SceneView view) => HandleUtility.GetHandleSize(view.pivot) * 0.012f;
        static Vector3 LabelNudge(SceneView view) => new Vector3(HandleUtility.GetHandleSize(view.pivot) * 0.02f, 0f, 0f);

        static readonly Color JointColor = new Color(0.30f, 0.85f, 1f, 0.95f);
        static readonly Color OkColor = new Color(0.35f, 1f, 0.45f, 0.95f);
        static readonly Color BadColor = new Color(1f, 0.25f, 0.25f, 0.95f);
        static readonly Color PlaceholderColor = new Color(0.55f, 0.55f, 0.60f, 0.65f);

        static GUIStyle _jointStyle, _badStyle;

        static GUIStyle JointStyle => _jointStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = JointColor },
            fontSize = 11,
        };

        static GUIStyle BadStyle => _badStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = BadColor },
            fontSize = 11,
        };

        /// <summary>
        /// Подпись сустава по-русски: инструмент читает Макс, а не логи. Сторона (<c>.R</c>) остаётся
        /// как есть — она и так однозначна, а «правый локоть» длиннее и на картинке лезет на соседа.
        /// </summary>
        static string Caption(string jointId)
        {
            int dot = jointId.IndexOf('.');
            string bone = dot < 0 ? jointId : jointId.Substring(0, dot);
            string side = dot < 0 ? "" : jointId.Substring(dot);
            return (Names.TryGetValue(bone, out string human) ? human : bone) + side;
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["hips"] = "таз",
            ["torso"] = "корпус",
            ["head"] = "голова",
            ["shoulder"] = "плечо",
            ["elbow"] = "локоть",
            ["grip"] = "хват",
            ["hip"] = "бедро",
            ["knee"] = "колено",
            ["ankle"] = "голеностоп",
        };
    }
}
#endif
