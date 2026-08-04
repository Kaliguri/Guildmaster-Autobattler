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

            // Версия билда — ТОЛЬКО номер, целиком (уточнение Макса 05.08.2026: «без названия игры, а
            // просто версия, но целиком, как 1.0-dev.7e18da8»). Имя продукта отсюда убрано: название
            // уже стоит вывеской над этой же колонкой, и повторять его в углу значит тратить
            // единственную служебную строку на то, что и так на экране.
            //
            // Лок-ключа намеренно нет: строка не содержит переводимых слов — это номер из
            // ProjectSettings, одинаковый на всех языках.
            //
            // Номер НЕ причёсывается: сборка кладёт в `Application.version` полный
            // `<bundleVersion>-dev.<sha7>`, и именно хвост с коммитом отвечает на «из чего собрано».
            if (version != null) version.text = BuildVersionLabel();

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

        /// <summary>
        /// Строка версии для угла экрана: ровно то, что уедет в собранный плеер.
        /// </summary>
        /// <remarks>
        /// В ПЛЕЕРЕ это <c>Application.version</c> как есть: хвост <c>-dev.&lt;sha7&gt;</c> проставляет
        /// <c>scripts/steam-publish.ps1</c> ключом <c>-buildVersion</c>, а релизной версией владеет тег.
        /// <para>В РЕДАКТОРЕ хвоста нет по построению — там играет голый <c>bundleVersion</c>, и версия
        /// в углу отвечала бы «0.1.0» на любом коммите. Поэтому в редакторе он дописывается тем же
        /// способом, что и при сборке: из головы репозитория. Без этого строка врёт ровно в том
        /// месте, ради которого она и существует, — «из чего это собрано».</para>
        /// <para>Чтение идёт по файлам <c>.git</c>, а не через запуск git: процесс из UI-кода дорог и
        /// на машине без git в PATH просто упадёт. Любая осечка молча даёт голый номер — версия в
        /// углу не тот повод, чтобы шуметь в консоль.</para>
        /// </remarks>
        private static string BuildVersionLabel()
        {
            string version = UnityEngine.Application.version;
#if UNITY_EDITOR
            if (!version.Contains("-"))
            {
                string sha = HeadShortSha();
                if (!string.IsNullOrEmpty(sha)) version += "-dev." + sha;
            }
#endif
            return version;
        }

#if UNITY_EDITOR
        /// <summary>Семь символов текущего HEAD, или пустая строка, если репозиторий не читается.</summary>
        private static string HeadShortSha()
        {
            try
            {
                string root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
                string gitDir = System.IO.Path.Combine(root, ".git");
                string head = System.IO.File.ReadAllText(System.IO.Path.Combine(gitDir, "HEAD")).Trim();

                // HEAD либо ссылается на ветку («ref: refs/heads/dev»), либо сам является хэшем
                // (detached). Второй случай — обычное состояние CI-раннера, поэтому он не экзотика.
                if (head.StartsWith("ref:"))
                {
                    string refPath = head.Substring(4).Trim();
                    string refFile = System.IO.Path.Combine(gitDir, refPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(refFile))
                    {
                        head = System.IO.File.ReadAllText(refFile).Trim();
                    }
                    else
                    {
                        // Ветка упакована в packed-refs — так бывает после `git gc` и на свежем клоне.
                        string packed = System.IO.Path.Combine(gitDir, "packed-refs");
                        if (!System.IO.File.Exists(packed)) return string.Empty;
                        head = string.Empty;
                        foreach (string line in System.IO.File.ReadAllLines(packed))
                        {
                            if (!line.EndsWith(" " + refPath)) continue;
                            head = line.Substring(0, line.IndexOf(' '));
                            break;
                        }
                    }
                }

                return head.Length >= 7 ? head.Substring(0, 7) : string.Empty;
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }
#endif
    }
}
