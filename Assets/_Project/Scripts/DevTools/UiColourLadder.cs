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

        /// <summary>Кадр второй лестницы — по СВЕТЛОТЕ фона.</summary>
        public const string LightnessOutputPath = "docs/lab/assets/ui-states/lightness-ladder.png";

        /// <summary>
        /// Ступени светлоты панели. Слева — как сейчас, дальше на ступень и на две вниз по рампе.
        /// </summary>
        /// <remarks>
        /// Числа сняты с живой рампы <c>ink</c>: 700 = 21%, 800 = 18%, 900 = 15%. Заказ Макса
        /// 07.08.2026: «Фон (именно фон!) должен быть темнее. А то все сливается».
        /// </remarks>
        private static readonly (string Label, float Lightness)[] LightnessSteps =
        {
            ("как сейчас · 21%", 0.21f),
            ("темнее · 18%",     0.18f),
            ("глубоко · 15%",    0.15f),
        };

        /// <summary>Насыщенность, принятая 06.08.2026. В этой лестнице она НЕ крутится.</summary>
        private const int AcceptedSaturation = 26;

        /// <summary>На сколько кнопка светлее панели в пробе «кнопка на своей ступени».</summary>
        /// <remarks>
        /// 0.06 — это ровно две ступени рампы (700→600 идёт 21% → 26%). Одна ступень (0.03) на
        /// кадре 06.08 не отличала кнопку от подложки настолько, чтобы спор закончился.
        /// </remarks>
        private const float ButtonLift = 0.06f;

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
        /// Лестница СВЕТЛОТЫ: три глубины фона, и в каждой — кнопка на фоне против кнопки на своей
        /// ступени.
        /// </summary>
        /// <remarks>
        /// Отвечает на два вопроса одним кадром, потому что они связаны. Первый — насколько темнее
        /// должен быть фон. Второй, найденный замером 07.08.2026: <c>.gm-button</c> красится ТЕМ ЖЕ
        /// токеном, что и панель под ней (<c>surface-panel</c>, rgb 67,54,40), то есть кнопка и
        /// подложка буквально одного цвета и различаются только каймой. Одно затемнение фона этого
        /// не лечит: кнопка уедет вместе с ним. Поэтому в каждой колонке стоят обе пробы — как
        /// сейчас и как предлагается, — и видно, что даёт глубина, а что разведение ступеней.
        /// </remarks>
        public static async UniTask<string> CaptureLightness(MonoBehaviour runner)
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[LightnessLadder] Нужен play mode: панель живёт только в игре.");
                return null;
            }

            var bootstrap = Object.FindAnyObjectByType<Guildmaster.UI.UiRootBootstrap>();
            UIDocument document = bootstrap != null ? bootstrap.GetComponent<UIDocument>() : null;
            if (document == null || document.rootVisualElement == null)
            {
                Debug.LogError("[LightnessLadder] Не найден UIDocument игрового интерфейса.");
                return null;
            }

            VisualElement root = document.rootVisualElement;
            var sheet = new VisualElement { name = "lightness-ladder" };
            sheet.AddToClassList("gm-sheet");
            sheet.style.flexDirection = FlexDirection.Row;
            root.Add(sheet);

            try
            {
                for (int i = 0; i < LightnessSteps.Length; i++)
                    sheet.Add(LightnessColumn(LightnessSteps[i].Label, LightnessSteps[i].Lightness));

                await UniTask.DelayFrame(SettleFrames);
                await UniTask.WaitForEndOfFrame(runner);

                Directory.CreateDirectory(Path.GetDirectoryName(LightnessOutputPath) ?? ".");
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(LightnessOutputPath, frame.EncodeToPNG()); }
                finally { Object.Destroy(frame); }
            }
            finally
            {
                sheet.RemoveFromHierarchy();
            }

            Debug.Log($"[LightnessLadder] Снято → {Path.GetFullPath(LightnessOutputPath)}");
            return LightnessOutputPath;
        }

        private static VisualElement LightnessColumn(string label, float lightness)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.paddingLeft = 24;
            column.style.paddingRight = 24;
            column.style.paddingTop = 24;
            column.style.paddingBottom = 24;
            // Подложка экрана — на ступень ниже панели, как в теме: панель обязана читаться
            // поднятой над полем, иначе проба сравнивает не то.
            column.style.backgroundColor = Ink(Mathf.Max(0.08f, lightness - 0.03f), AcceptedSaturation);

            var caption = new Label(label);
            caption.AddToClassList("gm-sheet__title");
            column.Add(caption);

            column.Add(LightnessPanel("кнопка = фон (как сейчас)", lightness, lightness));
            column.Add(LightnessPanel("кнопка на своей ступени", lightness, lightness + ButtonLift));
            return column;
        }

        private static VisualElement LightnessPanel(string note, float panelLightness, float buttonLightness)
        {
            var panel = new VisualElement();
            panel.AddToClassList("gm-panel");
            panel.style.backgroundColor = Ink(panelLightness, AcceptedSaturation);
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            panel.style.paddingTop = 16;
            panel.style.paddingBottom = 16;
            panel.style.marginBottom = 20;

            var caption = new Label(note);
            caption.AddToClassList("gm-text-label");
            caption.AddToClassList("gm-text--muted");
            panel.Add(caption);

            PlateButton plain = Button("Ристалище", null, AcceptedSaturation);
            plain.style.backgroundColor = Ink(buttonLightness, AcceptedSaturation);
            panel.Add(plain);

            // Главная кнопка живёт патиной и от светлоты фона не зависит — она в кадре как якорь:
            // по ней видно, не съела ли глубина фона разницу между «обычной» и «главной».
            panel.Add(Button("Кампания", "gm-button--primary", AcceptedSaturation));
            return panel;
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
