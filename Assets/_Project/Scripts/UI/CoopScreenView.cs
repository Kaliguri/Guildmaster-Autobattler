using System;
using Guildmaster.Core.Net;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран «Сетевая игра»: создать сессию, войти по адресу, отключиться — и всегда видеть, что
    /// происходит.
    /// </summary>
    /// <remarks>
    /// <b>Игрок входит только по приглашению</b> (решение Макса 02.08.2026): создание — один клик без
    /// названия и настроек, дальше зовут друга оверлеем Steam. Списка комнат и ввода адреса у игрока
    /// нет; прямое подключение остаётся отладочным и показывается только в редакторе и dev-сборке.
    /// <para><b>Состояние показывается словами.</b> Кооп ломается снаружи игры (порт, фаервол, другая
    /// версия), и единственное, чем интерфейс может помочь, — назвать, на каком шаге всё встало.
    /// Молчаливый экран игрок читает как «не работает интернет» и идёт не за патчем, а за отзывом.</para>
    /// <para><b>Кнопки — функция состояния, а не тумблеры.</b> «Создать» жива только в оффлайне,
    /// «Пригласить» и «Отключиться» — только в сессии; иначе экран позволяет нажать то, что уже
    /// сделано, и хост получает второй StartHost поверх первого.</para>
    /// </remarks>
    public static class CoopScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            ICoopSessionControl session,
            Func<string, string> localize,
            Action onBack)
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title   = root.Q<Label>("coop-title");
            var status  = root.Q<Label>("coop-status");
            var host    = root.Q<Button>("btn-host");
            var invite  = root.Q<Button>("btn-invite");
            var leave   = root.Q<Button>("btn-leave");
            var back    = root.Q<Button>("btn-back");

            if (title  != null) title.text  = L("ui.coop.title", "Сетевая игра");
            if (host   != null) host.text   = L("ui.coop.host", "Создать игру");
            if (invite != null) invite.text = L("ui.coop.invite", "Пригласить друга");
            if (leave  != null) leave.text  = L("ui.coop.leave", "Отключиться");
            if (back   != null) back.text   = L("ui.coop.back", "Назад");

            void Refresh()
            {
                CoopSessionState state = session?.State ?? CoopSessionState.Offline;
                bool offline = state == CoopSessionState.Offline;

                host?.SetEnabled(offline);
                leave?.SetEnabled(!offline);
                invite?.SetEnabled(session?.CanInvite ?? false);

                if (status != null) status.text = Describe(state, session, L);
            }

            if (host != null)   host.clicked   += () => { session?.StartHost(); Refresh(); };
            if (invite != null) invite.clicked += () => session?.InviteFriend();
            if (leave != null)  leave.clicked  += () => { session?.Leave(); Refresh(); };
            if (back != null)   back.clicked   += () => onBack?.Invoke();

            // Состояние меняется и без нас — хост ушёл, гость не достучался, — поэтому экран слушает
            // сессию, а не только свои кнопки. Отписка на снятии: экран пересоздаётся при каждом показе.
            if (session != null)
            {
                void OnStateChanged(CoopSessionState _) => Refresh();
                session.StateChanged += OnStateChanged;
                root.RegisterCallback<DetachFromPanelEvent>(_ => session.StateChanged -= OnStateChanged);
            }

            Refresh();
            return root;
        }

        private static string Describe(CoopSessionState state, ICoopSessionControl session, Func<string, string, string> L)
        {
            switch (state)
            {
                case CoopSessionState.Hosting:
                    return L("ui.coop.status.hosting", "Сессия поднята — можно звать напарника");
                case CoopSessionState.Connecting:
                    return L("ui.coop.status.connecting", "Подключаемся…");
                case CoopSessionState.Connected:
                    return L("ui.coop.status.connected", "На связи");
            }

            // Оффлайн: если прошлая сессия кончилась не по нашей воле, игрок обязан узнать причину —
            // отказ хоста приходит текстом и без него выглядит как обрыв связи.
            string message = session?.EndMessage;
            if (!string.IsNullOrEmpty(message)) return message;

            return (session?.EndReason ?? CoopEndReason.None) switch
            {
                CoopEndReason.HostLeft         => L("ui.coop.status.host_left", "Хост завершил игру"),
                CoopEndReason.ConnectionFailed => L("ui.coop.status.failed", "Не удалось подключиться"),
                CoopEndReason.Rejected         => L("ui.coop.status.rejected", "Хост отказал в подключении"),
                _                              => L("ui.coop.status.offline", "Играем одни"),
            };
        }
    }
}
