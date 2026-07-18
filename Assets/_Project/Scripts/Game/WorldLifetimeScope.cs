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
        [Tooltip("Design-конфиг тряски камеры (раздаётся ScreenShake-ам всех vcam). Пусто = дефолты в рантайме.")]
        [SerializeField] private Presentation.Design.CombatFeelConfig _feelConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // Снапшот арены из авторинга в ЭТОЙ (persist) сцене. Бой берёт тот же layout из предка —
            // единый мир, никакого per-battle FindFirstObjectByType в боевом скоупе.
            ArenaLayoutData layout = BuildArenaLayout();
            builder.RegisterInstance(layout);

            // Конфиг тряски: если ассет не назначен — рантайм-инстанс с дефолтами (камера не падает).
            var feel = _feelConfig != null
                ? _feelConfig
                : ScriptableObject.CreateInstance<Presentation.Design.CombatFeelConfig>();
            builder.RegisterInstance(feel);

            // Вне боя камера ни за кем не следует (пустой источник точек фокуса). На входе в бой
            // боевой скоуп переключит источник через CombatFocusTarget.SetSource(живые юниты).
            builder.RegisterInstance<Presentation.IFocusPointSource>(Presentation.EmptyFocusPointSource.Instance);

            // Единая камера-риг (Main Camera + Brain + vcam + focus + controller): резолвится из
            // этой persist-сцены. Держим здесь, чтобы риг пережил смену боевых сцен.
            builder.RegisterComponentInHierarchy<Presentation.CombatFocusTarget>();
            builder.RegisterComponentInHierarchy<Presentation.CameraModeController>()
                   .AsSelf().As<Presentation.IScreenShake>();
        }

        private ArenaLayoutData BuildArenaLayout()
        {
            // Авторинг ищем ТОЛЬКО в загруженных сценах; в единой-мировой раскладке он живёт в
            // WorldScene (из BattleScene удалён). Нет авторинга → бесконечное поле без зон.
            var authoring = FindFirstObjectByType<ArenaLayoutAuthoring>();
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
