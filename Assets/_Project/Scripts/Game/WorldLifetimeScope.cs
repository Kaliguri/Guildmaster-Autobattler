using Guildmaster.Core.Arena;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Персистентный DI-скоуп «мира»: живёт всю сессию (WorldScene грузится аддитивно на
    /// буте и не выгружается), дочерний к <see cref="RootLifetimeScope"/>. Держит ЕДИНУЮ
    /// камеру-риг и снапшот арены, переживающие бои — вне боя они показывают серую арену
    /// (карта/инвентарь), в бою тот же риг переиспользуется. Боевой <see cref="CombatLifetimeScope"/>
    /// становится дочерним к этому скоупу и резолвит камеру/арену из предка, без дублей
    /// Main Camera/Brain (вики «16» §5).
    /// </summary>
    public class WorldLifetimeScope : LifetimeScope
    {
        [Tooltip("Design-конфиг тряски камеры (раздаётся ScreenShake-ам всех vcam). ОБЯЗАТЕЛЕН. " +
                 "Пусто = красная ошибка и тряски нет вовсе (ScreenShake своих чисел не держит).")]
        [SerializeField] private Presentation.Design.CombatFeelConfig _feelConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // Снапшот арены из авторинга в ЭТОЙ (persist) сцене. Бой берёт тот же layout из предка —
            // единый мир, никакого per-battle поиска по сцене в боевом скоупе.
            ArenaLayoutData layout = BuildArenaLayout();
            builder.RegisterInstance(layout);

            // Конфиг тряски: без ассета тряски просто нет (см. ScreenShake) — не «примерно такая».
            var feel = ScopeWiring.Optional(_feelConfig, nameof(WorldLifetimeScope), nameof(_feelConfig),
                "тряски камеры не будет");
            builder.RegisterInstance(feel);

            // Тела на арене вне боя (двор, Ристалище, строй между забегами) и единственный вход показа
            // за кадром сцены. Держим здесь, потому что оба переживают бои: боевая симуляция теперь
            // рождается и умирает вместе с боем, и вечного владельца тел больше нет
            // (решение Макса 02.08.2026, см. журнал «The Simulation Belongs To The Battle»).
            builder.Register<Combat.Tape.WorldBodyStage>(Lifetime.Singleton);
            builder.Register<Combat.Tape.StageFrameRouter>(Lifetime.Singleton);

            // Вне боя камера ни за кем не следует (пустой источник точек фокуса). На входе в бой
            // боевой скоуп переключит источник через CombatFocusTarget.SetSource(живые юниты).
            builder.RegisterInstance<Presentation.IFocusPointSource>(Presentation.EmptyFocusPointSource.Instance);

            // Единая камера-риг (Main Camera + Brain + vcam + focus + controller): резолвится из
            // этой persist-сцены. Держим здесь, чтобы риг пережил смену боевых сцен.
            builder.RegisterComponentInHierarchy<Presentation.CombatFocusTarget>();
            builder.RegisterComponentInHierarchy<Presentation.CameraModeController>()
                   .AsSelf().As<Presentation.IScreenShake>();

            // Обесцвечивание арены: полигон — серая версия той же локации (материал, а не серый дубль тайлов).
            builder.RegisterComponentInHierarchy<Presentation.Arena.ArenaDesaturation>();

            // Смена облика арены поклеточной подменой тайлов. Держим здесь, а не в боевом скоупе:
            // переход обязан доигрывать, даже когда бой уже кончился и его скоуп ушёл.
            builder.RegisterComponentInHierarchy<Presentation.Arena.ArenaSkinSwapper>()
                   .AsSelf().As<IArenaSwap>();

            // Являет место боя на входе в узел: ждёт, пока откроется шторка перехода, и играет проявление.
            builder.RegisterComponentInHierarchy<Presentation.Arena.ArenaStagePresenter>();

            // World-слой карты акта (фаза D): живёт в этой persist-сцене СВОЕЙ зоной, разнесённой от арены
            // (положение объекта в сцене и задаёт, где карта в мире). Себя он привязывает к
            // WorldMapViewLink из корневого скоупа — петля забега висит выше и напрямую его не видит.
            builder.RegisterComponentInHierarchy<Presentation.Map.WorldMapView>();

            // Тумблеры постобработки: Volume зоны карты гасится из общего реестра эффектов (gm_fx).
            builder.RegisterComponentInHierarchy<Presentation.Effects.VolumeVisualToggle>();

            // Стол за главным меню: тот же материал, что под картой акта, — иначе за меню чёрный провал
            // (камера заливает пустоту цветом очистки).
            builder.RegisterComponentInHierarchy<Presentation.Map.MenuBackdropView>();
        }

        private ArenaLayoutData BuildArenaLayout()
        {
            // Авторинг ищем ТОЛЬКО в загруженных сценах; в единой-мировой раскладке он живёт в
            // WorldScene (из BattleScene удалён). Нет авторинга → бесконечное поле без зон.
            var authoring = FindAnyObjectByType<ArenaLayoutAuthoring>();
            if (authoring == null)
            {
                Debug.LogWarning("[WorldLifetimeScope] - ArenaLayoutAuthoring не найден в загруженных сценах → " +
                                 "бесконечное поле без зон (движение/кламп не ограничены).");
                return ArenaLayoutData.Unbounded;
            }
            return authoring.BuildLayout();
        }
    }
}
