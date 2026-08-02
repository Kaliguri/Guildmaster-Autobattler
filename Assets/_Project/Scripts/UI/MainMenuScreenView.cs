using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка главного меню (план [[act-map-run-loop]] §4 D1): Начать / Продолжить / Настройки / Выход. Общий код
    /// для роутера и превью-стенда. «Продолжить» активна только при наличии автосейва (<paramref name="hasSave"/>).
    /// </summary>
    public static class MainMenuScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            bool hasSave,
            Func<string, string> localize,
            Action onStart,
            Action onContinue,
            Action onSettings,
            Action onQuit,
            Action onProvingGrounds = null,
            Action onCoop = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title    = root.Q<Label>("menu-title");
            var version  = root.Q<Label>("menu-version");
            var start    = root.Q<Button>("btn-start");
            var cont     = root.Q<Button>("btn-continue");
            var settings = root.Q<Button>("btn-settings");
            var quit     = root.Q<Button>("btn-quit");

            if (title != null) title.text = L("ui.mainmenu.title", "Happy Guildmasters");

            // Версия билда. Лок-ключа намеренно нет: строка не содержит слов — это «v» и номер из
            // ProjectSettings, одинаковые на всех языках. Нужна, чтобы по скриншоту в багрепорте было
            // видно, на чём игрок играл.
            if (version != null) version.text = "v" + UnityEngine.Application.version;

            if (start != null)    { start.text    = L("ui.mainmenu.start", "Начать забег"); start.clicked += () => onStart?.Invoke(); }
            if (cont != null)
            {
                cont.text = L("ui.mainmenu.continue", "Продолжить");
                cont.SetEnabled(hasSave);
                cont.clicked += () => onContinue?.Invoke();
            }
            // Ристалище (ГДД [[proving-grounds]]) — открыто игроку с 02.08.2026: площадка стала
            // полноценным мероприятием и поднимается со своей ареной, то есть входить туда есть куда.
            // Экрана сборки состава пока нет — состав приходит ассетом или дев-командой, — но это уже
            // «неполно», а не «некуда идти».
            var proving = root.Q<Button>("btn-proving-grounds");
            if (proving != null)
            {
                proving.text = L("ui.mainmenu.proving_grounds", "Ристалище");
                proving.clicked += () => onProvingGrounds?.Invoke();
            }
            // Сетевая игра открывается ПОВЕРХ меню и его не закрывает: сессия поднимается до забега, а
            // «Начать забег» игрок жмёт уже подключённым — тот же путь, что у настроек.
            var coop = root.Q<Button>("btn-coop");
            if (coop != null)
            {
                coop.text = L("ui.mainmenu.coop", "Сетевая игра");
                coop.clicked += () => onCoop?.Invoke();
            }
            if (settings != null) { settings.text = L("ui.mainmenu.settings", "Настройки"); settings.clicked += () => onSettings?.Invoke(); }
            if (quit != null)     { quit.text     = L("ui.mainmenu.quit", "Выход"); quit.clicked += () => onQuit?.Invoke(); }

            return root;
        }
    }
}
