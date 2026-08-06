using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        /// <summary>Куда складываются кадры. Под <c>Temp</c> — они уходят в чат, а не в репозиторий.</summary>
        public const string OutputDir = "Temp/ContactSheet";

        /// <summary>
        /// Сколько элементов помещается на один кадр 1080p. Четыре, а не шесть: строка переносится
        /// (широкие пункты меню в один ряд не влезают), и высота строки скачет.
        /// </summary>
        private const int RowsPerFrame = 4;

        private static readonly PropertyInfo PseudoStatesProperty =
            typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Пары «элемент → состояние, которое ему навязано» — переустанавливаются каждый кадр.</summary>
        private static readonly List<(VisualElement Element, object State)> Forced = new();

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

            try
            {
                foreach (UiComponentGroup group in Enum.GetValues(typeof(UiComponentGroup)))
                {
                    List<UiComponentEntry> entries = EntriesOf(group);
                    int pages = Mathf.CeilToInt(entries.Count / (float)RowsPerFrame);

                    for (int page = 0; page < pages; page++)
                    {
                        BuildPage(sheet, group, entries, page);

                        // Два кадра: первый считает раскладку, второй отдаёт устоявшиеся стили.
                        await UniTask.DelayFrame(2);
                        ReapplyForced();

                        await UniTask.WaitForEndOfFrame(runner);
                        string path = Path.Combine(OutputDir, $"{group.ToString().ToLowerInvariant()}-{page + 1}.png");
                        SaveFrame(path);
                        saved.Add(path);
                    }
                }
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

        private static List<UiComponentEntry> EntriesOf(UiComponentGroup group)
        {
            var list = new List<UiComponentEntry>();
            foreach (UiComponentEntry entry in UiComponentRegistry.All)
            {
                if (entry.Group == group) list.Add(entry);
            }
            return list;
        }

        private static void BuildPage(VisualElement sheet, UiComponentGroup group,
                                      List<UiComponentEntry> entries, int page)
        {
            sheet.Clear();
            Forced.Clear();

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

            row.Add(Cell(entry, "покой",   UiElementState.None));
            if (entry.IsInteractive)
            {
                row.Add(Cell(entry, "наведение", UiElementState.Hover));
                row.Add(Cell(entry, "нажатие",   UiElementState.Active));
                row.Add(Cell(entry, "фокус",     UiElementState.Focus));
                row.Add(Cell(entry, "выключено", UiElementState.Disabled));
                if (entry.Required.HasFlag(UiElementState.Checked))
                    row.Add(Cell(entry, "отмечено", UiElementState.Checked));
            }

            for (int i = 0; i < entry.Variants.Count; i++)
            {
                row.Add(Cell(entry, Modifier(entry.Variants[i]), UiElementState.None, entry.Variants[i]));
            }

            return row;
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

            VisualElement sample = UiSampleFactory.Build(entry.Block);
            if (variant != null) sample.AddToClassList(variant);

            if (state == UiElementState.Disabled) sample.SetEnabled(false);
            else if (state != UiElementState.None) Force(sample, state);

            cell.Add(sample);

            var label = new Label(caption);
            label.AddToClassList("gm-sheet__cell-caption");
            cell.Add(label);

            return cell;
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
