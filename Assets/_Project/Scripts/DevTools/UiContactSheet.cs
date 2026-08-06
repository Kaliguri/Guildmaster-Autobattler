using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Guildmaster.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Контактный лист интерфейса: снимает КАЖДЫЙ элемент из <see cref="UiComponentRegistry"/> во ВСЕХ
    /// его состояниях — наведение, нажатие, фокус, выключенность, отмеченность — и складывает кадры в
    /// <c>Temp/ContactSheet</c>.
    /// </summary>
    /// <remarks>
    /// <b>Зачем.</b> Состояния интерактивных элементов невозможно увидеть на обычном скриншоте: их надо
    /// чем-то вызвать, а форсировать псевдосостояние в UI Toolkit Debugger нельзя — это дыра самого
    /// Unity, а не пробел процедуры. Пока листа не было, регрессию состояния первым замечал Макс, и ни
    /// разу наоборот. Лист даёт и мою самопроверку до показа, и его приёмку одним взглядом.
    ///
    /// <para><b>Почему из ЖИВОЙ игры, а не со стенда.</b> Решение Макса от 05.08.2026: изолированный
    /// стенд отвечал на вопрос «как это выглядит само по себе», а вопрос стоит «как это выглядит в
    /// игре» — на живом фоне, при своей подложке, в честном масштабе. Оттого лист рисуется поверх
    /// реальной панели (<see cref="UIDocument"/> из сцены) и снимается кадром экрана.</para>
    ///
    /// <para><b>Как форсируются состояния.</b> <c>VisualElement.pseudoStates</c> — internal, но
    /// записываемое (проверено на 6000.4.8f1: enum <c>None/Active/Hover/Checked/Disabled/Focus/Root</c>).
    /// Выключенность ставится НЕ форсом, а честным <c>SetEnabled(false)</c>: движок сам поднимет и
    /// псевдосостояние, и класс <c>.unity-disabled</c>, а половина наших правил висит именно на
    /// классе — форс показал бы картину, которой в игре не бывает.</para>
    ///
    /// <para><b>Готча раскладки.</b> Псевдосостояние сбрасывается пересчётом стиля, поэтому оно
    /// ставится ПОСЛЕ того, как раскладка улеглась, и повторно — прямо перед захватом кадра.</para>
    /// </remarks>
    public static class UiContactSheet
    {
        /// <summary>
        /// Куда складываются кадры: в Лабораторию, раздел «Элементы интерфейса».
        /// </summary>
        /// <remarks>
        /// Лежат В РЕПОЗИТОРИИ, а не в <c>Temp</c> (заказ Макса 06.08.2026: «чтобы я всегда мог и
        /// отдельные элементы глядеть, смотреть что ты сделала»). Кадры перезаписываются на каждом
        /// прогоне — историю держит git, и она честнее папки со снимками: там видно и что менялось,
        /// и вместе с каким коммитом.
        /// </remarks>
        public const string OutputDir = "docs/lab/assets/ui-states";

        /// <summary>Манифест для стенда: какие кадры сняты, когда и что на них.</summary>
        public const string ManifestPath = "docs/lab/data/ui-states.json";

        /// <summary>
        /// Сколько элементов помещается на один кадр 1080p. Четыре, а не шесть: строка переносится
        /// (широкие пункты меню в один ряд не влезают), и высота строки скачет.
        /// </summary>
        private const int RowsPerFrame = 4;

        /// <summary>
        /// Сколько кадров лист устаивается перед захватом.
        /// </summary>
        /// <remarks>
        /// Двух не хватало, и это ловилось прямым замером: два прогона ПОДРЯД без единой правки
        /// давали три расходящихся кадра. Причина — USS-переходы: правила состояний анимируются, и
        /// захват заставал их на полпути, каждый раз на разном. Пока кадр нестабилен, попиксельное
        /// сравнение врёт в обе стороны — оно и заставило меня однажды «починить» порядок импортов
        /// по шуму. Сорока кадров хватает с запасом на самый долгий наш переход.
        /// </remarks>
        private const int SettleFrames = 40;

        private static readonly PropertyInfo PseudoStatesProperty =
            typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Пары «элемент → состояние, которое ему навязано» — переустанавливаются каждый кадр.</summary>
        private static readonly List<(VisualElement Element, object State)> Forced = new();

        /// <summary>
        /// Образцы текущей страницы: блок → элемент. Нужны, чтобы снять с них ЗАМЕР после раскладки.
        /// </summary>
        /// <remarks>
        /// Вопрос Макса 06.08.2026 был «шрифты + цвет + различные иные особенности», и картинка на
        /// него отвечает наполовину: гарнитуру видно, а её ИМЯ и точный кегль — нет. Замер снимается
        /// с живого элемента (<c>resolvedStyle</c>), то есть показывает то, что реально применилось,
        /// а не то, что написано в правиле, — расхождение между этими двумя нас уже подводило.
        /// </remarks>
        private static readonly Dictionary<string, VisualElement> Measured = new();

        /// <summary>
        /// Снимает лист по всем группам. Возвращает пути сохранённых кадров.
        /// </summary>
        /// <param name="runner">Держатель кадрового ожидания: снимок берётся строго в конце кадра.</param>
        public static async UniTask<IReadOnlyList<string>> Capture(MonoBehaviour runner)
        {
            var saved = new List<string>();

            if (!Application.isPlaying)
            {
                Debug.LogError("[ContactSheet] Нужен play mode: вне игры живой панели не существует, " +
                               "и снимать нечего.");
                return saved;
            }

            UIDocument document = FindGameUi();
            if (document == null || document.rootVisualElement == null)
            {
                Debug.LogError("[ContactSheet] В сцене нет UIDocument — панель интерфейса не найдена.");
                return saved;
            }

            Directory.CreateDirectory(OutputDir);

            VisualElement root = document.rootVisualElement;
            var sheet = new VisualElement { name = "contact-sheet" };
            sheet.AddToClassList("gm-sheet");
            root.Add(sheet);

            var manifest = new StringBuilder();
            manifest.Append("{\n  \"frames\": [\n");

            try
            {
                foreach (UiComponentGroup group in Enum.GetValues(typeof(UiComponentGroup)))
                {
                    List<UiComponentEntry> entries = EntriesOf(group);
                    int pages = Mathf.CeilToInt(entries.Count / (float)RowsPerFrame);

                    for (int page = 0; page < pages; page++)
                    {
                        BuildPage(sheet, group, entries, page);

                        await UniTask.DelayFrame(SettleFrames);
                        ReapplyForced();

                        await UniTask.WaitForEndOfFrame(runner);
                        string file = $"{group.ToString().ToLowerInvariant()}-{page + 1}.png";
                        SaveFrame(Path.Combine(OutputDir, file));
                        saved.Add(Path.Combine(OutputDir, file));

                        AppendFrame(manifest, group, entries, page, file, saved.Count > 1);
                    }
                }

                manifest.Append("\n  ]\n}\n");
                File.WriteAllText(ManifestPath, manifest.ToString());
            }
            finally
            {
                Forced.Clear();
                sheet.RemoveFromHierarchy();
            }

            Debug.Log($"[ContactSheet] Снято кадров: {saved.Count} → {Path.GetFullPath(OutputDir)}");
            return saved;
        }

        /// <summary>
        /// Панель ИГРОВОГО интерфейса — та, что держит <c>UiRootBootstrap</c>.
        /// </summary>
        /// <remarks>
        /// Брать «любой <see cref="UIDocument"/>» нельзя, и это ловилось живьём: в CoreScene их два, и
        /// первым отдавался дев-оверлей «Dev Encounter Panel» с меньшим порядком сортировки. Лист лёг
        /// бы ПОД игровой интерфейс и снял бы кадр, на котором его почти не видно.
        /// </remarks>
        private static UIDocument FindGameUi()
        {
            var bootstrap = UnityEngine.Object.FindAnyObjectByType<Guildmaster.UI.UiRootBootstrap>();
            if (bootstrap != null) return bootstrap.GetComponent<UIDocument>();

            Debug.LogWarning("[ContactSheet] UiRootBootstrap не найден — беру первый UIDocument сцены. " +
                             "Лист может лечь не на ту панель.");
            return UnityEngine.Object.FindAnyObjectByType<UIDocument>();
        }

        /// <summary>
        /// Дописывает в манифест запись о снятом кадре: что за группа, какие элементы и их состояния.
        /// </summary>
        /// <remarks>
        /// Стенд Лаборатории строится ИЗ ДАННЫХ, а не из захардкоженного списка файлов: иначе он
        /// разойдётся с реестром на первом же новом компоненте — ровно так и врала прежняя витрина.
        /// JSON пишется руками, а не сериализатором: три поля не стоят зависимости, а Newtonsoft в
        /// этой сборке пришлось бы тянуть ради одной строки.
        /// </remarks>
        private static void AppendFrame(StringBuilder sb, UiComponentGroup group,
                                        List<UiComponentEntry> entries, int page, string file, bool comma)
        {
            if (comma) sb.Append(",\n");

            int from = page * RowsPerFrame;
            int to   = Mathf.Min(from + RowsPerFrame, entries.Count);

            sb.Append("    { \"group\": \"").Append(group).Append("\", \"file\": \"").Append(file)
              .Append("\", \"elements\": [");

            for (int i = from; i < to; i++)
            {
                if (i > from) sb.Append(", ");
                sb.Append("{ \"label\": \"").Append(Escape(entries[i].Label))
                  .Append("\", \"block\": \"").Append(entries[i].Block)
                  .Append("\", \"states\": \"").Append(entries[i].Required)
                  .Append("\", \"tones\": \"").Append(ToneList(entries[i].Tones))
                  .Append("\", \"type\": \"").Append(Escape(Measure(entries[i].Block)))
                  .Append("\" }");
            }

            sb.Append("] }");
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Метки роли по-русски, в том же порядке, в каком стоят ячейки на кадре.</summary>
        private static string ToneList(UiTextTone tones)
        {
            if (tones == UiTextTone.None) return string.Empty;

            var names = new List<string>();
            foreach (UiTextTone tone in ToneOrder)
            {
                if (tones.HasFlag(tone)) names.Add(ToneOf(tone).Caption);
            }
            return string.Join(", ", names);
        }

        /// <summary>
        /// Замер образца словами: гарнитура, кегль, цвет, разрядка. Пусто, если элемент не текстовый.
        /// </summary>
        /// <remarks>
        /// Читается <c>resolvedStyle</c>, то есть ПРИМЕНИВШЕЕСЯ значение, а не объявленное в правиле.
        /// Цвет печатается числами RGB: имя роли по значению не восстановить (одна ступень рампы
        /// стоит за несколькими ролями), а число сверяется с палитрой напрямую.
        /// </remarks>
        private static string Measure(string block)
        {
            if (!Measured.TryGetValue(block, out VisualElement sample)) return string.Empty;

            // Образец роли, набираемой предком, — это КОНТЕЙНЕР с подписью внутри; мерить надо саму
            // подпись, иначе замер вернёт стиль пустой коробки.
            VisualElement target = sample as Label ?? sample.Q<Label>() ?? sample;

            // Образец из НЕСКОЛЬКИХ строк замеру не поддаётся: у словаря подсказки каждое слово
            // своего цвета, и число под кадром описывало бы первое из шести — то есть врало бы
            // ровно тем способом, ради поимки которого замер и заведён.
            if (target != sample && sample.Query<Label>().ToList().Count > 1) return string.Empty;
            IResolvedStyle style = target.resolvedStyle;
            string face = style.unityFontDefinition.fontAsset != null
                ? style.unityFontDefinition.fontAsset.name
                : style.unityFont != null ? style.unityFont.name : "—";

            Color c = style.color;
            string color = $"rgb({Mathf.RoundToInt(c.r * 255)}, {Mathf.RoundToInt(c.g * 255)}, {Mathf.RoundToInt(c.b * 255)})";

            var parts = new List<string> { face, $"{Mathf.RoundToInt(style.fontSize)}px", color };
            if (Mathf.Abs(style.letterSpacing) > 0.01f) parts.Add($"разрядка {style.letterSpacing:0.#}px");

            return string.Join(" · ", parts);
        }

        private static List<UiComponentEntry> EntriesOf(UiComponentGroup group)
        {
            var list = new List<UiComponentEntry>();
            foreach (UiComponentEntry entry in UiComponentRegistry.All)
            {
                // Техническая форма в витрину не идёт: набор показывает то, ИЗ ЧЕГО собирают экраны,
                // а не всё, что существует в коде.
                if (entry.Group == group && !entry.Technical) list.Add(entry);
            }
            return list;
        }

        private static void BuildPage(VisualElement sheet, UiComponentGroup group,
                                      List<UiComponentEntry> entries, int page)
        {
            sheet.Clear();
            Forced.Clear();
            Measured.Clear();

            var title = new Label($"{group}  ·  лист {page + 1}");
            title.AddToClassList("gm-sheet__title");
            sheet.Add(title);

            int from = page * RowsPerFrame;
            int to   = Mathf.Min(from + RowsPerFrame, entries.Count);

            for (int i = from; i < to; i++)
            {
                sheet.Add(BuildRow(entries[i]));
            }
        }

        private static VisualElement BuildRow(UiComponentEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("gm-sheet__row");

            var caption = new Label($"{entry.Label}\n{entry.Block}");
            caption.AddToClassList("gm-sheet__caption");
            row.Add(caption);

            // У декоративного элемента подпись «покой» лишняя: состояний у него нет, и слово
            // намекает, что где-то рядом должны быть другие ячейки.
            row.Add(Cell(entry, entry.IsInteractive ? "покой" : string.Empty, UiElementState.None));

            // Ячейки рисуются ровно по требуемым состояниям. Показывать «фокус» там, где элемент
            // его не принимает (строка настроек — фокус живёт на её контроле), значит рисовать
            // ячейку, неотличимую от покоя, и читать её как ненайденный дефект.
            if (entry.Required.HasFlag(UiElementState.Hover))    row.Add(Cell(entry, "наведение", UiElementState.Hover));
            if (entry.Required.HasFlag(UiElementState.Active))   row.Add(Cell(entry, "нажатие",   UiElementState.Active));
            if (entry.Required.HasFlag(UiElementState.Focus))    row.Add(Cell(entry, "фокус",     UiElementState.Focus));
            if (entry.Required.HasFlag(UiElementState.Disabled)) row.Add(Cell(entry, "выключено", UiElementState.Disabled));
            if (entry.Required.HasFlag(UiElementState.Checked))  row.Add(Cell(entry, "отмечено",  UiElementState.Checked));

            // МЕТКИ — вторая ось текста: цвет поверх роли. Показываются рядом с покоем ровно так же,
            // как у кнопки показаны состояния, потому что вопрос к ним тот же: «а как это выглядит,
            // когда наступает». Красного текста на листе не было вовсе, хотя в дереве он живёт в
            // семи местах — то есть проверить его было нечем.
            foreach (UiTextTone tone in ToneOrder)
            {
                if (entry.Tones.HasFlag(tone)) row.Add(ToneCell(entry, tone));
            }

            for (int i = 0; i < entry.Variants.Count; i++)
            {
                row.Add(Cell(entry, Modifier(entry.Variants[i]), UiElementState.None, entry.Variants[i]));
            }

            return row;
        }

        /// <summary>Порядок меток на листе: от тихой к громкой, чтобы ряд читался как шкала.</summary>
        private static readonly UiTextTone[] ToneOrder =
        {
            UiTextTone.Muted, UiTextTone.Brass, UiTextTone.Value, UiTextTone.Accent,
            UiTextTone.Positive, UiTextTone.Negative, UiTextTone.Danger,
        };

        /// <summary>Класс метки и её подпись на листе.</summary>
        private static (string Class, string Caption) ToneOf(UiTextTone tone) => tone switch
        {
            UiTextTone.Muted    => ("gm-text--muted",    "приглушённый"),
            UiTextTone.Brass    => ("gm-text--brass",    "латунь"),
            UiTextTone.Value    => ("gm-text--value",    "ценность"),
            UiTextTone.Accent   => ("gm-text--accent",   "выбрано"),
            UiTextTone.Positive => ("gm-text--positive", "прирост"),
            UiTextTone.Negative => ("gm-text--negative", "убыль"),
            UiTextTone.Danger   => ("gm-text--danger",   "опасность"),
            _                   => (null, tone.ToString()),
        };

        /// <summary>
        /// Ячейка с меткой: та же роль, поверх неё второй класс.
        /// </summary>
        /// <remarks>
        /// Класс вешается на элемент, НЕСУЩИЙ роль, а не на корень образца: у роли, набираемой
        /// предком, корень — это контейнер-обёртка, и метка на нём не покрасила бы ничего.
        /// </remarks>
        private static VisualElement ToneCell(UiComponentEntry entry, UiTextTone tone)
        {
            (string cls, string caption) = ToneOf(tone);

            var cell = new VisualElement();
            cell.AddToClassList("gm-sheet__cell");

            VisualElement sample = UiSampleFactory.Build(entry);
            VisualElement target = sample.ClassListContains(entry.Block)
                ? sample
                : sample.Q(className: entry.Block) ?? sample;
            if (cls != null) target.AddToClassList(cls);

            cell.Add(sample);

            var label = new Label(caption);
            label.AddToClassList("gm-sheet__cell-caption");
            cell.Add(label);

            return cell;
        }

        /// <summary>Короткая подпись варианта: «gm-button--primary» → «--primary».</summary>
        private static string Modifier(string variant)
        {
            int at = variant.IndexOf("--", StringComparison.Ordinal);
            return at >= 0 ? variant.Substring(at) : variant;
        }

        private static VisualElement Cell(UiComponentEntry entry, string caption,
                                          UiElementState state, string variant = null)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gm-sheet__cell");

            VisualElement sample = UiSampleFactory.Build(entry);
            if (variant != null) sample.AddToClassList(variant);

            // Замер снимается с образца В ПОКОЕ: состояния меняют цвет, и «цвет роли» в подписи
            // должен означать покой, а не наведение.
            if (variant == null && state == UiElementState.None) Measured[entry.Block] = sample;

            if (state == UiElementState.Disabled) sample.SetEnabled(false);
            else if (state == UiElementState.Checked) Check(sample);
            else if (state != UiElementState.None) Force(sample, state);

            cell.Add(sample);

            var label = new Label(caption);
            label.AddToClassList("gm-sheet__cell-caption");
            cell.Add(label);

            return cell;
        }

        /// <summary>
        /// Отмечает образец: ставит значение реальному <see cref="Toggle"/> внутри него.
        /// </summary>
        /// <remarks>
        /// Навязать <c>:checked</c> корню образца нельзя — псевдокласс держит сам <c>Toggle</c>, и
        /// правило пишется через потомка. Первый прогон это и показал: ячейка «отмечено» ничем не
        /// отличалась от «покоя», хотя правило в теме уже стояло. Значение ставится честно, через
        /// <c>value</c>, — тогда движок поднимает псевдокласс сам.
        /// </remarks>
        private static void Check(VisualElement sample)
        {
            Toggle toggle = sample as Toggle ?? sample.Q<Toggle>();
            if (toggle != null) { toggle.value = true; return; }

            Force(sample, UiElementState.Checked);
        }

        /// <summary>Навязывает элементу псевдосостояние и запоминает пару для повторной установки.</summary>
        private static void Force(VisualElement element, UiElementState state)
        {
            if (PseudoStatesProperty == null)
            {
                Debug.LogError("[ContactSheet] VisualElement.pseudoStates не найдено: движок сменил " +
                               "внутренний API, форс состояний больше не работает.");
                return;
            }

            object value = Enum.Parse(PseudoStatesProperty.PropertyType, state.ToString());
            PseudoStatesProperty.SetValue(element, value);
            Forced.Add((element, value));
        }

        /// <summary>
        /// Переустанавливает навязанные состояния. Нужно потому, что пересчёт стиля их снимает, а
        /// случается он не в момент установки, а на следующем кадре.
        /// </summary>
        private static void ReapplyForced()
        {
            if (PseudoStatesProperty == null) return;

            for (int i = 0; i < Forced.Count; i++)
            {
                PseudoStatesProperty.SetValue(Forced[i].Element, Forced[i].State);
                Forced[i].Element.MarkDirtyRepaint();
            }
        }

        private static void SaveFrame(string path)
        {
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(path, frame.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.Destroy(frame);
            }
        }
    }
}
