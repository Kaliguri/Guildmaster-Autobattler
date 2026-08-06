using System.IO;
using Cysharp.Threading.Tasks;
using Guildmaster.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Лестница ГРОМКОСТИ: одна и та же панель на трёх насыщенностях фона.
    /// </summary>
    /// <remarks>
    /// <b>Зачем.</b> Замер палитры 06.08.2026 показал, что вся тёплая половина сидит на одном тоне
    /// 30-44°: <c>ink</c> — это <c>brass</c> при насыщенности 18% вместо 55%. То есть «фон должен
    /// стать приглушённой латунью» у нас уже выполнено, и единственная ручка, которой крутится
    /// громкость интерфейса, — НАСЫЩЕННОСТЬ фона. Тон и светлота при этом не двигаются, поэтому
    /// контраст текста и все состояния кнопок остаются на месте.
    ///
    /// <para><b>Почему кадром, а не описанием.</b> «Приглушённо», «средне», «комиксово» — слова,
    /// под которыми каждый видит своё. Три ступени рядом на одном кадре превращают выбор в
    /// указание пальцем. Тот же приём, что с типографикой: замер вместо «на глаз».</para>
    ///
    /// <para><b>Фон задаётся ИНЛАЙНОМ, а не правкой токенов.</b> Проба обязана быть обратимой:
    /// правка рампы разъехалась бы по всей игре, и откатывать пришлось бы диффом. Здесь же
    /// эксперимент живёт ровно в этом кадре.</para>
    /// </remarks>
    public static class UiColourLadder
    {
        public const string OutputPath = "docs/lab/assets/ui-states/colour-ladder.png";

        /// <summary>Ступени насыщенности: как сейчас, вдвое звонче, комикс.</summary>
        private static readonly (string Label, int Saturation)[] Steps =
        {
            ("тихо · 18% (как сейчас)", 18),
            ("средне · 26%",            26),
            ("комикс · 35%",            35),
        };

        /// <summary>Светлоты ступеней ink, снятые с живой рампы: 900 / 800 / 700 / 600.</summary>
        private static readonly float[] Levels = { 0.15f, 0.18f, 0.21f, 0.26f };

        private const float InkHue = 32f;
        private const int SettleFrames = 40;

        public static async UniTask<string> Capture(MonoBehaviour runner)
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[ColourLadder] Нужен play mode: панель живёт только в игре.");
                return null;
            }

            var bootstrap = Object.FindAnyObjectByType<Guildmaster.UI.UiRootBootstrap>();
            UIDocument document = bootstrap != null ? bootstrap.GetComponent<UIDocument>() : null;
            if (document == null || document.rootVisualElement == null)
            {
                Debug.LogError("[ColourLadder] Не найден UIDocument игрового интерфейса.");
                return null;
            }

            VisualElement root = document.rootVisualElement;
            var sheet = new VisualElement { name = "colour-ladder" };
            sheet.AddToClassList("gm-sheet");
            sheet.style.flexDirection = FlexDirection.Row;
            root.Add(sheet);

            try
            {
                for (int i = 0; i < Steps.Length; i++) sheet.Add(Column(Steps[i].Label, Steps[i].Saturation));

                await UniTask.DelayFrame(SettleFrames);
                await UniTask.WaitForEndOfFrame(runner);

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(OutputPath, frame.EncodeToPNG()); }
                finally { Object.Destroy(frame); }
            }
            finally
            {
                sheet.RemoveFromHierarchy();
            }

            Debug.Log($"[ColourLadder] Снято → {Path.GetFullPath(OutputPath)}");
            return OutputPath;
        }

        /// <summary>
        /// Колонка одной ступени: подложка экрана, панель на ней и обычный набор органов управления.
        /// </summary>
        /// <remarks>
        /// Внутри — то, что несёт вес на любом экране: заголовок, тело, обычная кнопка, главная,
        /// выключенная. Меньше набор — и не увидишь, как звонкость фона съедает разницу между
        /// обычной кнопкой и главной; больше — кадр перестанет читаться.
        /// </remarks>
        private static VisualElement Column(string label, int saturation)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.paddingLeft = 24;
            column.style.paddingRight = 24;
            column.style.paddingTop = 24;
            column.style.paddingBottom = 24;
            column.style.backgroundColor = Ink(0.15f, saturation);   // подложка экрана

            var caption = new Label(label);
            caption.AddToClassList("gm-sheet__title");
            column.Add(caption);

            var panel = new VisualElement();
            panel.AddToClassList("gm-panel");
            panel.style.backgroundColor = Ink(0.18f, saturation);    // поверхность панели
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            column.Add(panel);

            var title = new Label("Настройки");
            title.AddToClassList("gm-text-title");
            panel.Add(title);

            var body = new Label("Пояснение под заголовком — то, что читают вторым.");
            body.AddToClassList("gm-text-caption");
            body.AddToClassList("gm-text--muted");
            panel.Add(body);

            panel.Add(Button("Отмена", null, saturation));
            panel.Add(Button("Сохранить", "gm-button--primary", saturation));

            PlateButton off = Button("Недоступно", null, saturation);
            off.SetEnabled(false);
            panel.Add(off);

            return column;
        }

        private static PlateButton Button(string text, string role, int saturation)
        {
            var button = new PlateButton { text = text };
            button.AddToClassList("gm-button");
            if (role != null) button.AddToClassList(role);

            // Поле кнопки идёт на ступень СВЕТЛЕЕ панели — по решению Макса 06.08.2026 обычная
            // кнопка отделяется от латунного фона именно светлотой, а не сменой тона.
            button.style.marginTop = 12;
            button.style.width = 260;
            return button;
        }

        /// <summary>
        /// Ступень рампы ink при заданной насыщенности. Тон 32° — замер живой палитры.
        /// </summary>
        /// <remarks>
        /// Считается через HSL, а не через <c>Color.HSVToRGB</c>: у Unity здесь ДРУГАЯ модель (HSV),
        /// и «насыщенность 18%» в ней означает не то же самое. Проверено сверкой: формула ниже на
        /// S=18% воспроизводит нынешние значения рампы байт в байт.
        /// </remarks>
        private static Color Ink(float lightness, int saturationPercent)
            => FromHsl(InkHue, saturationPercent / 100f, lightness);

        private static Color FromHsl(float h, float s, float l)
        {
            float c = (1f - Mathf.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Mathf.Abs((h / 60f) % 2f - 1f));
            float m = l - c / 2f;

            float r = 0f, g = 0f, b = 0f;
            if (h < 60f)       { r = c; g = x; }
            else if (h < 120f) { r = x; g = c; }
            else if (h < 180f) { g = c; b = x; }
            else if (h < 240f) { g = x; b = c; }
            else if (h < 300f) { r = x; b = c; }
            else               { r = c; b = x; }

            return new Color(r + m, g + m, b + m);
        }
    }
}
