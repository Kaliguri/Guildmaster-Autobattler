#if UNITY_EDITOR
using System;
using System.IO;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.UI.EditorTools
{
    /// <summary>
    /// Печёт ФАКТУРЫ панелей интерфейса кодом и кладёт их в <c>Art/UI/Panels</c> готовыми к 9-slice.
    /// </summary>
    /// <remarks>
    /// Зачем инструмент, а не нарисованные картинки: UI-художника у нас нет, а плоская заливка давала
    /// ровно ту претензию, с которой начался заход 2026-08-02 — «прямоугольник без идеи и текстуры».
    /// Процедура закрывает разрыв: поверхность получает волокно, разводы и затёртый край, а правится
    /// это числом, а не кистью.
    /// <para>ГРАНИЦА ПРИМЕНЕНИЯ (правило захода): <b>математика делает материал и форму, человек —
    /// смысл</b>. Здесь пекутся ПОВЕРХНОСТИ. Иконки, портреты, знаки и иллюстрации не генерируются
    /// никогда: это язык, а не текстура, и их делает человек или PixelLab.</para>
    /// <para>Цвет берётся из <see cref="GuildmasterPalette"/> по имени токена, а не задаётся в окне:
    /// палитра — единый источник правды, и запечённый ассет обязан ехать за ней. Хочешь другой тон —
    /// меняешь токен и перепекаешь, а не подбираешь цвет заново.</para>
    /// <para>Результат ДЕТЕРМИНИРОВАН по сиду: тот же сид и те же параметры дают тот же файл, иначе
    /// каждый перепёк создавал бы визуальный диф на ровном месте.</para>
    /// </remarks>
    public sealed class PanelTextureBakerWindow : EditorWindow
    {
        /// <summary>Материал поверхности — правило, по которому считается фактура.</summary>
        private enum Surface
        {
            /// <summary>Пергамент: волокно, разводы, затёртая кромка.</summary>
            Parchment = 0,

            /// <summary>Дерево: волокно вытянуто вдоль оси, поперёк идут годовые кольца.</summary>
            Wood = 1,

            /// <summary>Штукатурка/камень: крупные пятна плюс мелкое зерно, без направления.</summary>
            Plaster = 2,

            /// <summary>Матовая пластина: полупрозрачное тёмное поле, свет сверху, фаска по кромке.</summary>
            MattePane = 3,

            /// <summary>Холст: переплетение нитей — две решётки поперёк друг друга.</summary>
            Linen = 4,
        }

        private const string OutputFolder = "Assets/_Project/Art/UI/Panels";
        private const string PalettePath = "Assets/_Project/ScriptableObjects/Configs/GuildmasterPalette.asset";

        [SerializeField] private Surface _surface = Surface.Parchment;
        [SerializeField] private string _fileName = "PanelParchment";
        [SerializeField] private int _size = 512;
        [SerializeField] private int _seed = 1;

        [Tooltip("Имя токена палитры для основы. Пусто — тело панели.")]
        [SerializeField] private string _baseToken = "--gm-color-surface-panel";

        [Tooltip("Насколько фактура отличается от ровной заливки. 0 — плоско, 1 — грубо.")]
        [Range(0f, 1f)] [SerializeField] private float _contrast = 0.45f;

        [Tooltip("Крупность рисунка: меньше — крупнее пятна.")]
        [Range(1f, 24f)] [SerializeField] private float _scale = 6f;

        [Tooltip("Затёртость кромки: насколько край темнее середины.")]
        [Range(0f, 1f)] [SerializeField] private float _edgeWear = 0.3f;

        [Tooltip("Свет, падающий сверху. Для пластины даёт блик, для бумаги — подсвеченный верх листа.")]
        [Range(0f, 1f)] [SerializeField] private float _topLight = 0.25f;

        [Tooltip("Непрозрачность основы. <1 нужен мета-слою: сквозь панель виден мир.")]
        [Range(0.1f, 1f)] [SerializeField] private float _alpha = 1f;

        private Texture2D _preview;

        [MenuItem("Alebardium/UI/Bake Panel Textures", priority = 300)]
        private static void Open() => GetWindow<PanelTextureBakerWindow>("Фактуры панелей");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Поверхность", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _surface = (Surface)EditorGUILayout.EnumPopup("Материал", _surface);
            _fileName = EditorGUILayout.TextField("Имя файла", _fileName);
            _size = EditorGUILayout.IntPopup("Размер", _size,
                new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
            _seed = EditorGUILayout.IntField("Сид", _seed);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Вид", EditorStyles.boldLabel);
            _baseToken = EditorGUILayout.TextField("Токен основы", _baseToken);
            _contrast = EditorGUILayout.Slider("Контраст фактуры", _contrast, 0f, 1f);
            _scale = EditorGUILayout.Slider("Крупность", _scale, 1f, 24f);
            _edgeWear = EditorGUILayout.Slider("Затёртость края", _edgeWear, 0f, 1f);
            _topLight = EditorGUILayout.Slider("Свет сверху", _topLight, 0f, 1f);
            _alpha = EditorGUILayout.Slider("Непрозрачность", _alpha, 0.1f, 1f);
            bool changed = EditorGUI.EndChangeCheck();

            if (changed || _preview == null) RebuildPreview();

            EditorGUILayout.Space();
            Rect r = GUILayoutUtility.GetRect(220f, 220f, GUILayout.ExpandWidth(false));
            if (_preview != null) EditorGUI.DrawPreviewTexture(r, _preview);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Запечь", GUILayout.Height(28f))) Bake(_surface, _fileName);
                if (GUILayout.Button("Запечь все материалы", GUILayout.Height(28f))) BakeAll();
            }

            EditorGUILayout.HelpBox(
                "Фактуры — поверхности, а не рисунки. Иконки, знаки и портреты здесь не пекутся: " +
                "их делает человек. Цвет приходит из палитры по токену — правь токен, а не окно.",
                MessageType.None);
        }

        private void RebuildPreview()
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = Generate(_surface, 220);
        }

        /// <summary>
        /// Печёт весь набор материалов с ПРЕСЕТНЫМИ ручками, а не с текущими значениями окна: у бумаги
        /// и у пластины разная природа (одна непрозрачна и волокниста, вторая просвечивает и ровна),
        /// и общий набор чисел дал бы половину набора браком. Текущие настройки окна при этом
        /// сохраняются — «запечь все» не должно менять то, с чем возится дизайнер.
        /// </summary>
        private void BakeAll()
        {
            float contrast = _contrast, scale = _scale, wear = _edgeWear, light = _topLight, alpha = _alpha;

            Preset(0.45f, 6f, 0.30f, 0.25f, 1f);    Bake(Surface.Parchment, "PanelParchment");
            Preset(0.40f, 5f, 0.35f, 0.20f, 1f);    Bake(Surface.Wood, "PanelWood");
            Preset(0.35f, 7f, 0.28f, 0.22f, 1f);    Bake(Surface.Plaster, "PanelPlaster");
            Preset(0.18f, 9f, 0.15f, 0.55f, 0.62f); Bake(Surface.MattePane, "PanelMattePane");
            Preset(0.30f, 4f, 0.30f, 0.20f, 1f);    Bake(Surface.Linen, "PanelLinen");

            _contrast = contrast; _scale = scale; _edgeWear = wear; _topLight = light; _alpha = alpha;
        }

        private void Preset(float contrast, float scale, float wear, float light, float alpha)
        {
            _contrast = contrast; _scale = scale; _edgeWear = wear; _topLight = light; _alpha = alpha;
        }

        private void Bake(Surface surface, string fileName)
        {
            Directory.CreateDirectory(OutputFolder);
            Texture2D tex = Generate(surface, _size);
            string path = $"{OutputFolder}/{fileName}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(path);
            Debug.Log($"[PanelTextureBaker] запечено: {path}");
        }

        /// <summary>
        /// Настройки импорта задаём КОДОМ: фактура тянется 9-slice, и любой из этих пунктов, забытый
        /// руками, виден на панели сразу — мип-уровни мылят кромку, повтор рвёт край, сжатие съедает
        /// тонкое зерно ради килобайта.
        /// </summary>
        private static void ApplyImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private Color ResolveBase()
        {
            var palette = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(PalettePath);
            if (palette != null && !string.IsNullOrEmpty(_baseToken) &&
                palette.TryGet(_baseToken, out Color fromPalette))
                return fromPalette;

            // Фолбэк — только на ВНЕШНИЙ отказ (нет ассета палитры или разошлись имена токенов).
            // Молча подставить «похожий» цвет нельзя: фактура уехала бы мимо темы, и заметили бы это
            // на скриншоте, а не здесь.
            Debug.LogWarning($"[PanelTextureBaker] токен '{_baseToken}' не найден в палитре — беру дерево по умолчанию.");
            return new Color(28f / 255f, 20f / 255f, 13f / 255f, 1f);
        }

        private Texture2D Generate(Surface surface, int size)
        {
            Color baseColor = ResolveBase();
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            // Сид сдвигает область выборки шума: Perlin в Unity детерминирован по координате, своего
            // состояния у него нет, поэтому «другой вариант» — это другой участок поля, а не другой RNG.
            float ox = _seed * 37.13f, oy = _seed * 91.71f;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;          // 0 внизу
                    float top = 1f - v;                 // 0 сверху — «свет падает сверху»

                    float k = SurfaceValue(surface, u, v, ox, oy);
                    k = 1f + (k - 0.5f) * 2f * _contrast;

                    // Затёртая кромка: к краю поверхность темнее. Считается по МИНИМАЛЬНОМУ расстоянию
                    // до края, поэтому работает одинаково на всех четырёх сторонах и переживает 9-slice.
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    k *= Mathf.Lerp(1f - _edgeWear, 1f, Mathf.SmoothStep(0f, 0.12f, edge));

                    // Свет сверху добавляется, а не умножается: на тёмной основе умножение ничего не даёт.
                    float light = Mathf.SmoothStep(0f, 0.6f, 1f - top) * _topLight * 0.35f;

                    float a = _alpha;
                    if (surface == Surface.MattePane)
                    {
                        // Фаска пластины: светлая кромка сверху и слева, тень снизу — только так плоское
                        // поле читается предметом, а не заливкой.
                        float rim = Mathf.Max(
                            1f - Mathf.SmoothStep(0f, 0.045f, top),
                            (1f - Mathf.SmoothStep(0f, 0.045f, u)) * 0.6f);
                        light += rim * 0.35f;
                        a = Mathf.Clamp01(_alpha + rim * 0.35f);
                    }

                    var c = new Color(
                        Mathf.Clamp01(baseColor.r * k + light * 1.10f),
                        Mathf.Clamp01(baseColor.g * k + light * 0.95f),
                        Mathf.Clamp01(baseColor.b * k + light * 0.72f),
                        a);
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Значение фактуры 0..1 в точке. Здесь и живёт разница между материалами.</summary>
        private float SurfaceValue(Surface surface, float u, float v, float ox, float oy)
        {
            switch (surface)
            {
                case Surface.Parchment:
                    // Волокно бумаги — сильно вытянутый шум; поверх крупные разводы.
                    return 0.5f
                         + (Fbm(u * _scale + ox, v * _scale + oy, 4) - 0.5f) * 0.9f
                         + (Mathf.PerlinNoise(u * 220f + ox, v * 7f + oy) - 0.5f) * 0.35f;

                case Surface.Wood:
                    // Годовые кольца: шум задаёт изгиб слоя, синус превращает его в полосы.
                    float rings = Mathf.Sin((v * 26f + Fbm(u * 3f + ox, v * 2f + oy, 3) * 9f) * Mathf.PI);
                    return 0.5f + rings * 0.35f + (Fbm(u * 90f + ox, v * 12f + oy, 2) - 0.5f) * 0.3f;

                case Surface.Plaster:
                    return 0.5f
                         + (Fbm(u * _scale * 0.6f + ox, v * _scale * 0.6f + oy, 5) - 0.5f) * 1.1f
                         + (Mathf.PerlinNoise(u * 260f + ox, v * 260f + oy) - 0.5f) * 0.25f;

                case Surface.Linen:
                    // Переплетение: две решётки поперёк друг друга, разной фазы — узел ткани.
                    float warp = Mathf.Sin(u * _scale * 26f) * 0.5f + 0.5f;
                    float weft = Mathf.Sin(v * _scale * 26f + 1.6f) * 0.5f + 0.5f;
                    return 0.5f + (warp * weft - 0.25f) * 0.8f
                         + (Fbm(u * 6f + ox, v * 6f + oy, 3) - 0.5f) * 0.5f;

                case Surface.MattePane:
                default:
                    // Матовость — мелкое ровное зерно: у стекла нет рисунка, есть шероховатость.
                    return 0.5f + (Fbm(u * _scale * 2f + ox, v * _scale * 2f + oy, 3) - 0.5f) * 0.5f;
            }
        }

        /// <summary>Несколько октав Perlin: одна даёт размытое пятно, четыре — материал.</summary>
        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, frq = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Mathf.PerlinNoise(x * frq, y * frq) * amp;
                norm += amp;
                frq *= 2.07f;
                amp *= 0.5f;
            }
            return norm > 0f ? sum / norm : 0.5f;
        }

        private void OnDisable()
        {
            if (_preview != null) DestroyImmediate(_preview);
        }
    }
}
#endif
