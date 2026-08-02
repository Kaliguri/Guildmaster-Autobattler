using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Состав Сессии — сеанса владения состоянием игры. Кто держит забег, кто пишет сейв и кто вообще
    /// имеет право что-то в забеге менять, решается ЗДЕСЬ, один раз, по роли.
    /// </summary>
    /// <remarks>
    /// <b>Роль выбирает состав, а не ветвление.</b> Владелец получает держателя забега и шину команд;
    /// гость не получает держателя вовсе — состояние приходит ему по сети, и сервиса, умеющего писать
    /// сейв, у него нет ФИЗИЧЕСКИ. Это сильнее любой проверки «а мы точно хост?»: то, чего нет в
    /// контейнере, нельзя случайно позвать.
    /// <para><b>Почему Сессия, а не Занятие:</b> забег кончается выходом в хаб, а гильдия и сейв
    /// остаются. Держать их в скоупе мероприятия значило бы терять состояние на каждом выходе.</para>
    /// <para><b>Гостевая ветка сегодня пуста намеренно.</b> Кооп-вертикаль ещё не написана, и класть в
    /// неё заглушку «как будто владелец» значило бы завести ровно тот тихий обход, ради запрета
    /// которого сессия и делится по роли. Что именно получит гость (приёмник состояния по сети и шина,
    /// отправляющая команды хосту вместо применения) — ТЗ кооп-вертикали §4.</para>
    /// </remarks>
    public sealed class SessionInstaller : IInstaller
    {
        private readonly SessionRole _role;

        public SessionInstaller(SessionRole role) => _role = role;

        public void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(new SessionContext(_role));

            if (_role == SessionRole.Owner) InstallOwner(builder);
            else                            InstallGuest(builder);
        }

        /// <summary>Владелец сейва: держит забег сам, сам его пишет и сам раздаёт гостям.</summary>
        private static void InstallOwner(IContainerBuilder builder)
        {
            // Durable-состояние забега + правила вместимости реликов (план 11 §3.1, §5.4). Читателям
            // снаружи сессии оно видно только через роутер — писать в обход шины команд нельзя.
            builder.Register<Guildmaster.Guild.RunStateService>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Guild.ISessionRunState>();

            // Раздача состояния гостям и приём их интентов. Регистрируется и в соло: транспорт не поднят,
            // и вся работа сводится к одному ветвлению в тике — а ветка «а вдруг мы одни» не нужна ни
            // одному потребителю.
            builder.RegisterEntryPoint<Net.RunStateBroadcast>(Lifetime.Singleton).AsSelf();

            // «Где мы»: вид мероприятия, границы арены и фаза боя. Живёт в сеансе, а не в корне, потому
            // что это обязанность роли — владелец объявляет, гость следует.
            builder.RegisterEntryPoint<Net.ActivityBroadcast>(Lifetime.Singleton).AsSelf();

            // Шина команд забега: снаружи сборки Guild в RunState пишут только через неё, и мутаторы
            // internal держат это компилятором. Лог append-only даёт реплей, аудит «кто передвинул» и
            // хвост для реконнекта; соло идёт этим же путём, иначе кооп нашёл бы обход первым же
            // расхождением состояний (ТЗ кооп-вертикали §4.1).
            // Сама шина регистрируется ТОЛЬКО собой: дорога к записи одна и идёт через корневой
            // SessionCommandRouter — иначе у писателей внутри сессии был бы второй путь, мимо роутера,
            // и «писать некуда» перестало бы работать одинаково для всех.
            builder.Register<Guildmaster.Guild.Commands.RunCommandLog>(Lifetime.Singleton);
            builder.Register<Guildmaster.Guild.Commands.RunCommandApplier>(Lifetime.Singleton);
            builder.Register<Guildmaster.Guild.Commands.RunCommandBus>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Guild.Commands.ISessionRunCommands>();

            // Общее согласие считает владелец — он единственный знает, кто в сессии. В соло участник один,
            // и гейт пропускает действие в тот же кадр: ветки «а мы одни?» у вызывающих нет.
            builder.RegisterEntryPoint<Net.HostReadyGate>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Core.Net.IReadyGate>();
        }

        /// <summary>
        /// Гость: состояние приходит по сети и только читается, изменения уезжают интентом хосту.
        /// </summary>
        /// <remarks>
        /// Держателя забега здесь нет, и это главное в ветке: у гостя нет ни объекта, который умеет
        /// менять чужой забег, ни сервиса, который умеет его записать. Лога команд тоже нет — гость
        /// ничего не применяет, он получает результат (см. <see cref="Net.GuestRunState"/>).
        /// </remarks>
        private static void InstallGuest(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<Net.GuestRunState>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Guild.ISessionRunState>();

            builder.Register<Net.RemoteRunCommands>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Guild.Commands.ISessionRunCommands>();

            // Мероприятие и арена открываются вслед за хостом: гость идёт туда же, где играют, а не
            // решает сам, где играть.
            builder.RegisterEntryPoint<Net.GuestActivityFollower>(Lifetime.Singleton).AsSelf();

            // Отряд на арене — вслед за снимком. Без этого гость видел бы пустое поле до самого первого
            // боя: тела кладёт петля акта, а её у гостя нет.
            builder.RegisterEntryPoint<Net.GuestPartyFollower>(Lifetime.Singleton).AsSelf();

            // Своё согласие гость отправляет, счёт получает. Решать, все ли готовы, ему нечем: кто в
            // сессии, знает хост.
            builder.RegisterEntryPoint<Net.GuestReadyGate>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Core.Net.IReadyGate>();
        }
    }
}
