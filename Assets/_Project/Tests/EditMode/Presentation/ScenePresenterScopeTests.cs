using System;
using System.Collections.Generic;
using System.Reflection;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Presentation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Сценные презентеры боя принадлежат МИРУ, а боевые внутренности получают по границе боя.
    /// </summary>
    /// <remarks>
    /// Инвариант кросс-файловый и ломается тихо: объект в персист-сцене переживает бои, а инъекция в
    /// него случается ровно один раз. Стоит вернуть боевую зависимость в <c>[Inject]</c>-метод — и
    /// презентер до конца сессии будет держать симуляцию ПЕРВОГО боя: второй бой отрисуется мёртвыми
    /// ссылками, и заметно это станет не сразу, а на втором узле забега.
    /// </remarks>
    public sealed class ScenePresenterScopeTests
    {
        /// <summary>Живёт и умирает вместе с боем — в инъекцию сценного объекта такому нельзя.</summary>
        private static readonly Type[] BattleOwned =
        {
            typeof(CombatSimulation),
            typeof(SpatialHash),
            typeof(EncounterLoader),
            typeof(RuntimeUnitFactory),
            typeof(BattleTape),
            typeof(BattleTapePlayback),
            typeof(BattleTapeRecorder),
            typeof(BattleTapeDispatcher),
            typeof(BattleUnitRegistry),
            typeof(DevOverlayMode),
        };

        private static readonly Type[] ScenePresenters =
        {
            typeof(CombatPresenter),
            typeof(CombatDebugDraw),
            typeof(CombatAreaFlash),
        };

        [Test]
        public void ScenePresenters_DoNotInjectBattleInternals()
        {
            var offences = new List<string>();

            foreach (Type presenter in ScenePresenters)
                foreach (MethodInfo method in presenter.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                                   | BindingFlags.Instance))
                {
                    // Атрибут ищем по имени: тестовая сборка на VContainer не ссылается, и заводить
                    // эту ссылку ради одного типа значило бы тянуть DI-контейнер в тесты рефлексии.
                    if (!HasInject(method)) continue;

                    foreach (ParameterInfo p in method.GetParameters())
                        if (Array.IndexOf(BattleOwned, p.ParameterType) >= 0)
                            offences.Add($"{presenter.Name}.{method.Name}({p.ParameterType.Name} {p.Name})");
                }

            Assert.IsEmpty(offences,
                "Боевое приходит по границе боя (BindBattle), а не инъекцией — иначе презентер до конца "
                + "сессии держит внутренности первого боя:\n" + string.Join("\n", offences));
        }

        private static bool HasInject(MethodInfo method)
        {
            foreach (object attribute in method.GetCustomAttributes(inherit: true))
                if (attribute.GetType().Name == "InjectAttribute") return true;
            return false;
        }

        [Test]
        public void ScenePresenters_HaveTheBattleBoundary()
        {
            foreach (Type presenter in ScenePresenters)
            {
                Assert.IsNotNull(presenter.GetMethod("BindBattle"),
                    $"{presenter.Name} обязан принимать бой явно");
                Assert.IsNotNull(presenter.GetMethod("UnbindBattle"),
                    $"{presenter.Name} обязан уметь отпустить бой: без этого он переживёт его со "
                    + "ссылками на мёртвое");
            }
        }
    }
}
