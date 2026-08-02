using System.Collections;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Activity;
using Guildmaster.Presentation.Arena;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Цикл жизни мероприятия: открыли — место есть, закрыли — места нет, открыли снова — место
    /// собирается ЗАНОВО. Гоняется на настоящем буте, потому что проверяет ровно то, что ломалось только
    /// в живой игре.
    /// </summary>
    /// <remarks>
    /// <b>Зачем этот тест существует.</b> 02.08.2026 за один вечер четырежды повторился один и тот же
    /// класс дефекта: состояние, накопленное показом или консолью, переживало своего владельца. Симптом
    /// каждый раз выглядел как «работает со второго раза» или «перестало после нескольких переходов», и
    /// каждый раз ловился только руками в play-mode. Два из тех багов (залипшая серость и пропавшая
    /// сборка арены) этот тест поймал бы сразу.
    /// <para>EditMode здесь бессилен: проверяется не логика класса, а то, что живёт между скоупами,
    /// сценой и компонентами показа.</para>
    /// </remarks>
    public sealed class ActivityLifeCycleTest
    {
        private const string EmptySkin = "__empty";
        private const float  BootTimeout = 20f;

        [UnityTest]
        public IEnumerator Activity_OpenedTwice_RebuildsThePlaceEachTime()
        {
            yield return LoadGame();

            // Ищем объектом, а не LifetimeScope.Find: тот отдаёт базовый тип, и корень пришлось бы
            // кастовать обратно.
            var root = Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "корневой скоуп не поднялся");

            var activities = root.Container.Resolve<ActivityHost>();
            var view       = root.Container.Resolve<IActivityView>();

            var swapper      = Object.FindAnyObjectByType<ArenaSkinSwapper>();
            var desaturation = Object.FindAnyObjectByType<ArenaDesaturation>();
            Assert.IsNotNull(swapper,      "в мире нет свопера обликов арены");
            Assert.IsNotNull(desaturation, "в мире нет обесцвечивания арены");

            // Вне мероприятия места нет: ни тайлов, ни серости.
            Assert.AreEqual(ActivityKind.None, view.Current.Kind, "мероприятие открылось само");
            Assert.AreEqual(EmptySkin, swapper.CurrentSkinId, "арена стоит готовой до всякого мероприятия");
            Assert.IsFalse(desaturation.IsGrey, "серость держится вне мероприятия");

            // Первый заход на площадку: место появляется.
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(4);

            Assert.AreEqual(ActivityKind.ProvingGrounds, view.Current.Kind);
            Assert.IsTrue(desaturation.IsGrey, "площадка открылась, но арена не серая");

            // Выход: место обязано уйти ЦЕЛИКОМ. Половина сброса — это ложное «всё уже так, как просят»
            // для идемпотентного входа, и второй заход пройдёт молча.
            activities.Close();
            yield return WaitFrames(2);

            Assert.AreEqual(ActivityKind.None, view.Current.Kind, "мероприятие пережило закрытие");
            Assert.AreEqual(EmptySkin, swapper.CurrentSkinId, "тайлы арены пережили мероприятие");
            Assert.IsFalse(desaturation.IsGrey, "серость пережила мероприятие — второй вход пройдёт молча");

            // Второй заход тем же путём: место собирается заново, а не «уже показано».
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(4);

            Assert.AreEqual(ActivityKind.ProvingGrounds, view.Current.Kind);
            Assert.IsTrue(desaturation.IsGrey, "на втором заходе площадка не встала — сборку пропустили");

            activities.Close();
            yield return WaitFrames(2);
        }

        /// <summary>
        /// Поднять игру так, как она поднимается у игрока: корневая сцена сама грузит мир и боевые
        /// системы. Ждём именно мир — до него ни скоупов, ни арены не существует.
        /// </summary>
        private static IEnumerator LoadGame()
        {
            yield return SceneManager.LoadSceneAsync("CoreScene", LoadSceneMode.Single);

            float deadline = Time.realtimeSinceStartup + BootTimeout;
            while (Object.FindAnyObjectByType<WorldLifetimeScope>() == null)
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"мир не поднялся за {BootTimeout} с — бут сломан");
                yield return null;
            }

            yield return WaitFrames(2);
        }

        /// <summary>
        /// Подождать несколько кадров. Именно кадров, а не секунд: VContainer диспатчит энтрипоинты
        /// отдельной фазой, и всё, что случается «на входе в место», происходит не в тот же вызов.
        /// </summary>
        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }
    }
}
