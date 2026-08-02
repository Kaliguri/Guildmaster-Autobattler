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
        public void Install(IContainerBuilder builder)
        {
            RegisterBattleSeam(builder);
            RegisterRunLoop(builder);
        }

        /// <summary>Шов «мероприятие заказывает бой» и владелец боевого скоупа.</summary>
        private static void RegisterBattleSeam(IContainerBuilder builder)
        {
            // Один инстанс под двумя ролями: IBattleSession (write-side, боевой скоуп) + IBattleClock
            // (read-side, верхняя панель). Живёт ровно столько, сколько мероприятие: вне его ни фазы,
            // ни часов боя не существует, и это честнее, чем фаза None у объекта-долгожителя.
            builder.Register<BattleSession>(Lifetime.Singleton).As<IBattleSession>().As<IBattleClock>();
            builder.Register<SoloReadyGate>(Lifetime.Singleton).As<IReadyGate>();
            builder.Register<SoloPlayerIntentSource>(Lifetime.Singleton).As<IPlayerIntentSource>();

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
