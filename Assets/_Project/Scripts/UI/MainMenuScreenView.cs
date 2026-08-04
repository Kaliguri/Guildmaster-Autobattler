using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка главного меню: Создать игру / Присоединиться / Настройки / Выход. Общий код для роутера
    /// и превью-стенда.
    /// </summary>
    /// <remarks>
    /// <b>Две кнопки входа, а не список режимов</b> (модель Макса 02.08.2026). Режим, дом и открытость
    /// для друзей выбираются следующим шагом — на экране «Создать игру». Прежде здесь стояли «Начать
    /// забег», «Продолжить», «Ристалище» и «Сетевая игра», и последняя вела ровно туда же, куда
    /// первая: кооп у нас свойство сеанса, а не отдельная игра.
    /// </remarks>
    public static class MainMenuScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            Func<string, string> localize,
            Action onCreate,
            Action onJoin,
            Action onSettings,
            Action onQuit,
            bool canJoin = true,
            Action onProfile = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var titleOver = root.Q<Label>("menu-title-over");
            var title     = root.Q<Label>("menu-title");
            var version   = root.Q<Label>("menu-version");
            var create   = root.Q<Button>("btn-create");
            var join     = root.Q<Button>("btn-join");
            var profile  = root.Q<Button>("btn-profile");
            var settings = root.Q<Button>("btn-settings");
            var quit     = root.Q<Button>("btn-quit");

            // Вывеска набрана ДВУМЯ элементами: у слов разные гарнитуры, кегли и разрядка. Ключи тоже
            // раздельные — иначе перевод, склеенный в одну строку, разложить обратно нечем. Регистр
            // задаёт содержимое ключа, а не USS: `text-transform` в UI Toolkit нет.
            if (titleOver != null) titleOver.text = L("ui.mainmenu.title_over", "HAPPY");
            if (title != null) title.text = L("ui.mainmenu.title", "GUILDMASTERS");

            // Версия билда. Лок-ключа намеренно нет: строка не содержит слов — это «v» и номер из
            // ProjectSettings, одинаковые на всех языках. Нужна, чтобы по скриншоту в багрепорте было
            // видно, на чём игрок играл.
            if (version != null) version.text = "v" + UnityEngine.Application.version;

            if (create != null)
            {
                create.text = L("ui.mainmenu.create", "СОЗДАТЬ ИГРУ");
                create.clicked += () => onCreate?.Invoke();
            }

            // «Профиль» — кем игрок заходит: слот сохранения плюс ник, цвет и курсор. Стоит в меню, а не
            // в настройках, потому что настройки принадлежат машине, а профиль — игроку.
            if (profile != null)
            {
                profile.text = L("ui.mainmenu.profile", "ПРОФИЛЬ");
                profile.clicked += () => onProfile?.Invoke();
            }

            // «Присоединиться» открывает список друзей Steam и меню НЕ закрывает: войти игрок
            // соглашается уже в оверлее, а уводит нас отсюда рукопожатие, а не клик. Без Steam кнопка
            // гаснет — это внешний отказ, и прятать его нельзя.
            if (join != null)
            {
                join.text = L("ui.mainmenu.join", "ПРИСОЕДИНИТЬСЯ");
                join.SetEnabled(canJoin);
                join.clicked += () => onJoin?.Invoke();
            }

            if (settings != null)
            {
                settings.text = L("ui.mainmenu.settings", "НАСТРОЙКИ");
                settings.clicked += () => onSettings?.Invoke();
            }

            if (quit != null)
            {
                quit.text = L("ui.mainmenu.quit", "ВЫХОД");
                quit.clicked += () => onQuit?.Invoke();
            }

            return root;
        }
    }
}
