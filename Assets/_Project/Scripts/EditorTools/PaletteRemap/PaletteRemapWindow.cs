using System.Collections.Generic;
using System.IO;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.PaletteRemap
{
    /// <summary>
    /// Alebardium Palette Remapper — перекрашивает растровые (пиксель-арт) спрайты в нашу палитру
    /// методом <b>gradient-map по яркости</b>: яркость каждого пикселя отображается в рампу
    /// <c>ink → brass → parchment</c> (дефолт — прямо из <c>tokens.primitives.uss</c>). В отличие от
    /// плоского Unity-tint (один цвет) градиент-мап сохраняет светотень и потому даёт двухцветный
    /// результат (тёмный фон + латунный кант) из одного прохода — если исходник имеет контраст по
    /// яркости (Honeti Gold-кнопки с bevel, панели со stroke).
    /// <para>Родствен Figma-плагину Alebardium Tint Exporter по идее, но работает с РАСТРОМ (не
    /// вектором) и даёт многоцветную рампу (не grayscale-маску под tint). Editor-only, ничего не
    /// тащит в рантайм.</para>
    /// </summary>
    public sealed class PaletteRemapWindow : EditorWindow
    {
        // Рампа перекраски. Дефолт задаётся из наших токенов в OnEnable.
        [SerializeField] private Gradient _ramp;
        // Нормализовать яркость по фактическому диапазону спрайта (min..max) перед мапом в рампу —
        // иначе спрайт, не покрывающий весь 0..1, ляжет лишь на кусок рампы.
        [SerializeField] private bool _normalize = true;
        // Порог непрозрачности: пиксели тусклее — считаются фоном и не красятся.
        [SerializeField] private float _alphaThreshold = 0.01f;
        // Настроить импорт результата как pixel-perfect Sprite (Point, без компрессии, без мипов).
        [SerializeField] private bool _applyPixelImport = true;
        [SerializeField] private string _outputFolder = "Assets/_Project/Art/UI/Honeti-gm";
        [SerializeField] private string _suffix = "_gm";

        private Vector2 _scroll;

        [MenuItem("Alebardium/Palette Remapper", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<PaletteRemapWindow>("Palette Remapper");
            w.minSize = new Vector2(360, 420);
        }

        private void OnEnable()
        {
            if (_ramp == null || _ramp.colorKeys == null || _ramp.colorKeys.Length == 0)
                _ramp = BuildGuildmasterRamp();
        }

        /// <summary>Снимок палитры проекта — тот же, что читают карта и боевой UI.</summary>
        private const string PalettePath = "Assets/_Project/ScriptableObjects/Configs/GuildmasterPalette.asset";

        // Ступени рампы: токен палитры → позиция по яркости. Ни цветов, ни разбора USS здесь нет.
        // Копия цветов ушла раньше (UA-21), собственный парсер токенов — сейчас: читать палитру
        // умеет PaletteSnapshotBuilder, и второй такой же разбор рядом — тот же дубль, только логики.
        private static readonly (string Token, float Position)[] RampStops =
        {
            ("--gm-ink-900",       0.00f),
            ("--gm-ink-600",       0.30f),
            ("--gm-ink-300",       0.50f),
            ("--gm-brass-700",     0.65f),
            ("--gm-brass-500",     0.80f),
            ("--gm-brass-300",     0.92f),
            ("--gm-parchment-100", 1.00f),
        };

        /// <summary>
        /// Дефолтная рампа Guildmaster из палитры проекта. Роли нет в снимке — ступень пропускается,
        /// и об этом говорится вслух: молча подставить «похожий» цвет значило бы перекрасить арт мимо
        /// палитры, а это ровно то, ради чего тул и существует.
        /// </summary>
        private static Gradient BuildGuildmasterRamp()
        {
            var palette = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(PalettePath);
            var keys = new List<GradientColorKey>(RampStops.Length);
            var missing = new List<string>();

            if (palette == null)
            {
                Debug.LogError($"[PaletteRemap] - нет снимка палитры {PalettePath}. Собери его: " +
                               "Alebardium → Дизайн-система → Пересобрать палитру.");
            }
            else
            {
                foreach ((string token, float position) in RampStops)
                {
                    if (palette.TryGet(token, out Color c)) keys.Add(new GradientColorKey(c, position));
                    else missing.Add(token);
                }

                if (missing.Count > 0)
                    Debug.LogError($"[PaletteRemap] - в палитре нет токенов: {string.Join(", ", missing)}. " +
                                   "Рампа собрана без них — пересобери снимок или проверь имена.");
            }

            var g = new Gradient();
            if (keys.Count > 0) g.colorKeys = keys.ToArray();
            g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            return g;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Gradient-map по яркости в нашу палитру. Тёмные пиксели → низ рампы (ink), " +
                "светлые → верх (brass/parchment). Выдели спрайты в Project и нажми Remap.",
                MessageType.Info);

            _ramp = EditorGUILayout.GradientField("Рампа (ink → brass → parch)", _ramp);
            if (GUILayout.Button("Сбросить рампу к токенам Guildmaster"))
                _ramp = BuildGuildmasterRamp();

            _normalize = EditorGUILayout.Toggle(
                new GUIContent("Нормализовать яркость", "Растянуть min..max яркости спрайта на всю рампу"),
                _normalize);
            _alphaThreshold = EditorGUILayout.Slider("Порог alpha", _alphaThreshold, 0f, 0.5f);
            _applyPixelImport = EditorGUILayout.Toggle(
                new GUIContent("Импорт как pixel Sprite", "Point-фильтр, без компрессии, без мипов"),
                _applyPixelImport);
            _outputFolder = EditorGUILayout.TextField("Папка вывода", _outputFolder);
            _suffix = EditorGUILayout.TextField("Суффикс имени", _suffix);

            EditorGUILayout.Space();

            var textures = SelectedTextures();
            EditorGUILayout.LabelField($"Выделено текстур: {textures.Count}", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(textures.Count == 0))
            {
                if (GUILayout.Button($"Remap выделенных ({textures.Count})", GUILayout.Height(32)))
                    RemapAll(textures);
            }

            EditorGUILayout.EndScrollView();
        }

        private static List<string> SelectedTextures()
        {
            var paths = new List<string>();
            foreach (var obj in Selection.objects)
            {
                var p = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(p)) continue;
                if (obj is Texture2D) { paths.Add(p); continue; }
                // Папка → все текстуры внутри.
                if (AssetDatabase.IsValidFolder(p))
                    foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { p }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(g));
            }
            return paths;
        }

        private void RemapAll(List<string> paths)
        {
            if (!AssetDatabase.IsValidFolder(_outputFolder))
                CreateFolderRecursive(_outputFolder);

            int done = 0;
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string src = paths[i];
                    EditorUtility.DisplayProgressBar("Palette Remap", Path.GetFileName(src), (float)i / paths.Count);
                    if (RemapOne(src)) done++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[PaletteRemap] Перекрашено {done}/{paths.Count} → {_outputFolder}");
        }

        private bool RemapOne(string srcPath)
        {
            // Читаем raw-байты и грузим в readable-копию — не зависим от isReadable исходного импорта.
            byte[] raw = File.ReadAllBytes(srcPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(raw)) { Object.DestroyImmediate(tex); return false; }

            Color[] px = tex.GetPixels();

            // Диапазон яркости среди непрозрачных пикселей (для нормализации).
            float min = 1f, max = 0f;
            if (_normalize)
            {
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < _alphaThreshold) continue;
                    float l = px[i].grayscale;
                    if (l < min) min = l;
                    if (l > max) max = l;
                }
                if (max <= min) { min = 0f; max = 1f; } // плоский спрайт — не делим на ноль
            }

            for (int i = 0; i < px.Length; i++)
            {
                float a = px[i].a;
                if (a < _alphaThreshold) { px[i] = new Color(0, 0, 0, 0); continue; }
                float l = px[i].grayscale;
                if (_normalize) l = Mathf.InverseLerp(min, max, l);
                Color mapped = _ramp.Evaluate(Mathf.Clamp01(l));
                mapped.a = a; // сохраняем исходную прозрачность (антиалиас краёв цел)
                px[i] = mapped;
            }

            var outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            outTex.SetPixels(px);
            outTex.Apply();

            string outPath = $"{_outputFolder}/{Path.GetFileNameWithoutExtension(srcPath)}{_suffix}.png";
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(outTex);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            if (_applyPixelImport) ApplyPixelSpriteImport(outPath);
            return true;
        }

        // Пиксель-арт импорт: Sprite, Point-фильтр, без компрессии/мипов — острые пиксели при апскейле.
        private static void ApplyPixelSpriteImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        private static void CreateFolderRecursive(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
