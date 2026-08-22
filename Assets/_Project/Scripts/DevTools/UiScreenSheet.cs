#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Кадры ЭКРАНОВ: каждый экран из <see cref="UiPreviewCatalog"/> снимается одним снимком и
    /// складывается в <see cref="OutputDir"/>.
    /// </summary>
    /// <remarks>
    /// <b>Зачем, если есть контактный лист.</b> Лист отвечает на вопрос «как выглядит ЭЛЕМЕНТ» и
    /// снимает его во всех состояниях. Вопрос «как выглядит ЭКРАН» он не закрывает, а приёмка Макса
    /// идёт именно по экранам: разбор 23.08.2026 показал, что три самых частых класса претензий —
    /// реф не отработан, метрика внутри элемента не сходится, элемент пропал — не ловятся ни одним
    /// из десяти статических гейтов и видны только на кадре целого экрана.
    ///
    /// <para><b>Почему это дешёвая надстройка, а не новая система.</b> Реестр экранов у нас уже был,
    /// просто не осознавался как реестр: <see cref="UiPreviewCatalog"/> держит пары «id → сборка со
    /// стендовыми данными», а 21 экранный View собирается чистой статической функцией
    /// <c>Build(данные)</c>. Оставалось снять кадр — этим и занят этот файл.</para>
    ///
    /// <para><b>Кадр снимается поверх ЖИВОЙ игры</b>, как и контактный лист: своей заливки у UI
    /// больше нет — задник экранов рисует презентация материалом стола, и снимок с изолированного
    /// стенда показал бы экран на пустоте. Игровой интерфейс на время прогона прячется, задник
    /// остаётся.</para>
    ///
    /// <para><b>Достоверность кадра.</b> Правило Макса 23.08.2026: стенд и игра собирают экран одним
    /// кодом, иначе кадра нет. Часть записей каталога этому пока не отвечает — они пересобирают
    /// экран заново, потому что настоящая сборка заперта приватным методом в <c>MenuRouter</c>.
    /// Такой кадр показывает стенд, а не игру, и приведение записей к правилу — отдельный шаг.</para>
    /// </remarks>
    public static class UiScreenSheet
    {
        /// <summary>Куда складываются кадры: в Лабораторию, рядом с кадрами элементов.</summary>
        public const string OutputDir = "docs/lab/assets/ui-screens";

        /// <summary>Манифест для стенда: какие экраны сняты и в каких файлах.</summary>
        public const string ManifestPath = "docs/lab/data/ui-screens.json";

        /// <summary>
        /// Сколько кадров экран устаивается перед захватом.
        /// </summary>
        /// <remarks>
        /// Сорок — то же число, что у контактного листа, и по той же причине: правила состояний
        /// анимируются USS-переходами, и захват на полпути даёт кадр, который меняется от прогона к
        /// прогону без единой правки. Экраны вдобавок гоняют вступительные анимации (заголовок,
        /// проявление панелей), так что запас нужен и здесь.
        /// </remarks>
        private const int SettleFrames = 40;

        /// <summary>
        /// Снимает по кадру на каждый экран каталога. Возвращает пути сохранённых файлов.
        /// </summary>
        /// <param name="runner">Держатель кадрового ожидания: снимок берётся строго в конце кадра.</param>
        public static async UniTask<IReadOnlyList<string>> Capture(MonoBehaviour runner)
        {
            var saved = new List<string>();

            if (!Application.isPlaying)
            {
                Debug.LogError("[ScreenSheet] Нужен play mode: вне игры живой панели не существует, " +
                               "и снимать нечего.");
                return saved;
            }

            UIDocument document = UiContactSheet.FindGameUi();
            if (document == null || document.rootVisualElement == null)
            {
                Debug.LogError("[ScreenSheet] В сцене нет UIDocument — панель интерфейса не найдена.");
                return saved;
            }

            Directory.CreateDirectory(OutputDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));

            VisualElement root = document.rootVisualElement;
            var hidden = new List<VisualElement>();

            var holder = new VisualElement { name = "screen-sheet" };
            holder.style.position = Position.Absolute;
            holder.style.left = 0;
            holder.style.top = 0;
            holder.style.right = 0;
            holder.style.bottom = 0;

            var manifest = new StringBuilder();
            manifest.Append("{\n  \"screens\": [\n");

            try
            {
                // Игровой интерфейс убирается с кадра, но не из иерархии: экран снимается на своём
                // заднике, а не поверх открытого сейчас меню.
                foreach (VisualElement child in root.Children())
                {
                    if (child.resolvedStyle.display == DisplayStyle.None) continue;
                    hidden.Add(child);
                }

                foreach (VisualElement child in hidden) child.style.display = DisplayStyle.None;
                root.Add(holder);

                foreach (string id in UiPreviewCatalog.Ids)
                {
                    UiPreviewCatalog.Build(id, holder);

                    await UniTask.DelayFrame(SettleFrames);
                    await UniTask.WaitForEndOfFrame(runner);

                    string file = $"{id}.png";
                    UiContactSheet.SaveFrame(Path.Combine(OutputDir, file));
                    saved.Add(Path.Combine(OutputDir, file));

                    if (saved.Count > 1) manifest.Append(",\n");
                    manifest.Append("    { \"id\": \"").Append(id)
                            .Append("\", \"file\": \"").Append(file).Append("\" }");
                }

                manifest.Append("\n  ]\n}\n");
                File.WriteAllText(ManifestPath, manifest.ToString());
            }
            finally
            {
                holder.RemoveFromHierarchy();
                foreach (VisualElement child in hidden) child.style.display = StyleKeyword.Null;
            }

            Debug.Log($"[ScreenSheet] Снято экранов: {saved.Count} → {Path.GetFullPath(OutputDir)}");
            return saved;
        }
    }
}
#endif
