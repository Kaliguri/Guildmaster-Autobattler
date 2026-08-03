using System.IO;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Body;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Guildmaster.Presentation.Editor
{
    /// <summary>
    /// Стенд пост-обработки: показывает боевой профиль и профиль карты ЖИВЬЁМ — с bloom, виньеткой и
    /// всем прочим, — не заходя в play-mode. Слева кадр и ручки сцены, справа сам профиль.
    /// </summary>
    /// <remarks>
    /// Заведён после того, как выяснилось, что оценивать свечение было не по чему: кадр из редактора без
    /// <c>renderPostProcessing</c> показывает арт почти без свечения, профиль пост-обработки лежал пустым
    /// и этого никто не видел, а единственным способом посмотреть правду был play-mode.
    /// <para>
    /// Стенд ничего не оставляет за собой: камера, volume и субъект живут с <see cref="HideFlags.HideAndDontSave"/>,
    /// не попадают в иерархию и в сохранение сцены и уничтожаются вместе с окном. Именно поэтому он
    /// работает в любой открытой сцене, включая чужую.
    /// </para>
    /// <para>
    /// Свечение он подаёт ТЕМ ЖЕ путём, что бой: <see cref="CastGlowMask"/> переводит роль приёма в части
    /// тела, состояние уезжает в <see cref="IUnitBodyVisual.Apply"/>. Поэтому стенд проверяет заодно и
    /// реестр частей — если приём светит не тем, это видно здесь, а не в бою.
    /// </para>
    /// </remarks>
    public sealed class PostFxLabWindow : EditorWindow
    {
        const string BattleProfilePath = "Assets/Settings/PostFX/BattlePostFX_Base.asset";
        const string MapProfilePath    = "Assets/Settings/PostFX/MapPostFX.asset";
        const string FeelConfigPath    = "Assets/_Project/ScriptableObjects/Configs/CombatFeelConfig.asset";
        const string DefaultSubject    = "Assets/_Project/Prefabs/Bones/BoneUnit_Standart.prefab";
        const string ShotFolder        = "Temp/PostFxLab";

        /// <summary>Далеко от любой живой сцены: стенд не должен попадать в чужие камеры и коллайдеры.</summary>
        static readonly Vector3 StageOrigin = new Vector3(10000f, 10000f, 0f);

        enum StageKind { Battle, Map }

        [SerializeField] private StageKind  _stage = StageKind.Battle;
        [SerializeField] private bool       _postOn = true;
        [SerializeField] private GameObject _subjectPrefab;
        [SerializeField] private Color      _background = new Color(0.08f, 0.08f, 0.10f, 1f);
        [SerializeField] private float      _zoom = 1f;

        [Header("Свечение части")]
        [SerializeField] private CastSource _castSource = CastSource.Auto;
        [SerializeField] private float      _glowAmount = 1f;
        [SerializeField] private float      _glowFlatness = 0f;
        [SerializeField] private Color      _glowColor = new Color(0.35f, 0.75f, 1f, 1f);
        [SerializeField] private float      _glowBloom = 2.5f;

        private GameObject     _root;
        private Camera         _camera;
        private Volume         _volume;
        private GameObject     _subject;
        private RenderTexture  _preview;
        private UnityEditor.Editor _profileEditor;
        private Vector2        _profileScroll;
        private string         _lastShot;

        [MenuItem("Alebardium/VFX/Post FX Lab", priority = 700)]
        static void Open() => GetWindow<PostFxLabWindow>("Post FX Lab").minSize = new Vector2(880f, 520f);

        private void OnEnable()
        {
            if (_subjectPrefab == null)
                _subjectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultSubject);
            var feel = AssetDatabase.LoadAssetAtPath<Design.CombatFeelConfig>(FeelConfigPath);
            if (feel != null)
            {
                _glowBloom    = feel.CastGlowBloomIntensity;
                _glowFlatness = feel.CastGlowFlatness;
            }
        }

        private void OnDisable() => TearDownStage();

        private void OnGUI()
        {
            EnsureStage();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPreviewColumn();
                DrawProfileColumn();
            }
        }

        // --- Левая колонка: кадр и ручки сцены ---------------------------------------------------------

        private void DrawPreviewColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.55f)))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    StageKind stage = (StageKind)EditorGUILayout.EnumPopup(_stage, EditorStyles.toolbarPopup, GUILayout.Width(90f));
                    if (stage != _stage) { _stage = stage; RebindProfile(); }

                    _postOn = GUILayout.Toggle(_postOn, _postOn ? "Пост ВКЛ" : "Пост ВЫКЛ",
                        EditorStyles.toolbarButton, GUILayout.Width(90f));

                    if (GUILayout.Button("Снять кадр", EditorStyles.toolbarButton, GUILayout.Width(90f))) SaveShot(_postOn);
                    if (GUILayout.Button("Снять A/B", EditorStyles.toolbarButton, GUILayout.Width(80f))) SaveAb();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Пересобрать", EditorStyles.toolbarButton, GUILayout.Width(90f))) TearDownStage();
                }

                Rect frame = GUILayoutUtility.GetRect(10f, 10f, 200f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                if (Event.current.type == EventType.Repaint)
                {
                    RenderTexture rt = RenderPreview((int)frame.width, (int)frame.height, _postOn);
                    if (rt != null) GUI.DrawTexture(frame, rt, ScaleMode.StretchToFill, false);
                }

                DrawSceneKnobs();
            }
        }

        private void DrawSceneKnobs()
        {
            EditorGUI.BeginChangeCheck();

            var prefab = (GameObject)EditorGUILayout.ObjectField("Субъект", _subjectPrefab, typeof(GameObject), false);
            if (prefab != _subjectPrefab) { _subjectPrefab = prefab; TearDownStage(); }

            _background = EditorGUILayout.ColorField("Фон", _background);
            _zoom       = EditorGUILayout.Slider("Приближение", _zoom, 0.25f, 4f);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Свечение части", EditorStyles.boldLabel);
            _castSource   = (CastSource)EditorGUILayout.EnumPopup("Чем исполнен приём", _castSource);
            _glowAmount   = EditorGUILayout.Slider("Сила", _glowAmount, 0f, 1f);
            _glowFlatness = EditorGUILayout.Slider("Плоскость", _glowFlatness, 0f, 1f);
            _glowColor    = EditorGUILayout.ColorField("Цвет (LDR)", _glowColor);
            _glowBloom    = EditorGUILayout.Slider("Множитель под bloom", _glowBloom, 1f, 5f);

            if (EditorGUI.EndChangeCheck()) Repaint();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Записать свечение в feel-конфиг"))  WriteGlowToConfig();
                if (GUILayout.Button("Открыть папку кадров"))             EditorUtility.RevealInFinder(ShotFolder);
            }

            if (!string.IsNullOrEmpty(_lastShot))
                EditorGUILayout.HelpBox("Последний кадр: " + _lastShot, MessageType.None);
        }

        // --- Правая колонка: сам профиль ----------------------------------------------------------------

        private void DrawProfileColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                VolumeProfile profile = CurrentProfile();
                EditorGUILayout.LabelField(_stage == StageKind.Battle ? "Боевой профиль" : "Профиль карты",
                    EditorStyles.boldLabel);

                if (profile == null)
                {
                    EditorGUILayout.HelpBox("Профиль не найден: " + CurrentProfilePath(), MessageType.Error);
                    return;
                }

                // Пустой профиль — та самая тихая поломка: bloom «есть» в тумблере и отсутствует в кадре.
                if (profile.components.Count == 0)
                    EditorGUILayout.HelpBox("В профиле НЕТ компонентов — пост-обработки не будет вовсе.",
                        MessageType.Warning);

                if (_profileEditor == null || _profileEditor.target != profile)
                {
                    if (_profileEditor != null) DestroyImmediate(_profileEditor);
                    _profileEditor = UnityEditor.Editor.CreateEditor(profile);
                }

                using (var scroll = new EditorGUILayout.ScrollViewScope(_profileScroll))
                {
                    _profileScroll = scroll.scrollPosition;
                    EditorGUI.BeginChangeCheck();
                    _profileEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck()) Repaint();
                }
            }
        }

        // --- Стенд ---------------------------------------------------------------------------------------

        private string CurrentProfilePath() => _stage == StageKind.Battle ? BattleProfilePath : MapProfilePath;

        private VolumeProfile CurrentProfile() =>
            AssetDatabase.LoadAssetAtPath<VolumeProfile>(CurrentProfilePath());

        private void RebindProfile()
        {
            if (_volume != null) _volume.sharedProfile = CurrentProfile();
            Repaint();
        }

        private void EnsureStage()
        {
            if (_root != null) return;

            _root = new GameObject("PostFxLab (temp)") { hideFlags = HideFlags.HideAndDontSave };
            _root.transform.position = StageOrigin;

            var camGo = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(_root.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.allowHDR     = true;
            _camera.clearFlags   = CameraClearFlags.SolidColor;
            _camera.enabled      = false;   // рисуем только по требованию, кадр за кадром
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.None;

            var volGo = new GameObject("Volume") { hideFlags = HideFlags.HideAndDontSave };
            volGo.transform.SetParent(_root.transform, false);
            _volume = volGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10000f;      // перебиваем сценные volume: в кадре должен быть ИМЕННО выбранный профиль
            _volume.sharedProfile = CurrentProfile();

            if (_subjectPrefab != null)
            {
                _subject = Instantiate(_subjectPrefab, StageOrigin, Quaternion.identity, _root.transform);
                _subject.hideFlags = HideFlags.HideAndDontSave;
                SetHideFlagsRecursively(_subject.transform);
            }
        }

        static void SetHideFlagsRecursively(Transform node)
        {
            node.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < node.childCount; i++) SetHideFlagsRecursively(node.GetChild(i));
        }

        private void TearDownStage()
        {
            if (_profileEditor != null) { DestroyImmediate(_profileEditor); _profileEditor = null; }
            if (_preview != null) { _preview.Release(); DestroyImmediate(_preview); _preview = null; }
            if (_root != null) { DestroyImmediate(_root); _root = null; }
            _camera = null; _volume = null; _subject = null;
        }

        /// <summary>Кадр стенда. Свечение подаётся боевым путём — через шов тела, не записью в материал.</summary>
        private RenderTexture RenderPreview(int width, int height, bool post)
        {
            if (_camera == null || width < 8 || height < 8) return null;

            if (_preview == null || _preview.width != width || _preview.height != height)
            {
                if (_preview != null) { _preview.Release(); DestroyImmediate(_preview); }
                _preview = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            }

            ApplyGlow();
            FrameSubject();

            _camera.backgroundColor = _background;
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = post;
            _volume.enabled = post;

            _camera.targetTexture = _preview;
            _camera.Render();
            _camera.targetTexture = null;
            return _preview;
        }

        /// <summary>Кадрируем по субъекту: иначе при смене префаба кадр уезжает в пустоту.</summary>
        private void FrameSubject()
        {
            Bounds bounds = new Bounds(StageOrigin, Vector3.one);
            bool any = false;
            if (_subject != null)
            {
                foreach (var r in _subject.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            float extent = any ? Mathf.Max(bounds.extents.x, bounds.extents.y) : 1f;
            _camera.orthographicSize = Mathf.Max(0.01f, extent * 1.4f / Mathf.Max(0.01f, _zoom));
            _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
        }

        private void ApplyGlow()
        {
            if (_subject == null) return;
            var body = _subject.GetComponentInChildren<SkeletalBodyVisual>(true);
            if (body == null) return;

            PartMask parts = CastGlowMask.Resolve(body.Parts, _castSource);
            Color hdr = new Color(_glowColor.r * _glowBloom, _glowColor.g * _glowBloom, _glowColor.b * _glowBloom, 1f);

            body.Apply(new BodyVisualState(
                Color.white,
                0f, Color.white,
                0f, Color.white, 1f, 1f, 0f,
                0f, Color.white,
                _glowAmount, hdr, parts, _glowFlatness));
        }

        // --- Кадры и запись значений ---------------------------------------------------------------------

        private void SaveShot(bool post)
        {
            string file = Shot(post, _stage.ToString().ToLowerInvariant() + (post ? "_post" : "_raw"));
            _lastShot = file;
            Debug.Log("[PostFxLab] кадр: " + file);
        }

        private void SaveAb()
        {
            string raw  = Shot(false, _stage.ToString().ToLowerInvariant() + "_raw");
            string post = Shot(true,  _stage.ToString().ToLowerInvariant() + "_post");
            _lastShot = post;
            Debug.Log("[PostFxLab] A/B:\n  " + raw + "\n  " + post);
        }

        private string Shot(bool post, string name)
        {
            const int size = 512;
            RenderTexture rt = RenderPreview(size, size, post);
            if (rt == null) return "<нет кадра>";

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Directory.CreateDirectory(ShotFolder);
            string file = Path.GetFullPath(Path.Combine(ShotFolder, name + ".png"));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            DestroyImmediate(tex);
            return file;
        }

        /// <summary>
        /// Подобранное свечение уезжает в feel-конфиг: играет ассет, а не ползунок стенда, и забытая ручка
        /// иначе живёт только здесь.
        /// </summary>
        private void WriteGlowToConfig()
        {
            var feel = AssetDatabase.LoadAssetAtPath<Design.CombatFeelConfig>(FeelConfigPath);
            if (feel == null) { Debug.LogError("[PostFxLab] не найден CombatFeelConfig: " + FeelConfigPath); return; }

            var so = new SerializedObject(feel);
            so.FindProperty("_castGlowFlatness").floatValue      = _glowFlatness;
            so.FindProperty("_castGlowBloomIntensity").floatValue = _glowBloom;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(feel);
            AssetDatabase.SaveAssetIfDirty(feel);
            Debug.Log($"[PostFxLab] в конфиг записано: плоскость {_glowFlatness:0.##}, множитель bloom {_glowBloom:0.##}");
        }
    }
}
