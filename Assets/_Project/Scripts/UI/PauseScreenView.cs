using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Системное меню (пауза): «Продолжить», «Настройки», приглашение друга, выход в главное меню и
    /// из игры. Что делают кнопки — знает владелец; здесь только вид.
    /// </summary>
    /// <remarks>
    /// Вынесено из <c>MenuRouter</c> 23.08.2026 по тому же правилу, что настройки и расстановка:
    /// экран, чью сборку нельзя позвать со стенда, невозможно снять кадром, а приёмка интерфейса
    /// идёт по кадрам.
    /// </remarks>
    public sealed class PauseScreenView
    {
        /// <summary>Стилевой маркер «системное меню» — по нему тема отличает паузу от прочих оверлеев.</summary>
        private const string RootClass = "gm-pause-root";

        /// <summary>Корень экрана.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Кнопки меню. Приглашение может отсутствовать в разметке — проверяй на null.</summary>
        public Button Return { get; private set; }
        public Button Settings { get; private set; }
        public Button Invite { get; private set; }
        public Button ToMainMenu { get; private set; }
        public Button Quit { get; private set; }

        /// <summary>
        /// Собирает системное меню.
        /// </summary>
        /// <param name="uxml">Разметка экрана.</param>
        /// <param name="localize">Ключ → строка; пустой ответ означает «нет перевода», берётся RU-запас.</param>
        /// <param name="canInvite">
        /// Доступно ли приглашение друга. Кнопка остаётся видимой и выключенной, а не исчезает: место
        /// приглашения в меню постоянно, и пропадающий пункт заставлял бы искать его заново.
        /// </param>
        public static PauseScreenView Build(VisualTreeAsset uxml, Func<string, string> localize, bool canInvite)
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement tree = uxml.CloneTree();
            tree.style.position = Position.Absolute;
            tree.style.left = 0;
            tree.style.top = 0;
            tree.style.right = 0;
            tree.style.bottom = 0;
            tree.AddToClassList(RootClass);

            var view = new PauseScreenView
            {
                Root       = tree,
                Return     = tree.Q<Button>("btn-return"),
                Settings   = tree.Q<Button>("btn-settings"),
                Invite     = tree.Q<Button>("btn-invite"),
                ToMainMenu = tree.Q<Button>("btn-main-menu"),
                Quit       = tree.Q<Button>("btn-quit"),
            };

            // Приглашение живёт ЗДЕСЬ, а не в главном меню: лобби поднимается вместе с игрой, и до
            // входа звать друга некуда.
            if (view.Invite != null)
            {
                view.Invite.text = L("ui.menu.invite", "Пригласить друга");
                view.Invite.SetEnabled(canInvite);
            }

            if (view.ToMainMenu != null) view.ToMainMenu.text = L("ui.menu.to_main_menu", "В главное меню");
            if (view.Quit != null) view.Quit.text = L("ui.menu.quit", "Выйти из игры");

            return view;
        }
    }
}
