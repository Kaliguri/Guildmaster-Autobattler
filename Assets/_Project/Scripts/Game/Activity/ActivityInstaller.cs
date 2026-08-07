using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Game.Services;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Activity
{
    /// <summary>
    /// Состав Занятия — конечного мероприятия со своим состоянием и выходом в хаб: забег, Двор,
    /// Ристалище, PvP-матч, дев-арена. Включено всегда ровно одно.
    /// </summary>
    /// <remarks>
    /// <b>Здесь живёт всё, что кончается вместе с мероприятием:</b> петля акта, узлы, награды, магазин,
    /// рукопожатие боя и владелец боевого скоупа. Пока этот слой жил в корне, «конец забега» приходилось
    /// объявлять вручную — сбрасывать фазу, просить бой вернуть мир, чистить ожидания, — и каждая забытая
    /// строка становилась багом следующего забега. Теперь конец мероприятия это смерть скоупа.
    /// <para><b>Состояние забега (<c>RunStateService</c>) сюда НЕ переезжает:</b> его дом — Сессия,
    /// уровнем выше. Забег кончается, а гильдия и сейв остаются — это разные жизни, и складывать их в
    /// одну значило бы терять состояние на каждом выходе в хаб. Мероприятие рождается ОТ сессии, так
    /// что состояние и роль ему видны и без переезда.</para>
    /// </remarks>
    public sealed class ActivityInstaller : IInstaller
    {
        private readonly ActivitySetup       _setup;
        private readonly Session.SessionRole _role;

        public ActivityInstaller(ActivitySetup setup, Session.SessionRole role)
        {
            _setup = setup;
            _role  = role;
        }

        public void Install(IContainerBuilder builder)
        {
            // С чем открыто мероприятие — доступно всем внутри, включая бой: ограничения площадки
            // (скрыть чужой строй, расставлять только своих) исполняет расстановка, а не тот, кто
            // мероприятие заказал.
            builder.RegisterInstance(_setup);

            // Сколько у площадки сторон, знает она одна — а раскладывает по ним участников сеанс.
            // Только у владельца: гость чужой состав не пересаживает.
            if (_role == Session.SessionRole.Owner)
                builder.RegisterEntryPoint<ActivitySideAssignment>(Lifetime.Singleton);

            RegisterBattleSeam(builder);

            // Ведение акта — обязанность владельца забега. Гость подключается к ЧУЖОМУ мероприятию:
            // куда идти по карте, какая награда выпала и что стоит в лавке, решает хост, а гостю
            // приезжает результат снимком состояния. Вторая петля акта на его стороне не «дублировала
            // бы работу», а расходилась бы с первой — на первом же ролле награды.
            if (_role == Session.SessionRole.Owner) RegisterRunLoop(builder);
        }

        /// <summary>Шов «мероприятие заказывает бой» и владелец боевого скоупа.</summary>
        private void RegisterBattleSeam(IContainerBuilder builder)
        {
            // Один инстанс под двумя ролями: IBattleSession (write-side, боевой скоуп) + IBattleClock
            // (read-side, верхняя панель). Живёт ровно столько, сколько мероприятие: вне его ни фазы,
            // ни часов боя не существует, и это честнее, чем фаза None у объекта-долгожителя.
            // У гостя фазу в него проставляет GuestActivityFollower — вести её ему нечем.
            builder.Register<BattleSession>(Lifetime.Singleton).As<IBattleSession>().As<IBattleClock>();

            // Готовность и авторитет. Гейт у гостя пока соло-тело: «ждём всех» появится вместе с
            // экранами подготовки, а до них ждать не на чем. Авторитет — нет: решения принимает хост,
            // и отвечать «да, я тут главный» гостю нельзя ни на секунду.
            builder.Register<SoloReadyGate>(Lifetime.Singleton).As<ISharedDecision>();

            if (_role == Session.SessionRole.Owner)
                builder.Register<SoloPlayerIntentSource>(Lifetime.Singleton).As<IPlayerIntentSource>();
            else
                builder.Register<GuestPlayerIntentSource>(Lifetime.Singleton).As<IPlayerIntentSource>();

            // Владелец жизненного цикла боя. Переехал из мира: бой заказывает узел, а узел — часть
            // мероприятия, и рождаться бой должен внутри той жизни, которая его заказала. Заготовку
            // боевого скоупа он резолвит сам — она выбрана в мире и видна отсюда вверх по цепочке.
            builder.RegisterEntryPoint<BattleHost>(Lifetime.Singleton).AsSelf();
        }

        /// <summary>Ведение акта: узлы, награды, передышка, магазин, последствия ивентов.</summary>
        private static void RegisterRunLoop(IContainerBuilder builder)
        {
            // Витрина наград после боя (A3): катит 1-из-3 реликов из контент-БД (детерминирован через RNG).
            builder.Register<RewardService>(Lifetime.Singleton);
            // Ценообразование реликвий (B1): цена по KitPower + разброс на сиде витрины.
            builder.Register<RelicPricer>(Lifetime.Singleton);
            // Показ награды: переиспользуют петля акта и вход одного боя.
            builder.Register<RewardPresenter>(Lifetime.Singleton).As<IRewardPresenter>();
            // Кнопки бита (A4): гейт «бой добит → к награде» и передышка между узлами.
            builder.Register<ContinuePresenter>(Lifetime.Singleton).As<IContinuePresenter>();

            // Применение последствий текстовых ивентов к RunState (план 11 §5.1).
            builder.Register<EventEffectApplier>(Lifetime.Singleton);
            // Магазин (B2): логика витрины/покупки/продажи за IShopController.
            builder.Register<ShopController>(Lifetime.Singleton);

            // Петля акта: резолвер узлов + выбор узла через карту + раннер обхода.
            builder.Register<NodeResolver>(Lifetime.Singleton).As<INodeResolver>();
            builder.Register<WorldMapNodeChooser>(Lifetime.Singleton).As<IMapNodeChooser>();
            builder.Register<RunBeatStage>(Lifetime.Singleton).As<IRunBeatStage>();
            builder.Register<ActRunner>(Lifetime.Singleton);
        }
    }
}
