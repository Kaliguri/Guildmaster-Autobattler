#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Front end for <see cref="AnimationLabRenderer"/>: pick a rig and a clip, get a PNG to look at.
    /// The rig and the last settings are remembered per project, so repeat passes are one click.
    /// </summary>
    public sealed class AnimationLabWindow : EditorWindow
    {
        const string RigKey = "Guildmaster.AnimationLab.Rig";
        const string FramesKey = "Guildmaster.AnimationLab.Frames";
        const string ColumnsKey = "Guildmaster.AnimationLab.Columns";
        const string CellKey = "Guildmaster.AnimationLab.Cell";

        GameObject _rig;
        AnimationClip _clip;
        int _frames;
        int _columns = 6;
        int _cellSize = 192;
        float _padding = 1.15f;
        Texture2D _preview;
        string _status;

        [MenuItem("Alebardium/Animation/Animation Lab", priority = 600)]
        public static void Open()
        {
            GetWindow<AnimationLabWindow>("Animation Lab").minSize = new Vector2(360f, 420f);
        }

        void OnEnable()
        {
            string rigPath = EditorPrefs.GetString(RigKey, string.Empty);
            if (!string.IsNullOrEmpty(rigPath)) _rig = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
            _frames = EditorPrefs.GetInt(FramesKey, 0);
            _columns = EditorPrefs.GetInt(ColumnsKey, 6);
            _cellSize = EditorPrefs.GetInt(CellKey, 192);
            PickClipFromSelection();
        }

        void OnSelectionChange()
        {
            if (PickClipFromSelection()) Repaint();
        }

        bool PickClipFromSelection()
        {
            var clip = Selection.activeObject as AnimationClip;
            if (clip == null || clip == _clip) return false;
            _clip = clip;
            return true;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _rig = (GameObject)EditorGUILayout.ObjectField("Rig prefab", _rig, typeof(GameObject), false);
            _clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", _clip, typeof(AnimationClip), false);
            EditorGUILayout.HelpBox("Selecting a clip in the Project window fills the Clip field.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            _frames = EditorGUILayout.IntField(new GUIContent("Frames", "0 = every clip frame, capped at 24"), _frames);
            _columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", _columns));
            _cellSize = Mathf.Max(32, EditorGUILayout.IntField("Cell size (px)", _cellSize));
            _padding = EditorGUILayout.Slider("Padding", _padding, 1f, 2f);

            if (_clip != null)
            {
                float rate = _clip.frameRate > 0f ? _clip.frameRate : 60f;
                EditorGUILayout.LabelField($"{_clip.length:F2}s at {rate:F0} fps = {Mathf.RoundToInt(_clip.length * rate) + 1} frames");
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_rig == null || _clip == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Contact sheet", GUILayout.Height(28f))) Run(onionSkin: false);
                    if (GUILayout.Button("Onion skin", GUILayout.Height(28f))) Run(onionSkin: true);
                }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, MessageType.Info);
                if (GUILayout.Button("Reveal output folder"))
                    Process.Start("explorer.exe", Path.GetFullPath(AnimationLabRenderer.DefaultOutputDir).Replace('/', '\\'));
            }

            if (_preview != null)
            {
                EditorGUILayout.Space();
                float width = EditorGUIUtility.currentViewWidth - 24f;
                float height = width * _preview.height / _preview.width;
                var rect = GUILayoutUtility.GetRect(width, height);
                GUI.DrawTexture(rect, _preview, ScaleMode.ScaleToFit);
            }
        }

        void Run(bool onionSkin)
        {
            EditorPrefs.SetString(RigKey, AssetDatabase.GetAssetPath(_rig));
            EditorPrefs.SetInt(FramesKey, _frames);
            EditorPrefs.SetInt(ColumnsKey, _columns);
            EditorPrefs.SetInt(CellKey, _cellSize);

            var options = new AnimationLabRenderer.Options
            {
                Frames = _frames,
                Columns = _columns,
                CellSize = _cellSize,
                Padding = _padding
            };

            try
            {
                var result = onionSkin
                    ? AnimationLabRenderer.RenderOnionSkin(_rig, _clip, options)
                    : AnimationLabRenderer.RenderContactSheet(_rig, _clip, options);
                _status = result.ToString();
                LoadPreview(result.Path);
                UnityEngine.Debug.Log("[AnimationLab] " + result);
            }
            catch (System.Exception e)
            {
                _status = "Failed: " + e.Message;
                UnityEngine.Debug.LogException(e);
            }
        }

        void LoadPreview(string path)
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _preview.LoadImage(File.ReadAllBytes(path));
        }

        void OnDisable()
        {
            if (_preview != null) DestroyImmediate(_preview);
        }
    }
}
#endif
