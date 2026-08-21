using System.Collections.Generic;
using Guildmaster.Core.DevConsole;
using Guildmaster.Core.Flow;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Дев-команды интерфейса: вызвать сообщение, ожидание и ленту, не доводя игру до настоящего
    /// события.
    /// </summary>
    /// <remarks>
    /// <b>Зачем.</b> Заказ Макса 20.08.2026: «Дай мне через F меню возможность мб в игре открывать
    /// пустые сообщения (ленты и notification). Чтобы сам потестил». До этого единственным способом
    /// увидеть окно был мой скриншот из play-сеанса — то есть приёмка шла через мои глаза, а
    /// владелец вида смотрел на картинку и не мог ни навести курсор, ни нажать, ни оценить размер
    /// вживую. Настоящие поводы (разрыв связи, приглашение, снос профиля) воспроизводятся тяжело: для
    /// половины нужен второй компьютер со Steam.
    ///
    /// <para><b>Команды публикуют ТОТ ЖЕ заказ, что и игра</b> — <see cref="NoticeRequest"/> и
    /// <see cref="BusyRequest"/> в общий канал. Никакого своего пути показа у них нет: иначе дев-окно
    /// начнёт отличаться от игрового ровно тем местом, которое и пришли проверить.</para>
    /// </remarks>
    public static class UiDevCommands
    {
        /// <summary>Сколько живёт дев-ожидание, если его не снять раньше: столько же, сколько
        /// настоящее подключение через relay Valve.</summary>
        private const int BusySeconds = 12;

        private static System.Threading.CancellationTokenSource _busy;

        public static void Register(DevCommandSet set)
        {
            if (set == null) return;

            set.Add("gm_ui_notice",
                "Окно-сообщение: gm_ui_notice <вид 0 инфо / 1 внимание / 2 ошибка> <ответов 0..3>",
                a => Notice(a.GetInt(0), a.GetInt(1)),
                new DevParam("kind", DevParamType.Int),
                new DevParam("answers", DevParamType.Int, true));

            set.Add("gm_ui_busy",
                $"Экран ожидания на {BusySeconds} с (повтор снимает): подключение к чужой игре",
                _ => Busy());

            set.Add("gm_ui_toast",
                "Лента: gm_ui_toast <сколько строк 1..3> — короткие сообщения без ответов",
                a => Toast(a.GetInt(0)),
                new DevParam("count", DevParamType.Int, true));
        }

        /// <summary>Показать окно нужного вида с нужным числом ответов.</summary>
        /// <remarks>
        /// Тексты здесь ДЛИННЫЕ намеренно: короткая подпись влезает куда угодно, и мерить раскладку
        /// ею бессмысленно. Ряд ответов проверяется худшим случаем, а не удобным.
        /// </remarks>
        public static string Notice(int kind, int answers)
        {
            IPublisher<NoticeRequest> bus = Publisher<NoticeRequest>();
            if (bus == null) return "Канал сообщений не поднят — игра ещё не собрана.";

            NoticeKind noticeKind = kind switch
            {
                1 => NoticeKind.Warning,
                2 => NoticeKind.Error,
                _ => NoticeKind.Info,
            };

            var options = new List<NoticeOption>();
            if (answers >= 1) options.Add(new NoticeOption(null, "Подождать ещё", null));
            if (answers >= 2) options.Add(new NoticeOption(null, "Вернуться в меню", null));
            if (answers >= 3) options.Add(new NoticeOption(null, "Выйти из игры", null, true));

            bus.Publish(new NoticeRequest(
                noticeKind,
                null, TitleFor(noticeKind),
                null, "Гилберт не отвечает уже двенадцать секунд.",
                "Steam: connection timed out (peer 76561198…)",
                options.Count > 0 ? options : null,
                "Забег останется у него — вы сможете вернуться, если он позовёт снова."));

            return $"Окно вида «{TitleFor(noticeKind)}», ответов {options.Count}. " +
                   "Закрыть можно только кнопкой — так и задумано.";
        }

        /// <summary>Поднять экран ожидания. Повторный вызов снимает его досрочно.</summary>
        public static string Busy()
        {
            IPublisher<BusyRequest> bus = Publisher<BusyRequest>();
            if (bus == null) return "Канал ожидания не поднят — игра ещё не собрана.";

            if (_busy != null)
            {
                _busy.Cancel();
                _busy.Dispose();
                _busy = null;
                return "Ожидание снято.";
            }

            _busy = new System.Threading.CancellationTokenSource();
            _busy.CancelAfter(BusySeconds * 1000);

            // Дев-ожидание несёт ту же кнопку отмены, что и настоящее: смотреть надо на то, что
            // увидит игрок, а не на облегчённую версию без половины элементов.
            var cancel = new NoticeOption("ui.common.cancel", "Отмена", () =>
            {
                _busy?.Cancel();
                _busy?.Dispose();
                _busy = null;
            });

            bus.Publish(new BusyRequest("ui.coop.connecting", "Подключение к игре", _busy.Token,
                "Steam ищет маршрут — это занимает несколько секунд.", cancel));

            return $"Ожидание поднято на {BusySeconds} с. Повтор команды снимет его раньше.";
        }

        /// <summary>Показать одну-три ленты подряд, чтобы увидеть стопку.</summary>
        public static string Toast(int count)
        {
            IPublisher<NoticeRequest> bus = Publisher<NoticeRequest>();
            if (bus == null) return "Канал сообщений не поднят — игра ещё не собрана.";

            string[] lines =
            {
                "Нет свободного слота под реликвию.",
                "Гилберт присоединился к игре.",
                "Сохранено.",
            };

            int shown = count <= 0 ? 1 : (count > lines.Length ? lines.Length : count);
            for (int i = 0; i < shown; i++)
            {
                // Ответов НЕТ — именно поэтому это лента, а не окно: облик выбирает модель по
                // наличию списка ответов, и дев-команда обязана ходить той же дорогой.
                bus.Publish(new NoticeRequest(NoticeKind.Info, null, null, null, lines[i]));
            }

            return $"Лент показано: {shown}. Уходят сами, клик закрывает раньше.";
        }

        private static string TitleFor(NoticeKind kind) => kind switch
        {
            NoticeKind.Warning => "Связь с хозяином потеряна",
            NoticeKind.Error   => "Не удалось подключиться к игре",
            _                  => "Гилберт зовёт в свою игру",
        };

        /// <summary>
        /// Публикатор из корневого скоупа. Промах регистрации здесь не ошибка: команду могли позвать
        /// раньше, чем игра собралась.
        /// </summary>
        private static IPublisher<T> Publisher<T>()
        {
            var root = LifetimeScope.Find<Guildmaster.Game.RootLifetimeScope>();
            if (root == null || root.Container == null) return null;

            try { return root.Container.Resolve(typeof(IPublisher<T>)) as IPublisher<T>; }
            catch { return null; }
        }
    }
}
