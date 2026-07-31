using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Dev-просмотрщик UI-экранов (<b>F4</b>): полноэкранная лента экранов со стендовыми данными через
    /// <see cref="UiPreviewCatalog"/> — посмотреть награду, ивент или галерею, не гоняя весь флоу.
    /// </summary>
    /// <remarks>
    /// <b>Список боёв отсюда снят 31.07</b> — его заменила витрина на F3
    /// (<see cref="DevBattleBrowserScreen"/>) с поиском и запуском через команды. Держать оба означало бы
    /// два несогласованных способа запустить бой: панель звала <c>EncounterLoader</c> напрямую, витрина
    /// зовёт команду. Осталась ровно та часть, у которой замены нет.
    /// <para>Повесь на GameObject с <see cref="UIDocument"/>; Editor-only по содержимому — каталог
    /// стенда грузит UXML через AssetDatabase, поэтому в билде лента просто не строится.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DevEncounterPanel : MonoBehaviour
    {
        private UIDocument _document;

        private VisualElement _screensRoot;   // полноэкранный просмотр экранов (тогглим display)
        private VisualElement _screenContent; // куда UiPreviewCatalog строит выбранный экран
        private bool _visible;

        private void Start()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null) { Debug.LogWarning("[DevEncounterPanel] - нет UIDocument"); enabled = false; return; }

            BuildScreensOverlay(_document.rootVisualElement);
            Apply();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // F4 — лента экранов. F1/F2/F3 отданы dev-консолям и витрине боёв (решение Макса 31.07).
            if (kb.f4Key.wasPressedThisFrame)
            {
                _visible = !_visible;
                Apply();
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void BuildScreensOverlay(VisualElement root)
        {
#if UNITY_EDITOR
            _screensRoot = new VisualElement { name = "gm-dev-screens" };
            _screensRoot.style.position = Position.Absolute;
            _screensRoot.style.left = 0; _screensRoot.style.top = 0;
            _screensRoot.style.right = 0; _screensRoot.style.bottom = 0;
            _screensRoot.style.display = DisplayStyle.None;

            // Слой контента — сюда каталог строит выбранный экран (у экранов свой полноэкранный scrim).
            _screenContent = new VisualElement { name = "gm-dev-screen-content" };
            _screenContent.style.position = Position.Absolute;
            _screenContent.style.left = 0; _screenContent.style.top = 0;
            _screenContent.style.right = 0; _screenContent.style.bottom = 0;
            _screensRoot.Add(_screenContent);

            // Верхняя лента-переключатель экранов (поверх контента).
            var bar = new VisualElement();
            bar.AddToClassList("gm-dev-screens-bar");
            foreach (string id in UiPreviewCatalog.Ids)
            {
                if (id == "dev-picker") continue; // сам пикер здесь не нужен
                string screenId = id;
                var b = new Button(() => UiPreviewCatalog.Build(screenId, _screenContent)) { text = ScreenLabel(screenId) };
                b.AddToClassList("gm-dev-play");
                bar.Add(b);
            }
            _screensRoot.Add(bar);
            root.Add(_screensRoot);
#endif
        }

#if UNITY_EDITOR
        private static string ScreenLabel(string id) => id switch
        {
            "reward"      => "Награда",
            "event"       => "Ивент",
            "settings"    => "Настройки",
            "gallery"     => "Галерея",
            "devconsole"  => "Консоль",
            _              => id,
        };
#endif

        private void Apply()
        {
            if (_screensRoot != null)
                _screensRoot.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
